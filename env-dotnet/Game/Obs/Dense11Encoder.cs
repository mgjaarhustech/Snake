using System;
using Snake.EnvServer.Game.Obs;
using Pb = Snake.V1;

namespace Snake.EnvServer.Game.Obs;

public sealed class Dense11Encoder : IObsEncoder
{
    public Pb.ObsType Type => Pb.ObsType.Dense11;

    public Pb.Observation Encode(Snapshot s)
    {
        Span<float> v = stackalloc float[11];
        int k = 0;

        // 3 danger flags: [straight, right, left]
        var (fDx,fDy) = Snapshot.Vec(s.Direction);
        var (rDx,rDy) = Snapshot.Vec(Snapshot.RotRight(s.Direction));
        var (lDx,lDy) = Snapshot.Vec(Snapshot.RotLeft(s.Direction));

        var pF = new Pt(s.Head.X + fDx, s.Head.Y + fDy);
        var pR = new Pt(s.Head.X + rDx, s.Head.Y + rDy);
        var pL = new Pt(s.Head.X + lDx, s.Head.Y + lDy);

        v[k++] = (!s.In(pF) || s.Occ(pF)) ? 1f : 0f;
        v[k++] = (!s.In(pR) || s.Occ(pR)) ? 1f : 0f;
        v[k++] = (!s.In(pL) || s.Occ(pL)) ? 1f : 0f;

        // 4 dir [L,R,U,D]
        v[k++] = s.Direction == Dir.LEFT  ? 1f : 0f;
        v[k++] = s.Direction == Dir.RIGHT ? 1f : 0f;
        v[k++] = s.Direction == Dir.UP    ? 1f : 0f;
        v[k++] = s.Direction == Dir.DOWN  ? 1f : 0f;

        // 4 food bits [left,right,up,down]
        v[k++] = s.Food.X < s.Head.X ? 1f : 0f;
        v[k++] = s.Food.X > s.Head.X ? 1f : 0f;
        v[k++] = s.Food.Y < s.Head.Y ? 1f : 0f;
        v[k++] = s.Food.Y > s.Head.Y ? 1f : 0f;

        var dense = new Pb.Dense(); dense.Data.AddRange(v.ToArray());
        return new Pb.Observation { Type = Type, Dense = dense };
    }
}
