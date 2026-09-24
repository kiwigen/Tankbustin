using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace Tankbustin;

/// <summary>
/// Owns the Lovense client. All network I/O runs off the game thread; the UI only reads
/// <see cref="Status"/> and <see cref="Toys"/>.
/// </summary>
public sealed class LovenseController : IDisposable
{
    private readonly Configuration cfg;
    private readonly IPluginLog log;
    private LovenseLanClient? client;
    private volatile IReadOnlyList<LovenseToy> toys = Array.Empty<LovenseToy>();

    public string Status { get; private set; } = "Not connected";
    public IReadOnlyList<LovenseToy> Toys => toys;

    public LovenseController(Configuration cfg, IPluginLog log)
    {
        this.cfg = cfg;
        this.log = log;
    }

    /// <summary>(Re)creates the client from the current configuration and fetches the toy list.</summary>
    public void Connect()
    {
        client?.Dispose();
        client = null;

        if (string.IsNullOrWhiteSpace(cfg.Ip))
        {
            Status = "Enter the phone's IP address first";
            return;
        }

        try
        {
            client = new LovenseLanClient(log,cfg.Ip.Trim(), cfg.Port, cfg.UseHttps, cfg.AppName);
        }
        catch (Exception ex)
        {
            Status = $"Invalid address: {ex.Message}";
            return;
        }

        _ = RefreshToysAsync();
    }

    public async Task RefreshToysAsync()
    {
        var c = client;
        if (c is null) { Status = "Not connected"; return; }

        Status = "Connecting...";
        try
        {
            log.Debug("Getting toys");
            var list = await c.GetToysAsync().ConfigureAwait(false);
            toys = list;
            Status = list.Count == 0
                ? "Reached the app, but no toys are connected"
                : $"Connected - {list.Count} toy(s) found";
        }
        catch (Exception ex)
        {
            toys = Array.Empty<LovenseToy>();
            Status = $"Connection failed: {ex.Message}";
            log.Warning(ex, "Lovense connection failed");
        }
    }

    public void Play(PatternDef pattern, float scale = 1f, IReadOnlyList<string>? toyIds = null)
    {
        if (toyIds is null)
            return;
        var cap = Math.Clamp(cfg.MaxLevel, 1, 20);
        int Level(int l) => Math.Clamp((int)MathF.Round(l * scale), 0, cap);
        var seconds = Math.Max(1, pattern.DurationSec);

        _ = RunAsync(toyClient => Task.WhenAll(
            toyIds.Select(toy => SendAsync(toyClient, pattern, toy, seconds, Level))));
    }

    private static Task SendAsync(LovenseLanClient toyClient, PatternDef pattern, string? toy, int seconds, Func<int, int> level)
    {
        switch (pattern.Mode)
        {
            case PatternMode.Constant:
                return toyClient.VibrateAsync(level(pattern.Level), seconds, toy);

            case PatternMode.Steps:
                var levels = pattern.ParseLevels().Take(50).Select(level).ToArray();
                return levels.Length == 0
                    ? Task.CompletedTask
                    : toyClient.PatternAsync(levels, pattern.IntervalMs, seconds, toy);

            case PatternMode.Preset:
                return toyClient.PresetAsync(pattern.PresetName, seconds, toy);   // fixed intensity, cap doesn't apply

            default:
                return Task.CompletedTask;
        }
    }
    
    
    
    public void Stop() => _ = RunAsync(c => c.StopAsync());

    private async Task RunAsync(Func<LovenseLanClient, Task> action)
    {
        var c = client;
        if (c is null) return;

        try
        {
            await action(c).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Status = $"Command failed: {ex.Message}";
            log.Warning(ex, "Lovense command failed");
        }
    }

    public void Dispose()
    {
        var c = client;
        client = null;
        if (c is null) return;

        try { c.StopAsync().Wait(1500); } catch { /* best effort */ }
        c.Dispose();
    }
}
