using System;

namespace Snake.EnvServer.Game.Obs
{
    public readonly record struct Snapshot(
        int Cols,
        int Rows,
        Snake.EnvServer.Game.Pt Head,
        Snake.EnvServer.Game.Dir Direction,
        Snake.EnvServer.Game.Pt Food,
        Snake.EnvServer.Game.Pt[] Body // excludes head
    )
    {
        public bool In(Snake.EnvServer.Game.Pt p) => p.X >= 0 && p.X < Cols && p.Y >= 0 && p.Y < Rows;

        public bool Occ(Snake.EnvServer.Game.Pt p)
        {
            if (p.X == Head.X && p.Y == Head.Y) return true;
            foreach (var b in Body) if (b.X == p.X && b.Y == p.Y) return true;
            return false;
        }

        public static (int dx, int dy) Vec(Snake.EnvServer.Game.Dir d) => d switch
        {
            Snake.EnvServer.Game.Dir.RIGHT => (1, 0),
            Snake.EnvServer.Game.Dir.LEFT  => (-1, 0),
            Snake.EnvServer.Game.Dir.DOWN  => (0, 1),
            _                               => (0, -1), // UP
        };

        public static Snake.EnvServer.Game.Dir RotLeft (Snake.EnvServer.Game.Dir d) => (Snake.EnvServer.Game.Dir)(((int)d + 3) & 3);
        public static Snake.EnvServer.Game.Dir RotRight(Snake.EnvServer.Game.Dir d) => (Snake.EnvServer.Game.Dir)(((int)d + 1) & 3);

        // Ego <-> World transforms under current Direction
        public (int wx, int wy) EgoToWorld(int ex, int ey) => Direction switch
        {
            Snake.EnvServer.Game.Dir.UP    => (ex, ey),
            Snake.EnvServer.Game.Dir.RIGHT => (-ey, ex),
            Snake.EnvServer.Game.Dir.DOWN  => (-ex, -ey),
            Snake.EnvServer.Game.Dir.LEFT  => (ey, -ex),
            _                               => (ex, ey)
        };

        public (int ex, int ey) WorldToEgo(int dx, int dy) => Direction switch
        {
            Snake.EnvServer.Game.Dir.UP    => (dx, dy),
            Snake.EnvServer.Game.Dir.RIGHT => (dy, -dx),
            Snake.EnvServer.Game.Dir.DOWN  => (-dx, -dy),
            Snake.EnvServer.Game.Dir.LEFT  => (-dy, dx),
            _                               => (dx, dy)
        };

        public int Cheb(Snake.EnvServer.Game.Pt a, Snake.EnvServer.Game.Pt b)
            => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
