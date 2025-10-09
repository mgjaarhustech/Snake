// Minimal REST client for your Snake env using Dense11 (numeric actions).
// Usage:
//   var env = new SnakeRestDense11Client("http://localhost:8080/v1");
//   var spec = await env.SpecAsync();
//   var s = await env.ResetAsync(123);
//   var step = await env.StepAsync(SnakeRestDense11Client.Action.Straight);

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class SnakeRestDense11Client : IDisposable
{
    public enum Action { Straight = 0, TurnRight = 1, TurnLeft = 2 }

    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;

    public Uri BaseUri { get; }

    public SnakeRestDense11Client(string baseUrl)
{
    // Ensure BaseAddress ends with a slash so "spec" -> ".../v1/spec"
    var rooted = baseUrl.TrimEnd('/') + "/";
    BaseUri = new Uri(rooted);
    _http = new HttpClient { BaseAddress = BaseUri };
    _json = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

    public void Dispose() => _http.Dispose();

    // --- Public API ---

    public async Task<SpecDto> SpecAsync(CancellationToken ct = default)
    {
        using var res = await _http.GetAsync("spec", ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<SpecDto>(_json, ct)
               ?? throw new InvalidOperationException("Empty /v1/spec response");
    }

    public async Task<float[]> ResetAsync(ulong seed, CancellationToken ct = default)
    {
        var body = new ResetBody(seed, "Dense11");
        using var res = await _http.PostAsJsonAsync("reset", body, _json, ct);
        res.EnsureSuccessStatusCode();
        var msg = await DeserializeStepAsync(res, ct);
        var dense = msg.Obs?.Dense?.Data
                    ?? throw new InvalidOperationException("Expected Dense11 payload on reset");
        if (dense.Length != 11) throw new InvalidOperationException($"Dense11 expected length 11, got {dense.Length}");
        return dense;
    }

    public async Task<StepResult> StepAsync(Action action, CancellationToken ct = default)
    {
        var body = new StepBody((int)action); // <-- numeric action now
        using var res = await _http.PostAsJsonAsync("step", body, _json, ct);
        res.EnsureSuccessStatusCode();
        var msg = await DeserializeStepAsync(res, ct);

        var dense = msg.Obs?.Dense?.Data
                    ?? throw new InvalidOperationException("Expected Dense11 payload on step");
        if (dense.Length != 11) throw new InvalidOperationException($"Dense11 expected length 11, got {dense.Length}");

        return new StepResult(
            Obs: dense,
            Signals: msg.Signals ?? Array.Empty<float>(),
            Done: msg.Done,
            Score: msg.Score,
            Length: msg.Length,
            Death: msg.Death ?? string.Empty,
            Steps: msg.Steps,
            Raw: msg
        );
    }

    // --- DTOs ---

    public sealed class SpecDto
    {
        [JsonPropertyName("cols")] public int Cols { get; set; }
        [JsonPropertyName("rows")] public int Rows { get; set; }
        [JsonPropertyName("timeout_mult")] public int TimeoutMult { get; set; }
        [JsonPropertyName("supported_obs")] public string[] SupportedObs { get; set; } = Array.Empty<string>();
        [JsonPropertyName("reward_signals")] public string[] RewardSignals { get; set; } = Array.Empty<string>();
    }

    public sealed record ResetBody(ulong seed, string obs_type);
    public sealed record StepBody(int action); // <-- numeric

    public sealed class StepResponseDto
    {
        [JsonPropertyName("obs")] public ObservationDto? Obs { get; set; }
        [JsonPropertyName("signals")] public float[]? Signals { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; }
        [JsonPropertyName("score")] public int Score { get; set; }
        [JsonPropertyName("length")] public int Length { get; set; }
        [JsonPropertyName("death")] public string? Death { get; set; }
        [JsonPropertyName("steps")] public int Steps { get; set; }
    }

    public sealed class ObservationDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("raw")] public RawStateDto? Raw { get; set; }
        [JsonPropertyName("dense")] public DenseDto? Dense { get; set; }
    }

    public sealed class DenseDto
    {
        [JsonPropertyName("data")] public float[] Data { get; set; } = Array.Empty<float>();
    }

    public sealed class RawStateDto
    {
        [JsonPropertyName("cols")] public int Cols { get; set; }
        [JsonPropertyName("rows")] public int Rows { get; set; }
        [JsonPropertyName("step")] public int Step { get; set; }
        [JsonPropertyName("head")] public PointDto Head { get; set; } = new();
        [JsonPropertyName("dir")] public string Dir { get; set; } = "";
        [JsonPropertyName("body")] public List<PointDto> Body { get; set; } = new();
        [JsonPropertyName("food")] public PointDto Food { get; set; } = new();
    }

    public sealed class PointDto
    {
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
    }

    public sealed record StepResult(
        float[] Obs, float[] Signals, bool Done, int Score, int Length, string Death, int Steps, StepResponseDto Raw
    );

    private async Task<StepResponseDto> DeserializeStepAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var msg = await res.Content.ReadFromJsonAsync<StepResponseDto>(_json, ct);
        if (msg is null) throw new InvalidOperationException("Empty JSON step/reset response");
        return msg;
    }
}
