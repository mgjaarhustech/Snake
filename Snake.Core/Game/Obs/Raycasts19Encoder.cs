namespace Snake.Core.Game.Obs;

public static class Raycasts19Encoder
{
    // 8 directions (N,NE,E,SE,S,SW,W,NW) * 2 features + 3 food features = 19
    public static float[] Encode(Snapshot s)
    {
        Span<float> v = stackalloc float[19];
        int k = 0;

        (int dx, int dy)[] dirs = new[]
        {
            (0,-1), (1,-1), (1,0), (1,1), (0,1), (-1,1), (-1,0), (-1,-1)
        };

        float GlobalMax(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return 1f;
            if (dx == 0 || dy == 0)
            {
                // axial
                return dx != 0 ? Math.Max(0, s.Cols - 1) : Math.Max(0, s.Rows - 1);
            }
            // diagonal
            return Math.Max(0, Math.Min(s.Cols - 1, s.Rows - 1));
        }

        foreach (var (dx, dy) in dirs)
        {
            int stepsToWall = 0;
            int stepsToBody = -1;

            int x = s.Head.X;
            int y = s.Head.Y;

            while (true)
            {
                x += dx; y += dy;
                var p = new Snake.Core.Game.Pt(x, y);
                if (!s.In(p)) break;

                // count this free step towards wall
                stepsToWall++;

                if (stepsToBody < 0 && s.Occ(p))
                {
                    stepsToBody = stepsToWall; // first body hit at this distance
                }
            }

            if (stepsToBody < 0) stepsToBody = stepsToWall; // no body before wall

            float gmax = GlobalMax(dx, dy);
            float wallFrac = gmax > 0 ? stepsToWall / gmax : 0f;
            float bodyFrac = gmax > 0 ? stepsToBody / gmax : 0f;

            v[k++] = wallFrac;
            v[k++] = bodyFrac;
        }

        // Food features
        float colsDen = Math.Max(1, s.Cols - 1);
        float rowsDen = Math.Max(1, s.Rows - 1);
        float dxn = (s.Food.X - s.Head.X) / colsDen;     // [-1,1]
        float dyn = (s.Food.Y - s.Head.Y) / rowsDen;     // [-1,1]
        float cheb = s.Cheb(s.Head, s.Food) / (float)Math.Max(s.Cols, s.Rows); // [0,1]

        v[k++] = dxn;
        v[k++] = dyn;
        v[k++] = cheb;

        return v.ToArray();
    }
}
