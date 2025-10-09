namespace Snake.Core.Game;

public sealed class Env
{
    private readonly int _cols, _rows, _timeoutMult;
    private readonly bool[] _occ;
    private Random _rng;
    private Pt _head, _food;
    private readonly LinkedList<Pt> _body = new(); // excludes head
    private Dir _dir = Dir.RIGHT;
    private int _steps, _score;

    public Env(int cols, int rows, int timeoutMult)
    {
        _cols = cols; _rows = rows; _timeoutMult = timeoutMult;
        _occ = new bool[_cols * _rows];
        _rng = new Random(1);
        Reset();
    }

    public void Seed(ulong s) =>
        _rng = new Random(unchecked((int)(s == 0 ? 1UL : s)));

    public void Reset()
    {
        Array.Fill(_occ, false);
        _steps = 0; _score = 0; _dir = Dir.RIGHT;
        _head = new Pt(_cols / 2, _rows / 2);
        _body.Clear();
        _body.AddLast(new Pt(_head.X - 1, _head.Y));
        _body.AddLast(new Pt(_head.X - 2, _head.Y));
        SetOcc(_head, true);
        foreach (var b in _body) SetOcc(b, true);
        PlaceFood();
    }

    public readonly record struct StepOut(float[] Signals, bool Done, int Score, int Length, string Death, int Steps);

    // action: 0 straight, 1 right, 2 left
    public StepOut Step(int action)
    {
        bool turning = action != 0;
        _dir = action switch
        {
            1 => (Dir)(((int)_dir + 1) & 3),
            2 => (Dir)(((int)_dir + 3) & 3),
            _ => _dir
        };

        int prevCheb = Cheb(_head, _food);

        var next = _dir switch
        {
            Dir.RIGHT => _head with { X = _head.X + 1 },
            Dir.LEFT  => _head with { X = _head.X - 1 },
            Dir.DOWN  => _head with { Y = _head.Y + 1 },
            _         => _head with { Y = _head.Y - 1 },
        };

        _steps++;
        bool timeout = _steps > _timeoutMult * Math.Max(1, _body.Count + 1);
        bool oob = !In(next);
        bool self = !oob && Occ(next);

        if (timeout || oob || self)
        {
            return new StepOut(
                Signals: new float[] { 0f, 1f, 1f, 0f, turning ? 1f : 0f, timeout ? 1f : 0f },
                Done: true,
                Score: _score,
                Length: _body.Count + 1,
                Death: timeout ? "timeout" : (oob ? "wall" : "self"),
                Steps: _steps
            );
        }

        // normal move
        _body.AddFirst(_head);
        SetOcc(_head, true);
        _head = next;

        bool ate = _head.X == _food.X && _head.Y == _food.Y;
        if (ate)
        {
            _score++;
            SetOcc(_head, true);
            PlaceFood();
        }
        else
        {
            var tail = _body.Last!.Value; _body.RemoveLast();
            SetOcc(tail, false);
            SetOcc(_head, true);
        }

        int newCheb = Cheb(_head, _food);
        float toward = (prevCheb - newCheb) / (float)Math.Max(_cols, _rows);

        return new StepOut(
            Signals: new float[] { ate ? 1f : 0f, 0f, 1f, toward, turning ? 1f : 0f, 0f },
            Done: false,
            Score: _score,
            Length: _body.Count + 1,
            Death: "",
            Steps: _steps
        );
    }

    // Accessors
    public int Cols => _cols;
    public int Rows => _rows;
    public int TimeoutMult => _timeoutMult;
    public int Steps => _steps;
    public int Score => _score;
    public Dir Direction => _dir;
    public Pt Head => _head;
    public Pt Food => _food;
    public IEnumerable<Pt> Body => _body; // excludes head

    // Snapshot for encoders
    public Obs.Snapshot Snapshot()
        => new(Cols, Rows, Head, Direction, Food, _body.ToArray());

    // Helpers
    private bool In(Pt p) => p.X >= 0 && p.X < _cols && p.Y >= 0 && p.Y < _rows;
    private int Idx(Pt p) => p.Y * _cols + p.X;
    private bool Occ(Pt p) => _occ[Idx(p)];
    private void SetOcc(Pt p, bool v) => _occ[Idx(p)] = v;
    private int Cheb(Pt a, Pt b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void PlaceFood()
    {
        while (true)
        {
            var f = new Pt(_rng.Next(0, _cols), _rng.Next(0, _rows));
            if (!Occ(f) && !(f.X == _head.X && f.Y == _head.Y)) { _food = f; return; }
        }
    }
}
