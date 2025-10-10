// tournament-controller/Models.cs
using System.Text.Json.Serialization;

public record OpenSessionRequest(
    string team,
    string obs_type,
    int? games,
    ulong? seed_start,
    ulong? seed_stride,
    List<ulong>? seeds
);

public record OpenSessionResponse(
    string session,
    string team,
    string obs_type,
    int total_games,
    List<ulong> assigned_seeds
);

public record ResetComboBody(string? session, ulong? seed, string? obs_type);
public record StepComboBody(string? session, int action);

public record ComboEnvelope
{
    public StepResponse Step { get; set; } = new();
    [JsonPropertyName("raw_for_render")] public RawState RawForRender { get; set; } = new();
}

public record ManyComboEnvelope
{
    public string Session { get; set; } = "";
    public List<ComboEnvelope> Envs { get; set; } = new();
}

public record StepResponse
{
    public Observation Obs { get; set; } = new();
    public float[] Signals { get; set; } = Array.Empty<float>();
    public bool Done { get; set; }
    public int Score { get; set; }
    public int Length { get; set; }
    public string Death { get; set; } = "";
    public int Steps { get; set; }
}

public record Observation
{
    public string Type { get; set; } = "";
    public Dense? Dense { get; set; }
    public RawState? Raw { get; set; }
}

public record Dense { public float[] Data { get; set; } = Array.Empty<float>(); }

public record RawState
{
    public int Cols { get; set; }
    public int Rows { get; set; }
    public int Step { get; set; }
    public Point Head { get; set; } = new();
    public string Dir { get; set; } = "";
    public List<Point> Body { get; set; } = new();
    public Point Food { get; set; } = new();
}

public record Point { public int X { get; set; } public int Y { get; set; } }

public record LeaderRow(string team, string obs_type, int games, int total_score, double avg_score, int best_length);

public record RunStatus(
    string session,
    string team,
    string obs_type,
    int totalGames,
    int completed,
    int remaining,
    int currentEpisodeSteps,
    int totalScore,
    int bestLength,
    bool finished
);

public sealed class SessionState
{
    public string Session { get; }
    public string Team { get; }
    public string ObsType { get; }
    public Queue<ulong> PendingSeeds { get; }
    public int TotalGames { get; }
    public int Completed { get; set; }
    public int TotalScore { get; set; }
    public int BestLength { get; set; }
    public string EnvSessionId { get; } // reuse controller session id for env pool
    public int CurrentEpisodeSteps { get; set; }

    public SessionState(string session, string team, string obsType, IEnumerable<ulong> seeds)
    {
        Session = session;
        Team = team;
        ObsType = obsType;
        PendingSeeds = new Queue<ulong>(seeds);
        TotalGames = PendingSeeds.Count;
        EnvSessionId = session;
    }
}
