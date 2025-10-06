using Microsoft.AspNetCore.Server.Kestrel.Core;
using Snake.EnvServer.Game;
using Snake.EnvServer.REST;
using Snake.EnvServer.Services;
using Google.Protobuf;          // JsonFormatter
using Pb = Snake.V1;

var builder = WebApplication.CreateBuilder(args);
var protoJson = new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues: true));

// config...
int cols        = int.Parse(builder.Configuration["cols"] ?? "40");
int rows        = int.Parse(builder.Configuration["rows"] ?? "30");
int timeoutMult = int.Parse(builder.Configuration["timeout-mult"] ?? "150");
int restPort    = int.Parse(builder.Configuration["rest-port"] ?? "8080");
int grpcPort    = int.Parse(builder.Configuration["grpc-port"] ?? "50051");

builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(restPort, lo => lo.Protocols = HttpProtocols.Http1); // REST
    o.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2); // gRPC
});

builder.Services.AddSingleton(new Env(cols, rows, timeoutMult));
builder.Services.AddSingleton<Snake.EnvServer.Services.SnakeEnvService>();
builder.Services.AddGrpc();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.MapGrpcService<SnakeEnvService>();

// REST spec (plain JSON is fine here)
app.MapGet("/v1/spec", (Env env) =>
{
    return Results.Json(new {
        cols,
        rows,
        timeout_mult = timeoutMult,
        supported_obs = new[] { "RawState", "Dense32", "Dense11", "Dense28Ego", "Raycasts19" },
        reward_signals = new[] { "eat_food","death","step_cost","toward_food","turning","timeout" }
    });
});

// REST → protobuf JSON (so oneof shows up correctly)
app.MapPost("/v1/reset", (ResetBody body, SnakeEnvService svc) =>
{
    // enums are PascalCase now; no helpers needed
    var ok = Enum.TryParse<Pb.ObsType>(body.obs_type, true, out var ot) ? ot : Pb.ObsType.Dense32;
    var msg = svc.ResetCore(new Pb.ResetRequest { Seed = body.seed, ObsType = ok });
    return Results.Text(protoJson.Format(msg), "application/json");
});

app.MapPost("/v1/step", (StepBody body, SnakeEnvService svc) =>
{
    var ok = Enum.TryParse<Pb.Action>(body.action, true, out var act) ? act : Pb.Action.Straight;
    var msg = svc.StepCore(new Pb.StepRequest { Action = ok });
    return Results.Text(protoJson.Format(msg), "application/json");
});

app.MapPost("/v1/reset_many", (ManyResetBody body, SnakeEnvService svc) =>
{
    var ok  = Enum.TryParse<Pb.ObsType>(body.obs_type, true, out var ot) ? ot : Pb.ObsType.Dense32;
    var one = svc.ResetCore(new Pb.ResetRequest { Seed = (body.seeds?.FirstOrDefault()) ?? 1UL, ObsType = ok });
    var msg = new Pb.ManyResponse { Envs = { one } };
    return Results.Text(protoJson.Format(msg), "application/json");
});

app.MapPost("/v1/step_many", (ManyStepBody body, SnakeEnvService svc) =>
{
    var first = (body.actions?.FirstOrDefault()) ?? "Straight";
    var ok = Enum.TryParse<Pb.Action>(first, true, out var act) ? act : Pb.Action.Straight;
    var one = svc.StepCore(new Pb.StepRequest { Action = ok });
    var msg = new Pb.ManyResponse { Envs = { one } };
    return Results.Text(protoJson.Format(msg), "application/json");
});


app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Redirect("/swagger"));
app.Run();