namespace Snake.Core.Game;

// File-scoped namespaces across the core to avoid mixing styles.

public enum Dir { RIGHT = 0, DOWN = 1, LEFT = 2, UP = 3 }
public readonly record struct Pt(int X, int Y);
