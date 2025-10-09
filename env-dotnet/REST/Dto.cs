namespace Snake.EnvServer.REST;

// Single-env REST bodies
public record ResetBody(ulong seed, string obs_type);   // obs_type: "RawState" | "Dense11" | "Dense28Ego" | "Dense32" | "Raycasts19"

public record StepBody(int action);  // <— add this

// We parse /v1/step body manually in Program.cs, so no StepBody needed for single

// Vector (pool) REST bodies
public record ManyResetBody(ulong[]? seeds, string obs_type, int count, string? session);
public record ManyStepBody(int[] actions, string session);