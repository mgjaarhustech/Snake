using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Snake.EnvServer.Game;
using Snake.EnvServer.Game.Obs;
using Pb = Snake.V1;

namespace Snake.EnvServer.Services;

public sealed class SnakeEnvService : Pb.SnakeEnv.SnakeEnvBase
{
    private readonly Env _env;
    private readonly object _gate = new();
    private Pb.ObsType _obs = Pb.ObsType.Dense32;

    // Encoder registry
    private readonly Dictionary<Pb.ObsType, IObsEncoder> _encoders;

    public SnakeEnvService(Env env)
    {
        _env = env;
        _encoders = new()
        {
            { Pb.ObsType.RawState,     new RawStateEncoder()     },
            { Pb.ObsType.Dense32,      new Dense32Encoder()      },
            { Pb.ObsType.Dense11,      new Dense11Encoder()      },
            { Pb.ObsType.Dense28Ego,   new Dense28EgoEncoder()   },
            { Pb.ObsType.Raycasts19,   new Raycasts19Encoder()   },
        };
    }

    public override Task<Pb.Spec> GetSpec(Empty _, ServerCallContext __)
        => Task.FromResult(new Pb.Spec
        {
            Cols = _env.Cols,
            Rows = _env.Rows,
            TimeoutMult = _env.TimeoutMult,
            SupportedObs = { _encoders.Keys },
            RewardSignals = { "eat_food", "death", "step_cost", "toward_food", "turning", "timeout" }
        });

    // -------- Core helpers --------
    public Pb.StepResponse ResetCore(Pb.ResetRequest req)
    {
        lock (_gate)
        {
            _obs = _encoders.ContainsKey(req.ObsType) ? req.ObsType : Pb.ObsType.Dense32;
            _env.Seed(req.Seed);
            _env.Reset();
            return BuildResponse(null);
        }
    }

    public Pb.StepResponse StepCore(Pb.StepRequest req)
    {
        lock (_gate)
        {
            int a = req.Action switch
            {
                Pb.Action.TurnRight => 1,
                Pb.Action.TurnLeft  => 2,
                _ => 0
            };
            var s = _env.Step(a);
            return BuildResponse(s);
        }
    }

    public override Task<Pb.StepResponse> Reset(Pb.ResetRequest req, ServerCallContext __)
        => Task.FromResult(ResetCore(req));

    public override Task<Pb.StepResponse> Step(Pb.StepRequest req, ServerCallContext __)
        => Task.FromResult(StepCore(req));

    public override Task<Pb.ManyResponse> ResetMany(Pb.ResetManyRequest req, ServerCallContext __)
    {
        lock (_gate)
        {
            _obs = _encoders.ContainsKey(req.ObsType) ? req.ObsType : Pb.ObsType.Dense32;
            _env.Seed(req.Seeds.Count > 0 ? req.Seeds[0] : 1);
            _env.Reset();
            return Task.FromResult(new Pb.ManyResponse { Envs = { BuildResponse(null) } });
        }
    }

    public override Task<Pb.ManyResponse> StepMany(Pb.StepManyRequest req, ServerCallContext __)
    {
        var a0 = req.Actions.Count > 0 ? req.Actions[0] : Pb.Action.Straight;
        var one = StepCore(new Pb.StepRequest { Action = a0 });
        return Task.FromResult(new Pb.ManyResponse { Envs = { one } });
    }

    private Pb.StepResponse BuildResponse(Env.StepOut? s)
    {
        var snap = _env.Snapshot();
        var obs  = _encoders.TryGetValue(_obs, out var enc) ? enc.Encode(snap) : _encoders[Pb.ObsType.Dense32].Encode(snap);

        var res = new Pb.StepResponse
        {
            Obs    = obs,
            Done   = s?.Done ?? false,
            Score  = s?.Score ?? _env.Score,
            Length = s?.Length ?? (_env.Body.Count() + 1),
            Death  = s?.Death ?? "",
            Steps  = s?.Steps ?? _env.Steps
        };
        res.Signals.AddRange(s is null ? new float[] { 0,0,0,0,0,0 } : s.Signals);
        return res;
    }
}
