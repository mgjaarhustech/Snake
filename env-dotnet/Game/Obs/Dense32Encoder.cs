using System;
using Snake.EnvServer.Game.Obs;
using Pb = Snake.V1;

namespace Snake.EnvServer.Game.Obs;

public sealed class Dense32Encoder : IObsEncoder
{
    public Pb.ObsType Type => Pb.ObsType.Dense32;

    public Pb.Observation Encode(Snapshot s)
    {
        Span<float> v = stackalloc float[32];
        int k = 0;
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var p = new Pt(s.Head.X + dx, s.Head.Y + dy);
            v[k++] = (!s.In(p) || s.Occ(p)) ? 1f : 0f; // 1 = blocked
        }
        // dir one-hot [L,R,U,D]
        v[k++] = s.Direction == Dir.LEFT  ? 1f : 0f;
        v[k++] = s.Direction == Dir.RIGHT ? 1f : 0f;
        v[k++] = s.Direction == Dir.UP    ? 1f : 0f;
        v[k++] = s.Direction == Dir.DOWN  ? 1f : 0f;
        // food bits [left,right,up,down]
        v[k++] = s.Food.X < s.Head.X ? 1f : 0f;
        v[k++] = s.Food.X > s.Head.X ? 1f : 0f;
        v[k++] = s.Food.Y < s.Head.Y ? 1f : 0f;
        v[k++] = s.Food.Y > s.Head.Y ? 1f : 0f;

        var dense = new Pb.Dense(); dense.Data.AddRange(v.ToArray());
        return new Pb.Observation { Type = Type, Dense = dense };
    }
}
