namespace Snake.Core.Game.Obs;

public static class Dense28EgoEncoder
{
    // 24 egocentric cells of 5x5 ring (front→back, left→right) + 4 food bits [ahead, behind, left, right]
    public static float[] Encode(Snapshot s)
    {
        Span<float> v = stackalloc float[28];
        int k = 0;

        for (int ey = -2; ey <= 2; ey++)
        {
            for (int ex = -2; ex <= 2; ex++)
            {
                if (ex == 0 && ey == 0) continue; // skip head

                // convert ego offset -> world
                var (wx, wy) = s.EgoToWorld(ex, ey);
                var p = new Snake.Core.Game.Pt(s.Head.X + wx, s.Head.Y + wy);

                float blocked = 1f;
                if (s.In(p)) blocked = s.Occ(p) ? 1f : 0f; // 1 = blocked, 0 = free
                v[k++] = blocked;
            }
        }

        // food in ego frame
        var dx = s.Food.X - s.Head.X;
        var dy = s.Food.Y - s.Head.Y;
        var (fx, fy) = s.WorldToEgo(dx, dy);

        v[k++] = (fy < 0) ? 1f : 0f; // ahead
        v[k++] = (fy > 0) ? 1f : 0f; // behind
        v[k++] = (fx < 0) ? 1f : 0f; // left
        v[k++] = (fx > 0) ? 1f : 0f; // right

        return v.ToArray();
    }
}
