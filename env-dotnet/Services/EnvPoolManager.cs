using Snake.Core.Game;
using Snake.Core.Game.Obs;
using Pb = Snake.V1;

namespace Snake.EnvServer.Services;

public sealed class EnvPoolManager
{
    private sealed class Slot
    {
        public Env Env { get; }
        public bool Dead { get; set; }
        public Pb.StepResponse? Terminal { get; set; } // last real terminal response
        public Slot(Env env) { Env = env; }
    }

    private sealed class Pool
    {
        public Pb.ObsType ObsType { get; }
        public List<Slot> Slots { get; }
        public Pool(Pb.ObsType t, List<Slot> s) { ObsType = t; Slots = s; }
    }

    private readonly Dictionary<string, Pool> _pools = new();
    private readonly object _gate = new();

    // ---------- helpers ----------
    private static Pb.RawState BuildRawState(Env env)
    {
        var rc = RawStateEncoder.From(env);
        var raw = new Pb.RawState
        {
            Cols = rc.Cols,
            Rows = rc.Rows,
            Step = rc.Step,
            Head = new Pb.Point { X = rc.Head.X, Y = rc.Head.Y },
            Dir  = rc.Direction.ToString(),
            Food = new Pb.Point { X = rc.Food.X, Y = rc.Food.Y }
        };
        foreach (var b in rc.Body) raw.Body.Add(new Pb.Point { X = b.X, Y = b.Y });
        return raw;
    }

    // Build one StepResponse (alive) using encoders from core
    private static Pb.StepResponse BuildAlive(Env env, Pb.ObsType obsType, Env.StepOut? s, bool withRaw)
    {
        var res = new Pb.StepResponse
        {
            Obs    = new Pb.Observation { Type = obsType },
            Done   = s?.Done   ?? false,
            Score  = s?.Score  ?? env.Score,
            Length = s?.Length ?? (env.Body.Count() + 1),
            Death  = s?.Death  ?? "",
            Steps  = s?.Steps  ?? env.Steps
        };
        res.Signals.AddRange(s?.Signals ?? new float[] { 0,0,0,0,0,0 });

        if (obsType == Pb.ObsType.RawState)
        {
            var raw = BuildRawState(env);
            res.Obs.Raw = raw;
            if (withRaw) res.RawForRender = raw;
        }
        else
        {
            var snap = env.Snapshot();
            float[] dense = obsType switch
            {
                Pb.ObsType.Dense11    => Dense11Encoder.Encode(snap),
                Pb.ObsType.Dense28Ego => Dense28EgoEncoder.Encode(snap),
                Pb.ObsType.Raycasts19 => Raycasts19Encoder.Encode(snap),
                Pb.ObsType.Dense32    => Dense32Encoder.Encode(snap),
                _ => throw new NotSupportedException($"ObsType {obsType} not supported")
            };
            res.Obs.Dense = new Pb.Dense(); 
            res.Obs.Dense.Data.AddRange(dense);

            if (withRaw) res.RawForRender = BuildRawState(env);
        }

        return res;
    }

    // "Echo" a dead env on subsequent calls (signals all zeros; obs repeated).
    // If withRaw=true, include latest raw frame for rendering.
    private static Pb.StepResponse EchoTerminal(Pb.ObsType obsType, Slot slot, bool zeroDense, bool withRaw)
    {
        var terminal = slot.Terminal!;
        var res = new Pb.StepResponse
        {
            Obs    = new Pb.Observation { Type = obsType },
            Done   = true,
            Score  = terminal.Score,
            Length = terminal.Length,
            Death  = terminal.Death,
            Steps  = terminal.Steps
        };
        res.Signals.AddRange(new float[] { 0,0,0,0,0,0 });

        if (obsType == Pb.ObsType.RawState && terminal.Obs.Raw is not null)
        {
            // Repeat the last raw obs
            var raw = new Pb.RawState
            {
                Cols = terminal.Obs.Raw.Cols,
                Rows = terminal.Obs.Raw.Rows,
                Step = terminal.Obs.Raw.Step,
                Head = terminal.Obs.Raw.Head,
                Dir  = terminal.Obs.Raw.Dir,
                Food = terminal.Obs.Raw.Food
            };
            raw.Body.AddRange(terminal.Obs.Raw.Body);
            res.Obs.Raw = raw;
        }
        else if (terminal.Obs.Dense is not null)
        {
            var data = terminal.Obs.Dense.Data;
            res.Obs.Dense = new Pb.Dense();
            if (zeroDense) res.Obs.Dense.Data.AddRange(Enumerable.Repeat(0f, data.Count));
            else           res.Obs.Dense.Data.AddRange(data);
        }

        if (withRaw)
        {
            // Provide a current raw snapshot so the client can render, even while echoing.
            res.RawForRender = BuildRawState(slot.Env);
        }

        return res;
    }

    // Create or replace a pool; returns (session, initial responses)
    public (string session, List<Pb.StepResponse> envs) ResetMany(
        int count,
        Pb.ObsType obsType,
        IReadOnlyList<ulong>? seeds,
        int cols,
        int rows,
        int timeoutMult,
        string? session = null,
        bool withRaw = false
    )
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

        lock (_gate)
        {
            var id = string.IsNullOrWhiteSpace(session) ? Guid.NewGuid().ToString("N") : session;

            // Build slots
            var slots = new List<Slot>(count);
            for (int i = 0; i < count; i++)
            {
                var env = new Env(cols, rows, timeoutMult);
                var seed = (seeds != null && i < seeds.Count) ? seeds[i] : (ulong)(i + 1);
                env.Seed(seed);
                env.Reset();
                slots.Add(new Slot(env));
            }

            var pool = new Pool(obsType, slots);
            _pools[id] = pool;

            // Initial observations (done=false)
            var outList = new List<Pb.StepResponse>(count);
            foreach (var s in slots)
            {
                var r = BuildAlive(s.Env, obsType, null, withRaw);
                outList.Add(r);
            }
            return (id, outList);
        }
    }

    // Step the whole pool; broadcast if actions.Count==1
    public List<Pb.StepResponse> StepMany(string session, IReadOnlyList<int> actions, bool zeroDenseEcho = false, bool withRaw = false)
    {
        lock (_gate)
        {
            if (!_pools.TryGetValue(session, out var pool))
                throw new KeyNotFoundException($"Session '{session}' not found.");

            var n = pool.Slots.Count;
            if (actions.Count != 1 && actions.Count != n)
                throw new ArgumentException($"actions length must be 1 (broadcast) or {n}.");

            var res = new List<Pb.StepResponse>(n);
            for (int i = 0; i < n; i++)
            {
                var slot = pool.Slots[i];

                if (slot.Dead)
                {
                    res.Add(EchoTerminal(pool.ObsType, slot, zeroDenseEcho, withRaw));
                    continue;
                }

                int a = actions[actions.Count == 1 ? 0 : i];
                if (a < 0 || a > 2) a = 0;

                var stepOut = slot.Env.Step(a);
                var r = BuildAlive(slot.Env, pool.ObsType, stepOut, withRaw);

                if (r.Done)
                {
                    slot.Dead = true;
                    slot.Terminal = r;  // store terminal to echo later
                }

                res.Add(r);
            }

            return res;
        }
    }
}
