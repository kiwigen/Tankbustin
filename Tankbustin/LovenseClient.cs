using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace Tankbustin;

public sealed record LovenseToy(string Id, string Name, int Battery, bool Connected);

/// <summary>Minimal client for the Lovense Remote "Game Mode" local HTTP API.</summary>
public sealed class LovenseLanClient : IDisposable
{
    private readonly HttpClient http;
    private readonly Uri commandUri;
    private readonly IPluginLog log;

    public LovenseLanClient(IPluginLog log, string ip, int port, bool useHttps, string appName)
    {
        this.log = log;
        var baseUrl = useHttps
            ? $"https://{ip.Replace('.', '-')}.lovense.club:{port}"
            : $"http://{ip}:{port}";

        commandUri = new Uri($"{baseUrl}/command");
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.Add("X-platform", appName);
    }

    private async Task<JsonNode?> SendAsync(JsonObject payload, CancellationToken ct)
    {
        using var resp = await http.PostAsJsonAsync(commandUri, payload, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<List<LovenseToy>> GetToysAsync(CancellationToken ct = default)
    {
        log.Debug("Getting toys start");
        var res = await SendAsync(new JsonObject { ["command"] = "GetToys" }, ct).ConfigureAwait(false);
        log.Debug($"App response: {res?.ToJsonString()}");
        var toysNode = res?["data"]?["toys"];
        var result = new List<LovenseToy>();

        // Depending on the app version, "toys" is either a JSON object or a JSON-encoded string.
        JsonObject? toys = toysNode switch
        {
            JsonObject o => o,
            JsonValue v when v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)
                => JsonNode.Parse(s) as JsonObject,
            _ => null
        };
        if (toys is null) return result;
        log.Debug($"Toys: {toys.ToJsonString()}");
        foreach (var (id, node) in toys)
        {
            if (node is not JsonObject toy) continue;
            var nick = toy["nickName"]?.GetValue<string>();
            var name = !string.IsNullOrEmpty(nick) ? nick : toy["name"]?.GetValue<string>() ?? id;
            int battery = toy["battery"]?.GetValue<int>() ?? 0; 
            bool.TryParse(toy["connected"]?.GetValue<string>(), out var connected);
            result.Add(new LovenseToy(
                id,
                name,
                battery,
                connected));
        }
        return result;
    }

    /// <summary>Levels are 0-20. seconds = 0 runs until Stop. toyId = null targets all toys.</summary>
    public Task<JsonNode?> VibrateAsync(int level, int seconds, string? toyId, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["command"] = "Function",
            ["action"] = $"Vibrate:{Math.Clamp(level, 0, 20)}",
            ["timeSec"] = seconds,
            ["apiVer"] = 1
        };
        if (toyId is not null) payload["toy"] = toyId;
        return SendAsync(payload, ct);
    }

    public Task<JsonNode?> StopAsync(string? toyId = null, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["command"] = "Function",
            ["action"] = "Stop",
            ["timeSec"] = 0,
            ["apiVer"] = 1
        };
        if (toyId is not null) payload["toy"] = toyId;
        return SendAsync(payload, ct);
    }

    /// <summary>Built-in presets: pulse, wave, fireworks, earthquake.</summary>
    public Task<JsonNode?> PresetAsync(string name, int seconds, string? toyId, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["command"] = "Preset",
            ["name"] = name,
            ["timeSec"] = seconds,
            ["apiVer"] = 1
        };
        if (toyId is not null) payload["toy"] = toyId;
        return SendAsync(payload, ct);
    }

    /// <summary>Custom pattern: one level (0-20) per step, one step every intervalMs (min 100), max ~50 steps.</summary>
    public Task<JsonNode?> PatternAsync(IEnumerable<int> levels, int intervalMs, int seconds,
        string? toyId, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["command"] = "Pattern",
            ["rule"] = $"V:1;F:;S:{Math.Max(100, intervalMs)}#",
            ["strength"] = string.Join(";", levels.Select(l => Math.Clamp(l, 0, 20))),
            ["timeSec"] = seconds,
            ["apiVer"] = 2
        };
        if (toyId is not null) payload["toy"] = toyId;
        return SendAsync(payload, ct);
    }

    public void Dispose() => http.Dispose();
}
