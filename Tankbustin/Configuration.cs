using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Tankbustin;

public enum PatternMode { Constant, Steps, Preset }

public enum TriggerKind { DamageTaken, HealReceived, LowHp, Death, Revive, CombatStart, CombatEnd }

[Serializable]
public class PatternDef
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New pattern";
    public PatternMode Mode { get; set; } = PatternMode.Constant;

    ///Constant mode: vibration level 0-20.
    public int Level { get; set; } = 5;

    ///Steps mode: comma separated levels (0-20), e.g. "2,6,10,14,10,6,2".
    public string LevelsText { get; set; } = "2,6,10,14,10,6,2";

    ///Steps mode: time per step in ms (Lovense minimum is 100).
    public int IntervalMs { get; set; } = 300;

    ///Preset mode: pulse, wave, fireworks or earthquake.
    public string PresetName { get; set; } = "pulse";

    ///How long the pattern runs (steps loop until this elapses).
    public int DurationSec { get; set; } = 3;

    public int[] ParseLevels()
    {
        var list = new List<int>();
        foreach (var part in LevelsText.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part, out var v))
                list.Add(Math.Clamp(v, 0, 20));
        return list.ToArray();
    }
}

[Serializable]
public class TriggerBinding
{
    public TriggerKind Kind { get; set; }
    public bool Enabled { get; set; }
    public Guid PatternId { get; set; } = Guid.Empty;
    public int CooldownMs { get; set; } = 1000;

    /// DamageTaken / HealReceived: minimum % of max HP for the event to count.
    /// LowHp: fires when HP falls to or below this % of max HP.
    public float Threshold { get; set; }

    /// DamageTaken / HealReceived: scale pattern intensity by how big the hit/heal was.
    public bool ScaleWithAmount { get; set; }
    
    //List of toys assigned
    public List<string> ToyIds { get; set; } = new();
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Connection
    public string Ip { get; set; } = "192.168.1.100";
    public int Port { get; set; } = 20010;
    public bool UseHttps { get; set; } = false;
    public string AppName { get; set; } = "Tankbustin";
    public bool AutoConnect { get; set; } = false;
    public string ToyId { get; set; } = "";

    // Safety / general
    public bool Enabled { get; set; } = true;
    public int MaxLevel { get; set; } = 10;
    public float ScaleFullAtPercent { get; set; } = 25f;
    public bool StopOnLogout { get; set; } = true;

    public List<PatternDef> Patterns { get; set; } = new();
    public List<TriggerBinding> Triggers { get; set; } = new();

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
        EnsureDefaults();
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);

    private void EnsureDefaults()
    {
        if (Patterns.Count == 0)
        {
            Patterns.Add(new PatternDef { Name = "Light tap", Mode = PatternMode.Constant, Level = 4, DurationSec = 1 });
            Patterns.Add(new PatternDef
            {
                Name = "Ramp", Mode = PatternMode.Steps, LevelsText = "2,5,8,11,8,5,2", IntervalMs = 300, DurationSec = 4
            });
            Patterns.Add(new PatternDef { Name = "Wave", Mode = PatternMode.Preset, PresetName = "wave", DurationSec = 5 });
        }

        var firstPattern = Patterns.FirstOrDefault()?.Id ?? Guid.Empty;
        foreach (var kind in Enum.GetValues<TriggerKind>())
        {
            if (Triggers.Any(t => t.Kind == kind)) continue;

            // Everything starts disabled so nothing fires until the user opts in.
            Triggers.Add(kind switch
            {
                TriggerKind.DamageTaken => new TriggerBinding { Kind = kind, PatternId = firstPattern, CooldownMs = 500, Threshold = 1f, ScaleWithAmount = true },
                TriggerKind.HealReceived => new TriggerBinding { Kind = kind, PatternId = firstPattern, CooldownMs = 2000, Threshold = 5f },
                TriggerKind.LowHp => new TriggerBinding { Kind = kind, PatternId = firstPattern, CooldownMs = 10000, Threshold = 25f },
                TriggerKind.Death => new TriggerBinding { Kind = kind, PatternId = firstPattern, CooldownMs = 5000 },
                TriggerKind.Revive => new TriggerBinding { Kind = kind, PatternId = firstPattern, CooldownMs = 2000 },
                _ => new TriggerBinding { Kind = kind, PatternId = firstPattern, CooldownMs = 3000 },
            });
        }
    }
}
