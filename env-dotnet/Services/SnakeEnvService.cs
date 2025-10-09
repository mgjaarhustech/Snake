using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Snake.Core.Game;         // core Env
using Snake.Core.Game.Obs;     // Snapshot + encoders
using Pb = Snake.V1;

namespace Snake.EnvServer.Services;

public sealed class SnakeEnvService : Pb.SnakeEnv.SnakeEnvBase
{
    private readonly Env _env;               // single-env (legacy)
    private readonly EnvPoolManager _pool;   // vector pool
    private readonly object _gate = new();
    private Pb.ObsType _obs = Pb.ObsType.Dense11;

    public SnakeEnvService(Env env, EnvPoolManager pool)
    {
        _env = env;
        _pool = pool;
    }

    public override Task<Pb.Spec> GetSpec(Empty _, ServerCallContext __)
        => Task.FromResult(new Pb.Spec
        {
            Cols = _env.Cols,
            Rows = _env.Rows,
            TimeoutMult = _env.TimeoutMult,
            SupportedObs =
            {
                Pb.ObsType.RawState,
                Pb.ObsType.Dense32,
                Pb.ObsType.Dense11,
                Pb.ObsType.Dense28Ego,
                Pb.ObsType.Raycasts19
            },
            RewardSignals = { "eat_food", "death", "step_cost", "toward_food", "turning", "timeout" }
        });

    // ---------- single-env ----------
    public Pb.StepResponse ResetCore(Pb.ResetRequest req)
    {
        lock (_gate)
        {
            _obs = req.ObsType is Pb.ObsType.RawState or Pb.ObsType.Dense32 or Pb.ObsType.Dense11 or Pb.ObsType.Dense28Ego or Pb.ObsType.Raycasts19
                ? req.ObsType : Pb.ObsType.Dense11;

            _env.Seed(req.Seed);
            _env.Reset();
            var rsp = BuildAlive(_env, _obs, s: null);
            if (req.WithRaw) rsp.RawForRender = BuildRawState(_env);
            return rsp;
        }
    }

    public Pb.StepResponse StepCore(Pb.StepRequest req)
    {
        lock (_gate)
        {
            int a = req.Action;
            if (a < 0 || a > 2) a = 0;

            var s = _env.Step(a);
            var rsp = BuildAlive(_env, _obs, s);
            if (req.WithRaw) rsp.RawForRender = BuildRawState(_env);
            return rsp;
        }
    }

    public override Task<Pb.StepResponse> Reset(Pb.ResetRequest req, ServerCallContext __)
        => Task.FromResult(ResetCore(req));

    public override Task<Pb.StepResponse> Step(Pb.StepRequest req, ServerCallContext __)
        => Task.FromResult(StepCore(req));

    // ---------- pool (vector) ----------
    public override Task<Pb.ManyResponse> ResetMany(Pb.ResetManyRequest req, ServerCallContext __)
    {
        if (req.Count <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "count must be > 0"));

        var (session, envs) = _pool.ResetMany(
            count: req.Count,
            obsType: req.ObsType,
            seeds: req.Seeds,
            cols: _env.Cols, rows: _env.Rows, timeoutMult: _env.TimeoutMult,
            session: string.IsNullOrWhiteSpace(req.Session) ? null : req.Session,
            withRaw: req.WithRaw
        );

        return Task.FromResult(new Pb.ManyResponse { Envs = { envs }, Session = session });
    }

    public override Task<Pb.ManyResponse> StepMany(Pb.StepManyRequest req, ServerCallContext __)
    {
        if (string.IsNullOrWhiteSpace(req.Session))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "session is required"));

        var envs = _pool.StepMany(req.Session, req.Actions, zeroDenseEcho: false, withRaw: req.WithRaw);
        return Task.FromResult(new Pb.ManyResponse { Envs = { envs }, Session = req.Session });
    }

    // ---------- shared builders ----------
    private static Pb.StepResponse BuildAlive(Env env, Pb.ObsType obsType, Env.StepOut? s)
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
        res.Signals.AddRange(s?.Signals ?? new float[] { 0, 0, 0, 0, 0, 0 });

        if (obsType == Pb.ObsType.RawState)
        {
            var raw = BuildRawState(env);
            res.Obs.Raw = raw;
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
                _ => throw new NotSupportedException($"ObsType '{obsType}' not supported")
            };
            res.Obs.Dense = new Pb.Dense();
            res.Obs.Dense.Data.AddRange(dense);
        }

        return res;
    }

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
}
