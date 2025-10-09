using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Text.Json;
using Google.Protobuf;          // JsonFormatter
using Snake.EnvServer.REST;
using Snake.EnvServer.Services;
using Snake.Core.Game;          // Env from the core DLL
using Snake.Core.Game.Obs;
using Pb = Snake.V1;

var builder = WebApplication.CreateBuilder(args);
var protoJson = new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues: true));

// config
int cols        = int.Parse(builder.Configuration["cols"] ?? "30");
int rows        = int.Parse(builder.Configuration["rows"] ?? "30");
int timeoutMult = int.Parse(builder.Configuration["timeout-mult"] ?? "150");
int restPort    = int.Parse(builder.Configuration["rest-port"] ?? "8080");
int grpcPort    = int.Parse(builder.Configuration["grpc-port"] ?? "50051");

// Kestrel
builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(restPort, lo => lo.Protocols = HttpProtocols.Http1); // REST
    o.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2); // gRPC
});

// DI
builder.Services.AddSingleton(new Env(cols, rows, timeoutMult)); // single-env instance
builder.Services.AddSingleton<SnakeEnvService>();                // service uses core Env
builder.Services.AddSingleton<EnvPoolManager>();
builder.Services.AddGrpc();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
builder.Services.AddCors(o =>
{
    o.AddPolicy("OpenCors", p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();
app.UseCors("OpenCors");

app.MapGrpcService<SnakeEnvService>();

// -------- REST --------

app.MapGet("/v1/spec", (Env env) =>
{
    return Results.Json(new {
        cols,
        rows,
        timeout_mult = timeoutMult,
        supported_obs = new[] { "RawState", "Dense32", "Dense11", "Dense28Ego", "Raycasts19" },
        reward_signals = new[] { "eat_food","death","step_cost","toward_food","turning","timeout" },
        action_space = new[] { 0, 1, 2 },
        action_names = new[] { "Straight", "TurnRight", "TurnLeft" } // docs only
    });
});

// Reset (protobuf JSON so oneof is correct)
app.MapPost("/v1/reset", (ResetBody body, SnakeEnvService svc) =>
{
    var ok = Enum.TryParse<Pb.ObsType>(body.obs_type, true, out var ot) ? ot : Pb.ObsType.Dense11;
    var msg = svc.ResetCore(new Pb.ResetRequest { Seed = body.seed, ObsType = ok });
    return Results.Text(protoJson.Format(msg), "application/json");
});

// Step — NUMERIC ONLY (0/1/2)
app.MapPost("/v1/step", (StepBody body, SnakeEnvService svc) =>
{
    int i = body.action;
    if (i is < 0 or > 2)
        return Results.BadRequest(new { error = "action must be 0, 1, or 2" });

    var msg = svc.StepCore(new Pb.StepRequest { Action = i });
    return Results.Text(protoJson.Format(msg), "application/json");
});

app.MapPost("/v1/reset_many", (ManyResetBody body, SnakeEnvService svc) =>
{
    var ok = Enum.TryParse<Pb.ObsType>(body.obs_type, true, out var ot) ? ot : Pb.ObsType.Dense11;
    var req = new Pb.ResetManyRequest {
        ObsType = ot,
        Count   = body.count,
        Session = body.session ?? ""
    };
    if (body.seeds is not null) req.Seeds.AddRange(body.seeds);

    var msg = svc.ResetMany(req, null!).Result;
    return Results.Text(
        new Google.Protobuf.JsonFormatter(new Google.Protobuf.JsonFormatter.Settings(formatDefaultValues:true)).Format(msg),
        "application/json"
    );
});

app.MapPost("/v1/step_many", (ManyStepBody body, SnakeEnvService svc) =>
{
    var req = new Pb.StepManyRequest { Session = body.session };
    req.Actions.AddRange(body.actions);

    var msg = svc.StepMany(req, null!).Result;
    return Results.Text(
        new Google.Protobuf.JsonFormatter(new Google.Protobuf.JsonFormatter.Settings(formatDefaultValues: true)).Format(msg),
        "application/json"
    );
});

// --- Combo endpoints: chosen obs + RawState for rendering (REST-only) ---

// POST /v1/reset_combo  { seed: <uint64>, obs_type: "Dense11" | ... }
app.MapPost("/v1/reset_combo", (ResetBody body, SnakeEnvService svc, Env env) =>
{
    // Parse obs type (PascalCase accepted; default Dense11)
    var ok = Enum.TryParse<Pb.ObsType>(body.obs_type, true, out var ot) ? ot : Pb.ObsType.Dense11;

    // Do a normal reset (produces StepResponse with the chosen obs)
    var stepMsg = svc.ResetCore(new Pb.ResetRequest { Seed = body.seed, ObsType = ok });

    // Build a Pb.RawState from the current env snapshot (independent of chosen obs)
    var rc = RawStateEncoder.From(env);
    var raw = new Pb.RawState
    {
        Cols = rc.Cols,
        Rows = rc.Rows,
        Step = rc.Step,
        Head = new Pb.Point { X = rc.Head.X, Y = rc.Head.Y },
        Dir  = rc.Direction.ToString(),
        Food = new Pb.Point { X = rc.Food.X, Y = rc.Food.Y }
    };
    foreach (var b in rc.Body) raw.Body.Add(new Pb.Point { X = b.X, Y = b.Y });

    // Serialize both with Protobuf JsonFormatter to keep oneof/casing correct
    var stepJson = protoJson.Format(stepMsg);
    var rawJson  = protoJson.Format(raw);

    // Return a small envelope: { "step": <StepResponse>, "raw_for_render": <RawState> }
    var payload = $"{{\"step\":{stepJson},\"raw_for_render\":{rawJson}}}";
    return Results.Text(payload, "application/json");
});

// POST /v1/step_combo  { action: <int 0|1|2> }
app.MapPost("/v1/step_combo", (StepBody body, SnakeEnvService svc, Env env) =>
{
    int i = body.action;
    if (i is < 0 or > 2)
        return Results.BadRequest(new { error = "action must be 0, 1, or 2" });

    // Normal step (produces StepResponse with currently-selected obs type)
    var stepMsg = svc.StepCore(new Pb.StepRequest { Action = i });

    // RawState snapshot in parallel for rendering
    var rc = RawStateEncoder.From(env);
    var raw = new Pb.RawState
    {
        Cols = rc.Cols,
        Rows = rc.Rows,
        Step = rc.Step,
        Head = new Pb.Point { X = rc.Head.X, Y = rc.Head.Y },
        Dir = rc.Direction.ToString(),
        Food = new Pb.Point { X = rc.Food.X, Y = rc.Food.Y }
    };
    foreach (var b in rc.Body) raw.Body.Add(new Pb.Point { X = b.X, Y = b.Y });

    var stepJson = protoJson.Format(stepMsg);
    var rawJson = protoJson.Format(raw);
    var payload = $"{{\"step\":{stepJson},\"raw_for_render\":{rawJson}}}";
    return Results.Text(payload, "application/json");
});

// --- Combo (many): chosen obs + RawState for rendering for EACH env ---

// POST /v1/reset_many_combo
// body: { "seeds"?: [uint64], "obs_type": "Dense11", "count": 8, "session"?: "" }
app.MapPost("/v1/reset_many_combo", (ManyResetBody body, SnakeEnvService svc) =>
{
    var ok = Enum.TryParse<Pb.ObsType>(body.obs_type, true, out var ot) ? ot : Pb.ObsType.Dense11;
    var req = new Pb.ResetManyRequest {
        ObsType = ot,
        Count   = body.count,
        Session = body.session ?? "",
        WithRaw = true
    };
    if (body.seeds is not null) req.Seeds.AddRange(body.seeds);

    var msg = svc.ResetMany(req, null!).Result;
    return Results.Text(
        new Google.Protobuf.JsonFormatter(new Google.Protobuf.JsonFormatter.Settings(formatDefaultValues:true)).Format(msg),
        "application/json"
    );
});

// POST /v1/step_many_combo
// body: { "session": "<id>", "actions": [1] }  // broadcast or per-env
app.MapPost("/v1/step_many_combo", (ManyStepBody body, SnakeEnvService svc) =>
{
    var req = new Pb.StepManyRequest { Session = body.session, WithRaw = true };
    req.Actions.AddRange(body.actions);

    var msg = svc.StepMany(req, null!).Result;
    return Results.Text(
        new Google.Protobuf.JsonFormatter(new Google.Protobuf.JsonFormatter.Settings(formatDefaultValues:true)).Format(msg),
        "application/json"
    );
});



app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Redirect("/swagger"));
app.Run();
