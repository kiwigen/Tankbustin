using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Tankbustin.Windows;

public sealed class ConfigWindow : Window
{
    private static readonly string[] PresetNames = { "pulse", "wave", "fireworks", "earthquake" };

    private static readonly Vector4 Green = new(0.4f, 0.9f, 0.4f, 1f);
    private static readonly Vector4 Red = new(1f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Grey = new(0.7f, 0.7f, 0.7f, 1f);

    private readonly Configuration cfg;
    private readonly LovenseController ctl;
    private Guid selectedPattern = Guid.Empty;

    public ConfigWindow(Configuration cfg, LovenseController ctl) : base("Lovense Link###LovenseLinkConfig")
    {
        this.cfg = cfg;
        this.ctl = ctl;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(560, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##lovenseTabs")) return;

        if (ImGui.BeginTabItem("Connection")) { DrawConnection(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Patterns")) { DrawPatterns(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Triggers")) { DrawTriggers(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Safety")) { DrawSafety(); ImGui.EndTabItem(); }

        ImGui.EndTabBar();
    }

    private void DrawConnection()
    {
        ImGui.TextWrapped("In the Lovense Remote app open Discover > Game Mode, enable it, and enter the phone's " +
                          "local IP and the port shown there. Your PC and phone must be on the same network.");
        ImGui.Spacing();

        EditText("IP address", cfg.Ip, v => cfg.Ip = v, 64);
        EditInput("Port", cfg.Port, v => cfg.Port = Math.Clamp(v, 1, 65535));
        EditBool("Use HTTPS (lovense.club)", cfg.UseHttps, v => cfg.UseHttps = v,
            "HTTP works fully offline. HTTPS resolves a *.lovense.club hostname, so it needs internet DNS. " +
            "Use the HTTPS port (usually 30010) when this is on.");
        EditBool("Connect automatically on plugin load", cfg.AutoConnect, v => cfg.AutoConnect = v);

        ImGui.Spacing();
        if (ImGui.Button("Connect / refresh toys")) ctl.Connect();
        ImGui.SameLine();
        if (ImGui.Button("Stop")) ctl.Stop();

        var status = ctl.Status;
        var color = status.StartsWith("Connected") ? Green
            : status.Contains("failed") || status.Contains("Invalid") ? Red
            : Grey;
        ImGui.TextColored(color, status);

        ImGui.Separator();
        DrawToyPicker();
    }

    private void DrawToyPicker()
    {
        var toys = ctl.Toys;
        var preview = string.IsNullOrEmpty(cfg.ToyId)
            ? "All toys"
            : toys.FirstOrDefault(t => t.Id == cfg.ToyId)?.Name ?? cfg.ToyId;

        ImGui.SetNextItemWidth(240);
        if (ImGui.BeginCombo("Target toy", preview))
        {
            if (ImGui.Selectable("All toys", string.IsNullOrEmpty(cfg.ToyId)))
            {
                cfg.ToyId = "";
                cfg.Save();
            }

            foreach (var toy in toys)
            {
                var label = $"{toy.Name} ({(toy.Battery >= 0 ? toy.Battery + "%" : "?")}){(toy.Connected ? "" : " - offline")}##{toy.Id}";
                if (ImGui.Selectable(label, toy.Id == cfg.ToyId))
                {
                    cfg.ToyId = toy.Id;
                    cfg.Save();
                }
            }
            ImGui.EndCombo();
        }

        if (ImGui.Button("Test: level 3 for 2 s"))
            ctl.Play(new PatternDef { Mode = PatternMode.Constant, Level = 3, DurationSec = 2 });
    }

    private void DrawPatterns()
    {
        if (cfg.Patterns.All(p => p.Id != selectedPattern))
            selectedPattern = cfg.Patterns.FirstOrDefault()?.Id ?? Guid.Empty;

        var current = cfg.Patterns.FirstOrDefault(p => p.Id == selectedPattern);

        ImGui.SetNextItemWidth(240);
        if (ImGui.BeginCombo("Pattern", current?.Name ?? "(none)"))
        {
            foreach (var p in cfg.Patterns)
                if (ImGui.Selectable($"{p.Name}##{p.Id}", p.Id == selectedPattern))
                    selectedPattern = p.Id;
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("Add"))
        {
            var np = new PatternDef();
            cfg.Patterns.Add(np);
            selectedPattern = np.Id;
            cfg.Save();
            return;
        }

        if (current is null) return;

        ImGui.SameLine();
        if (ImGui.Button("Delete"))
        {
            cfg.Patterns.Remove(current);
            foreach (var binding in cfg.Triggers.Where(b => b.PatternId == current.Id))
                binding.PatternId = Guid.Empty;
            cfg.Save();
            return;
        }

        ImGui.Separator();

        EditText("Name", current.Name, v => current.Name = v, 48);

        ImGui.SetNextItemWidth(240);
        if (ImGui.BeginCombo("Mode", current.Mode.ToString()))
        {
            foreach (var mode in Enum.GetValues<PatternMode>())
                if (ImGui.Selectable(mode.ToString(), mode == current.Mode))
                {
                    current.Mode = mode;
                    cfg.Save();
                }
            ImGui.EndCombo();
        }

        switch (current.Mode)
        {
            case PatternMode.Constant:
                EditSlider("Level (0-20)", current.Level, v => current.Level = v, 0, 20);
                break;

            case PatternMode.Steps:
                EditText("Levels", current.LevelsText, v => current.LevelsText = v, 400, 320);
                ImGui.TextDisabled($"Comma separated, each 0-20. {current.ParseLevels().Length} valid step(s), max 50 used.");
                EditSlider("Step length (ms)", current.IntervalMs, v => current.IntervalMs = v, 100, 2000);
                break;

            case PatternMode.Preset:
                ImGui.SetNextItemWidth(240);
                if (ImGui.BeginCombo("Preset", current.PresetName))
                {
                    foreach (var name in PresetNames)
                        if (ImGui.Selectable(name, name == current.PresetName))
                        {
                            current.PresetName = name;
                            cfg.Save();
                        }
                    ImGui.EndCombo();
                }
                ImGui.TextDisabled("Presets have a fixed intensity - the max level cap in Safety does not apply.");
                break;
        }

        EditSlider("Duration (s)", current.DurationSec, v => current.DurationSec = v, 1, 60);

        ImGui.Spacing();
        if (ImGui.Button("Test pattern")) ctl.Play(current);
        ImGui.SameLine();
        if (ImGui.Button("Stop##pattern")) ctl.Stop();
    }
    
    private void DrawTriggers()
    {
        ImGui.TextWrapped("Bind in-game events to patterns. All triggers start disabled.");
        ImGui.Spacing();

        for (var i = 0; i < cfg.Triggers.Count; i++)
        {
            var trigger = cfg.Triggers[i];
            ImGui.PushID(i);

            var header = $"{GetTriggerTitle(trigger.Kind)}{(trigger.Enabled ? "  [on]" : "")}###trigger{i}";
            if (ImGui.CollapsingHeader(header))
            {
                ImGui.TextDisabled(GetTriggerDescription(trigger.Kind));

                if (trigger.Kind != TriggerKind.Death)
                {
                    EditBool("Enabled", trigger.Enabled, v => trigger.Enabled = v);
                    PatternCombo("Pattern", trigger.PatternId, v => trigger.PatternId = v);
                    EditSlider("Cooldown (ms)", trigger.CooldownMs, v => trigger.CooldownMs = v, 0, 30000);

                }
                switch (trigger.Kind)
                {
                    case TriggerKind.DamageTaken:
                    case TriggerKind.HealReceived:
                        EditFloat("Min. % of max HP", trigger.Threshold, v => trigger.Threshold = v, 0.5f, 50f);
                        EditBool("Scale intensity with amount", trigger.ScaleWithAmount, v => trigger.ScaleWithAmount = v,
                            "Bigger hits/heals produce a stronger pattern (see 'Full intensity at' in Safety).");
                        break;

                    case TriggerKind.LowHp:
                        EditFloat("HP threshold (%)", trigger.Threshold, v => trigger.Threshold = v, 5f, 90f);
                        break;
                }

                ToySelectionCombo(cfg.Triggers[i]);
            }

            ImGui.PopID();
        }
    }

    private void PatternCombo(string label, Guid current, Action<Guid> set)
    {
        var name = cfg.Patterns.FirstOrDefault(p => p.Id == current)?.Name ?? "(none)";
        ImGui.SetNextItemWidth(240);
        if (!ImGui.BeginCombo(label, name)) return;

        foreach (var p in cfg.Patterns)
            if (ImGui.Selectable($"{p.Name}##{p.Id}", p.Id == current))
            {
                set(p.Id);
                cfg.Save();
            }
        ImGui.EndCombo();
    }


    private void ToySelectionCombo(TriggerBinding triggerBinding)
    {
        var toys = ctl.Toys;
        var preview = triggerBinding.ToyIds.Count == 0
            ? ""
            : string.Join(", ", triggerBinding.ToyIds.Select(id => toys.FirstOrDefault(x => x.Id == id)?.Name ?? id));

        ImGui.SetNextItemWidth(240);
        if (!ImGui.BeginCombo("Toys", preview)) return;

        if (ImGui.SmallButton("Use all toys"))
        {
            foreach (var toy in toys)
            {
                var containsToy = triggerBinding.ToyIds.Contains(toy.Id);
                if (!containsToy) 
                    triggerBinding.ToyIds.Add(toy.Id); 
                else 
                    triggerBinding.ToyIds.Remove(toy.Id);
            }
            cfg.Save();
        }
        
        foreach (var toy in toys)
        {
            var containsToy = triggerBinding.ToyIds.Contains(toy.Id);
            if (ImGui.Checkbox($"{toy.Name}##{toy.Id}", ref containsToy))
            {
                if (containsToy) 
                    triggerBinding.ToyIds.Add(toy.Id); 
                else 
                    triggerBinding.ToyIds.Remove(toy.Id);
                cfg.Save();
            }
        }

        // Saved assignments for toys that aren't connected right now stay visible, so they can be removed.
        foreach (var id in triggerBinding.ToyIds.Where(id => toys.All(x => x.Id != id)).ToList())
        {
            var on = true;
            if (ImGui.Checkbox($"{id} (not connected)##{id}", ref on) && !on)
            {
                triggerBinding.ToyIds.Remove(id);
                cfg.Save();
            }
        }

        ImGui.EndCombo();
    }

    private static string GetTriggerTitle(TriggerKind kind) => kind switch
    {
        TriggerKind.DamageTaken => "Damage taken",
        TriggerKind.HealReceived => "Healing received",
        TriggerKind.LowHp => "Low HP",
        TriggerKind.Death => "Death",
        TriggerKind.Revive => "Revived",
        TriggerKind.CombatStart => "Combat start",
        TriggerKind.CombatEnd => "Combat end",
        _ => kind.ToString(),
    };

    private static string GetTriggerDescription(TriggerKind kind) => kind switch
    {
        TriggerKind.DamageTaken => "Your HP dropped since the last frame.",
        TriggerKind.HealReceived => "Your HP increased since the last frame.",
        TriggerKind.LowHp => "HP fell to or below the threshold (re-arms after recovering 10% above it).",
        TriggerKind.Death => "Your HP reached 0.",
        TriggerKind.Revive => "You came back from 0 HP.",
        TriggerKind.CombatStart => "You entered combat.",
        TriggerKind.CombatEnd => "You left combat.",
        _ => "",
    };

    private void DrawSafety()
    {
        EditBool("Triggers enabled (master switch)", cfg.Enabled, v =>
        {
            cfg.Enabled = v;
            if (!v) ctl.Stop();
        });

        EditSlider("Max level cap", cfg.MaxLevel, v => cfg.MaxLevel = v, 1, 20);
        ImGui.TextDisabled("Every constant/step level is clamped to this value.");

        EditFloat("Full intensity at (% HP)", cfg.ScaleFullAtPercent, v => cfg.ScaleFullAtPercent = v, 5f, 100f);
        ImGui.TextDisabled("A hit of this size (or bigger) plays the pattern at 100% when scaling is on.");

        EditBool("Stop the toy on logout", cfg.StopOnLogout, v => cfg.StopOnLogout = v);

        ImGui.Separator();
        if (ImGui.Button("EMERGENCY STOP")) ctl.Stop();
        ImGui.TextDisabled("Chat command: /lovense stop");
    }

    private void EditText(string label, string value, Action<string> set, int maxLength, float width = 240)
    {
        var v = value;
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputText(label, ref v, maxLength)) set(v);
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
    }

    private void EditInput(string label, int value, Action<int> set)
    {
        var v = value;
        ImGui.SetNextItemWidth(140);
        if (ImGui.InputInt(label, ref v)) set(v);
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
    }

    private void EditSlider(string label, int value, Action<int> set, int min, int max)
    {
        var v = value;
        ImGui.SetNextItemWidth(240);
        if (ImGui.SliderInt(label, ref v, min, max)) set(v);
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
    }

    private void EditFloat(string label, float value, Action<float> set, float min, float max)
    {
        var v = value;
        ImGui.SetNextItemWidth(240);
        if (ImGui.SliderFloat(label, ref v, min, max)) set(v);
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
    }

    private void EditBool(string label, bool value, Action<bool> set, string? tooltip = null)
    {
        var v = value;
        if (ImGui.Checkbox(label, ref v))
        {
            set(v);
            cfg.Save();
        }
        if (tooltip is not null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
    }
}
