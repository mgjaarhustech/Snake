// tournament-controller/Program.cs
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// -------------------- Hosting & URLs --------------------
builder.WebHost.UseUrls("http://0.0.0.0:8090");

// -------------------- Config --------------------
string ENV_BASE = Environment.GetEnvironmentVariable("ENV_BASE") ?? "http://localhost:8080/v1";
int DEFAULT_GAMES = int.TryParse(Environment.GetEnvironmentVariable("DEFAULT_GAMES"), out var g) ? g : 500;
int MAX_STEPS_PER_EP = int.TryParse(Environment.GetEnvironmentVariable("MAX_STEPS_PER_EP"), out var ms) ? ms : 10_000;

// -------------------- CORS (open LAN) --------------------
builder.Services.AddCors(o =>
{
    o.AddPolicy("OpenCors", p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// -------------------- Swagger --------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// -------------------- EF Core (SQLite) --------------------
var conn = builder.Configuration.GetConnectionString("TournamentDb");
if (string.IsNullOrWhiteSpace(conn))
{
    var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
    Directory.CreateDirectory(dataDir);
    conn = $"Data Source={Path.Combine(dataDir, "tournament.db")}";
}
builder.Services.AddDbContext<TournamentDb>(o => o.UseSqlite(conn));

var app = builder.Build();

// Create DB if missing
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TournamentDb>();
    await db.Database.EnsureCreatedAsync();
}

// -------------------- Pipeline --------------------
app.UseCors("OpenCors");
app.UseSwagger();
app.UseSwaggerUI();

// -------------------- JSON opts --------------------
var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};

// -------------------- In-memory session book-keeping --------------------
var sessions = new ConcurrentDictionary<string, SessionState>(StringComparer.Ordinal);

// -------------------- HttpClient to env --------------------
var http = new HttpClient { BaseAddress = new Uri(ENV_BASE.EndsWith("/") ? ENV_BASE : ENV_BASE + "/") };

static IEnumerable<ulong> SeedsSeries(int games, ulong start, ulong stride)
{
    for (int i = 0; i < games; i++) yield return start + (ulong)i * stride;
}

// -------------------- Health --------------------
app.MapGet("/health", () => Results.Ok(new { ok = true, envBase = ENV_BASE, port = 8090 }));

// ======================================================================
// Endpoints
// ======================================================================

// Open a controller session (persist Team + SessionRow)
app.MapPost("/tc/open_session", async (OpenSessionRequest req, TournamentDb db) =>
{
    if (string.IsNullOrWhiteSpace(req.team))
        return Results.BadRequest(new { error = "team required" });
    if (string.IsNullOrWhiteSpace(req.obs_type))
        return Results.BadRequest(new { error = "obs_type required (Dense11, Dense32, Dense28Ego, Raycasts19, RawState)" });

    int games = req.games ?? DEFAULT_GAMES;
    if (games <= 0) return Results.BadRequest(new { error = "games must be > 0" });

    var seeds = (req.seeds is { Count: > 0 })
        ? req.seeds
        : SeedsSeries(games, req.seed_start ?? 1UL, req.seed_stride ?? 1UL).ToList();

    string session = Guid.NewGuid().ToString("N");
    var st = new SessionState(session, req.team.Trim(), req.obs_type.Trim(), seeds);
    if (!sessions.TryAdd(session, st))
        return Results.Problem("session allocation failed");

    var team = await db.Teams.FirstOrDefaultAsync(t => t.Name == st.Team);
    if (team is null)
    {
        team = new Team { Name = st.Team };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
    }

    db.Sessions.Add(new SessionRow
    {
        Session = st.Session,
        ObsType = st.ObsType,
        TotalGames = st.TotalGames,
        TeamId = team.Id
    });
    await db.SaveChangesAsync();

    return Results.Json(new OpenSessionResponse(session, st.Team, st.ObsType, st.TotalGames, seeds), json);
});

// Status
app.MapGet("/tc/status/{session}", (string session) =>
{
    if (!sessions.TryGetValue(session, out var st))
        return Results.NotFound(new { error = "unknown session" });

    var res = new RunStatus(
        st.Session, st.Team, st.ObsType,
        st.TotalGames,
        st.Completed,
        st.TotalGames - st.Completed - st.PendingSeeds.Count,
        st.CurrentEpisodeSteps,
        st.TotalScore,
        st.BestLength,
        finished: st.Completed >= st.TotalGames
    );
    return Results.Json(res, json);
});

// Leaderboard (in-memory)
app.MapGet("/tc/leaderboard", () =>
{
    var rows = sessions.Values
        .Where(s => s.Completed > 0)
        .GroupBy(s => (s.Team, s.ObsType))
        .Select(g =>
        {
            int games = g.Sum(x => x.Completed);
            int total = g.Sum(x => x.TotalScore);
            double avg = games > 0 ? (double)total / games : 0;
            int bestLen = g.Max(x => x.BestLength);
            return new LeaderRow(g.Key.Team, g.Key.ObsType, games, total, Math.Round(avg, 3), bestLen);
        })
        .OrderByDescending(r => r.avg_score)
        .ThenByDescending(r => r.total_score)
        .ThenBy(r => r.team, StringComparer.OrdinalIgnoreCase)
        .ToList();

    return Results.Json(rows, json);
});

// Student RESET (combo) – creates a GameResult row
app.MapPost("/v1/reset_combo", async (ResetComboBody body, TournamentDb db) =>
{
    if (body.session is null || !sessions.TryGetValue(body.session, out var st))
        return Results.BadRequest(new { error = "valid session required" });

    if (st.PendingSeeds.Count == 0)
        return Results.BadRequest(new { error = "no remaining seeds for this session" });

    var seed = st.PendingSeeds.Dequeue();
    st.CurrentEpisodeSteps = 0;

    // create DB row
    var sessionRow = await db.Sessions.FirstOrDefaultAsync(s => s.Session == st.Session);
    if (sessionRow is null) return Results.Problem("DB session not found");

    db.Games.Add(new GameResult
    {
        SessionRowId = sessionRow.Id,
        Seed = seed,
        StartedUtc = DateTime.UtcNow
    });
    await db.SaveChangesAsync();

    // env reset_many_combo (count=1)
    var envReq = new { seeds = new[] { seed }, obs_type = st.ObsType, count = 1, session = st.EnvSessionId };
    var envRes = await http.PostAsJsonAsync("reset_many_combo", envReq, json);
    if (!envRes.IsSuccessStatusCode)
        return Results.Problem($"env reset_many_combo failed: {envRes.StatusCode} {await envRes.Content.ReadAsStringAsync()}");

    var payload = await envRes.Content.ReadFromJsonAsync<ManyComboEnvelope>(json)
                  ?? new ManyComboEnvelope { Session = st.EnvSessionId, Envs = new() { new ComboEnvelope() } };

    return Results.Json(payload.Envs[0], json);
});

// Student STEP (combo) – update GameResult row when done
app.MapPost("/v1/step_combo", async (StepComboBody body, TournamentDb db) =>
{
    if (body.session is null || !sessions.TryGetValue(body.session, out var st))
        return Results.BadRequest(new { error = "valid session required" });

    int a = body.action;
    if (a < 0 || a > 2) return Results.BadRequest(new { error = "action must be 0|1|2" });

    var envReq = new { session = st.EnvSessionId, actions = new[] { a } };
    var envRes = await http.PostAsJsonAsync("step_many_combo", envReq, json);
    if (!envRes.IsSuccessStatusCode)
        return Results.Problem($"env step_many_combo failed: {envRes.StatusCode} {await envRes.Content.ReadAsStringAsync()}");

    var payload = await envRes.Content.ReadFromJsonAsync<ManyComboEnvelope>(json)
                  ?? new ManyComboEnvelope { Session = st.EnvSessionId, Envs = new() { new ComboEnvelope() } };

    var env = payload.Envs[0];
    st.CurrentEpisodeSteps = env.Step.Steps;

    // If episode ends (or hard-cap), close latest unfinished game
    if (env.Step.Done || env.Step.Steps >= MAX_STEPS_PER_EP)
    {
        st.Completed++;
        st.TotalScore += env.Step.Score;
        st.BestLength = Math.Max(st.BestLength, env.Step.Length);
        st.CurrentEpisodeSteps = 0;

        var sessionRow = await db.Sessions.FirstOrDefaultAsync(s => s.Session == st.Session);
        if (sessionRow != null)
        {
            var openGame = await db.Games
                .Where(g => g.SessionRowId == sessionRow.Id && g.FinishedUtc == null)
                .OrderByDescending(g => g.Id)
                .FirstOrDefaultAsync();

            if (openGame != null)
            {
                openGame.Score = env.Step.Score;
                openGame.Length = env.Step.Length;
                openGame.Steps = env.Step.Steps;
                openGame.FinishedUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
    }

    return Results.Json(env, json);
});

app.MapGet("/", () => Results.Redirect("/swagger"));

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"Tournament controller listening on: {string.Join(", ", app.Urls)}");
    Console.WriteLine($"ENV_BASE = {ENV_BASE}");
});

app.Run();
