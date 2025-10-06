namespace Snake.EnvServer.REST;

public record ResetBody(ulong seed, string obs_type);      // obs_type: "RAW_STATE" | "DENSE32"
public record StepBody(string action);                     // "STRAIGHT" | "TURN_RIGHT" | "TURN_LEFT"
public record ManyResetBody(ulong[] seeds, string obs_type);
public record ManyStepBody(string[] actions);
