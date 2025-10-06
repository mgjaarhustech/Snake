using Snake.EnvServer.Game.Obs;
using Pb = Snake.V1;

namespace Snake.EnvServer.Game.Obs;

public sealed class RawStateEncoder : IObsEncoder
{
    public Pb.ObsType Type => Pb.ObsType.RawState;

    public Pb.Observation Encode(Snapshot s)
    {
        var raw = new Pb.RawState
        {
            Cols = s.Cols,
            Rows = s.Rows,
            Step = 0, // service fills Steps in StepResponse; here we only encode geometry
            Head = new Pb.Point { X = s.Head.X, Y = s.Head.Y },
            Dir  = s.Direction.ToString(),
            Food = new Pb.Point { X = s.Food.X, Y = s.Food.Y }
        };
        foreach (var b in s.Body) raw.Body.Add(new Pb.Point { X = b.X, Y = b.Y });

        return new Pb.Observation { Type = Type, Raw = raw };
    }
}
