namespace Snake.Core.Game.Obs;

// Core-side raw state (no protobuf here)
public readonly record struct RawStateCore(
    int Cols,
    int Rows,
    int Step,
    Snake.Core.Game.Pt Head,
    Snake.Core.Game.Dir Direction,
    Snake.Core.Game.Pt[] Body, // excludes head
    Snake.Core.Game.Pt Food
);

public static class RawStateEncoder
{
    // Easiest: build directly from Env (has Steps)
    public static RawStateCore From( Snake.Core.Game.Env env )
        => new RawStateCore(
            Cols: env.Cols,
            Rows: env.Rows,
            Step: env.Steps,
            Head: env.Head,
            Direction: env.Direction,
            Body: env.Body.ToArray(),
            Food: env.Food
        );

    // Optional overload: from Snapshot (+ explicit step)
    public static RawStateCore Encode( Snapshot s, int step )
        => new RawStateCore(
            Cols: s.Cols,
            Rows: s.Rows,
            Step: step,
            Head: s.Head,
            Direction: s.Direction,
            Body: s.Body,
            Food: s.Food
        );
}
