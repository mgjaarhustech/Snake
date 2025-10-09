namespace Snake.Core.Game.Obs;

public static class Dense32Encoder
{
    // Same as the original: 24 ring cells (5x5 around head, skipping center) + dir one-hot + 4 food bits
    public static float[] Encode(Snapshot s)
    {
        Span<float> v = stackalloc float[32];
        int k = 0;

        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx == 0 && dy == 0) continue;

                var p = new Snake.Core.Game.Pt(s.Head.X + dx, s.Head.Y + dy);
                float blocked = 1f;
                if (s.In(p)) blocked = s.Occ(p) ? 1f : 0f;
                v[k++] = blocked;
            }
        }

        // dir one-hot [L,R,U,D]
        v[k++] = s.Direction == Snake.Core.Game.Dir.LEFT  ? 1f : 0f;
        v[k++] = s.Direction == Snake.Core.Game.Dir.RIGHT ? 1f : 0f;
        v[k++] = s.Direction == Snake.Core.Game.Dir.UP    ? 1f : 0f;
        v[k++] = s.Direction == Snake.Core.Game.Dir.DOWN  ? 1f : 0f;

        // food bits (world)
        v[k++] = s.Food.X < s.Head.X ? 1f : 0f;
        v[k++] = s.Food.X > s.Head.X ? 1f : 0f;
        v[k++] = s.Food.Y < s.Head.Y ? 1f : 0f;
        v[k++] = s.Food.Y > s.Head.Y ? 1f : 0f;

        return v.ToArray();
    }
}
