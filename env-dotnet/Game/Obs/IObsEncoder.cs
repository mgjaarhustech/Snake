using Snake.EnvServer.Game.Obs;
using Pb = Snake.V1;

namespace Snake.EnvServer.Game.Obs;

public interface IObsEncoder
{
    Pb.ObsType Type { get; }
    Pb.Observation Encode(Snapshot s);
}
