using System;
using Snake.EnvServer.Game.Obs;
using Pb = Snake.V1;

namespace Snake.EnvServer.Game.Obs;

public sealed class Raycasts19Encoder : IObsEncoder
{
    public Pb.ObsType Type => Pb.ObsType.Raycasts19;

    public Pb.Observation Encode(Snapshot s)
    {
        // Layout (19 floats):
        // 16 ray features (8 directions × [frac_to_wall, frac_to_body]) in order:
        // N, NE, E, SE, S, SW, W, NW; each contributes [wall, body]
        // + 3 food features: [dx_norm, dy_norm, cheb_norm]
        Span<float> v = stackalloc float[19];
        int k = 0;

        (int dx, int dy)[] dirs =
        [
            (0,-1), (1,-1), (1,0), (1,1),
            (0, 1), (-1,1), (-1,0), (-1,-1)
        ];

        foreach (var d in dirs)
        {
            int maxGlobal = MaxStepsToWallGlobal(s, d.dx, d.dy);
            int toWall    = StepsToWallFromHead(s, d.dx, d.dy);
            int? toBody   = StepsToBodyFromHead(s, d.dx, d.dy); // null if none before wall

            v[k++] = maxGlobal == 0 ? 0f : toWall / (float)maxGlobal;        // frac_to_wall ∈ [0,1]
            v[k++] = toBody is null ? 1f : (maxGlobal == 0 ? 0f : Math.Clamp(toBody.Value / (float)maxGlobal, 0f, 1f));
        }

        // Food: dx_norm, dy_norm in [-1,1], and Chebyshev distance normalized ∈ [0,1]
        float dxn = (s.Food.X - s.Head.X) / (float)Math.Max(1, s.Cols - 1);
        float dyn = (s.Food.Y - s.Head.Y) / (float)Math.Max(1, s.Rows - 1);
        float cheb = s.Cheb(s.Head, s.Food) / (float)Math.Max(s.Cols, s.Rows);

        v[k++] = dxn;
        v[k++] = dyn;
        v[k++] = cheb;

        var dense = new Pb.Dense(); dense.Data.AddRange(v.ToArray());
        return new Pb.Observation { Type = Type, Dense = dense };
    }

    private static int MaxStepsToWallGlobal(Snapshot s, int dx, int dy)
    {
        // maximum possible steps to wall for ANY starting cell on this board in this direction
        if (dx == 0 && dy != 0) return s.Rows - 1;
        if (dy == 0 && dx != 0) return s.Cols - 1;
        // diagonal
        return Math.Min(s.Cols - 1, s.Rows - 1);
    }

    private static int StepsToWallFromHead(Snapshot s, int dx, int dy)
    {
        int steps = 0;
        var p = new Pt(s.Head.X + dx, s.Head.Y + dy);
        while (s.In(p)) { steps++; p = new Pt(p.X + dx, p.Y + dy); }
        return steps;
    }

    private static int? StepsToBodyFromHead(Snapshot s, int dx, int dy)
    {
        int steps = 0;
        var p = new Pt(s.Head.X + dx, s.Head.Y + dy);
        while (s.In(p))
        {
            if (s.Occ(p)) return steps; // first occupied cell (body/head)
            steps++;
            p = new Pt(p.X + dx, p.Y + dy);
        }
        return null; // no body before wall
    }
}
