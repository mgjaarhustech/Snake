namespace Snake.Core.Game.Obs;

public readonly record struct Snapshot(
    int Cols,
    int Rows,
    Snake.Core.Game.Pt Head,
    Snake.Core.Game.Dir Direction,
    Snake.Core.Game.Pt Food,
    Snake.Core.Game.Pt[] Body // excludes head
)
{
    public bool In(Snake.Core.Game.Pt p) => p.X >= 0 && p.X < Cols && p.Y >= 0 && p.Y < Rows;

    public bool Occ(Snake.Core.Game.Pt p)
    {
        if (p.X == Head.X && p.Y == Head.Y) return true;
        foreach (var b in Body) if (b.X == p.X && b.Y == p.Y) return true;
        return false;
    }

    public static (int dx, int dy) Vec(Snake.Core.Game.Dir d) => d switch
    {
        Snake.Core.Game.Dir.RIGHT => (1, 0),
        Snake.Core.Game.Dir.LEFT  => (-1, 0),
        Snake.Core.Game.Dir.DOWN  => (0, 1),
        _                         => (0, -1), // UP
    };

    public static Snake.Core.Game.Dir RotLeft(Snake.Core.Game.Dir d)  => (Snake.Core.Game.Dir)(((int)d + 3) & 3);
    public static Snake.Core.Game.Dir RotRight(Snake.Core.Game.Dir d) => (Snake.Core.Game.Dir)(((int)d + 1) & 3);

    public (int wx, int wy) EgoToWorld(int ex, int ey) => Direction switch
    {
        Snake.Core.Game.Dir.UP    => (ex, ey),
        Snake.Core.Game.Dir.RIGHT => (-ey, ex),
        Snake.Core.Game.Dir.DOWN  => (-ex, -ey),
        Snake.Core.Game.Dir.LEFT  => (ey, -ex),
        _                         => (ex, ey)
    };

    public (int ex, int ey) WorldToEgo(int dx, int dy) => Direction switch
    {
        Snake.Core.Game.Dir.UP    => (dx, dy),
        Snake.Core.Game.Dir.RIGHT => (dy, -dx),
        Snake.Core.Game.Dir.DOWN  => (-dx, -dy),
        Snake.Core.Game.Dir.LEFT  => (-dy, dx),
        _                         => (dx, dy)
    };

    public int Cheb(Snake.Core.Game.Pt a, Snake.Core.Game.Pt b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
