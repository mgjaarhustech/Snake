namespace Snake.Core.Game.Obs;

public static class Dense11Encoder
{
    // Layout: [danger_straight, danger_right, danger_left, dirL, dirR, dirU, dirD, foodL, foodR, foodU, foodD]
    public static float[] Encode(Snapshot s)
    {
        Span<float> v = stackalloc float[11];
        int k = 0;

        // relative directions
        var (fx, fy) = Snapshot.Vec(s.Direction);
        var (rx, ry) = Snapshot.Vec(Snapshot.RotRight(s.Direction));
        var (lx, ly) = Snapshot.Vec(Snapshot.RotLeft(s.Direction));

        bool DangerAt(int dx, int dy)
        {
            var p = new Snake.Core.Game.Pt(s.Head.X + dx, s.Head.Y + dy);
            return !s.In(p) || s.Occ(p);
        }

        v[k++] = DangerAt(fx, fy) ? 1f : 0f;  // straight
        v[k++] = DangerAt(rx, ry) ? 1f : 0f;  // right
        v[k++] = DangerAt(lx, ly) ? 1f : 0f;  // left

        // dir one-hot [L,R,U,D]
        v[k++] = s.Direction == Snake.Core.Game.Dir.LEFT  ? 1f : 0f;
        v[k++] = s.Direction == Snake.Core.Game.Dir.RIGHT ? 1f : 0f;
        v[k++] = s.Direction == Snake.Core.Game.Dir.UP    ? 1f : 0f;
        v[k++] = s.Direction == Snake.Core.Game.Dir.DOWN  ? 1f : 0f;

        // food bits (world-relative)
        v[k++] = s.Food.X < s.Head.X ? 1f : 0f;
        v[k++] = s.Food.X > s.Head.X ? 1f : 0f;
        v[k++] = s.Food.Y < s.Head.Y ? 1f : 0f;
        v[k++] = s.Food.Y > s.Head.Y ? 1f : 0f;

        return v.ToArray();
    }
}
