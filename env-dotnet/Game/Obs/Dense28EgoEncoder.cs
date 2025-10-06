using System;
using Snake.EnvServer.Game.Obs;
using Pb = Snake.V1;

namespace Snake.EnvServer.Game.Obs;

public sealed class Dense28EgoEncoder : IObsEncoder
{
    public Pb.ObsType Type => Pb.ObsType.Dense28Ego;

    public Pb.Observation Encode(Snapshot s)
    {
        Span<float> v = stackalloc float[28];
        int k = 0;

        // 24 ego cells: rows front(ey=-2) -> back(ey=+2); cols left(ex=-2)->right(ex=+2); skip center
        for (int ey = -2; ey <= 2; ey++)
        for (int ex = -2; ex <= 2; ex++)
        {
            if (ex == 0 && ey == 0) continue;
            var (wx, wy) = s.EgoToWorld(ex, ey);
            var p = new Pt(s.Head.X + wx, s.Head.Y + wy);
            v[k++] = (!s.In(p) || s.Occ(p)) ? 1f : 0f;
        }

        // 4 food bits relative to heading: [ahead, behind, left, right]
        var (dx, dy) = (s.Food.X - s.Head.X, s.Food.Y - s.Head.Y);
        var (fx, fy) = s.WorldToEgo(dx, dy);
        v[k++] = fy < 0 ? 1f : 0f; // ahead
        v[k++] = fy > 0 ? 1f : 0f; // behind
        v[k++] = fx < 0 ? 1f : 0f; // left
        v[k++] = fx > 0 ? 1f : 0f; // right

        var dense = new Pb.Dense(); dense.Data.AddRange(v.ToArray());
        return new Pb.Observation { Type = Type, Dense = dense };
    }
}
