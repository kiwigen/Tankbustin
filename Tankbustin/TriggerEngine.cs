using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace Tankbustin;

/// <summary>Polls the local player once per framework tick and turns state changes into trigger events.</summary>
public sealed class TriggerEngine : IDisposable
{
    private readonly IFramework framework;
    private readonly IObjectTable objects;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly Configuration cfg;
    private readonly LovenseController controller;
    private readonly Dictionary<TriggerKind, long> lastFire = new();

    private bool hadPlayer;
    private bool wasInCombat;
    private bool lowFired;
    private uint? prevHp;
    private uint prevMax;
    private bool weaknessDebuffed;
    private bool brinkOfDeathDebuffed;

    private const uint WeaknessId = 0;
    private const uint BrinkOfDeathId = 1;
    
    public TriggerEngine(IFramework framework, IObjectTable objects, ICondition condition, IPluginLog log, 
        Configuration cfg, LovenseController controller)
    {
        this.framework = framework;
        this.objects = objects;
        this.condition = condition;
        this.log = log;
        this.cfg = cfg;
        this.controller = controller;

        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    private void OnUpdate(IFramework _)
    {
        var player = objects.LocalPlayer;
        if (player is null)
        {
            if (hadPlayer)
            {
                hadPlayer = false;
                prevHp = null;
                if (cfg.StopOnLogout) controller.Stop();
            }
            return;
        }

        uint currentHp = player.CurrentHp;
        uint maxHp = player.MaxHp;
        if (maxHp == 0) return;

        var inCombat = condition[ConditionFlag.InCombat];
        var hpPct = currentHp * 100f / maxHp;
        
        if (!hadPlayer)
        {
            // First frame after login: just sync state, don't fire anything.
            hadPlayer = true;
            wasInCombat = inCombat;
            lowFired = hpPct <= Threshold(TriggerKind.LowHp, 25f);
            prevHp = currentHp;
            prevMax = maxHp;
            return;
        }
        brinkOfDeathDebuffed = false;
        weaknessDebuffed = false;
        var playerStatus = player.StatusList;
        foreach (var status in playerStatus)
        {
            if (status.StatusId == 0)
                continue;
#if DEBUG
            log.Debug($"Status received: {status.StatusId}, {status.GameData.Value.Name}");
#endif
            switch (status.StatusId)
            {
                case BrinkOfDeathId:
                    brinkOfDeathDebuffed = true;
                    weaknessDebuffed = false;
                    break;
                case WeaknessId:
                    weaknessDebuffed = true;
                    brinkOfDeathDebuffed = false;
                    break;
            }
        } 
        // Max HP changes (level sync, gear swap, food) shouldn't be mistaken for damage/healing.
        
        if (prevHp is { } previosHp && prevMax == maxHp)
        {
            if (currentHp < previosHp)
                Fire(TriggerKind.DamageTaken, (previosHp - currentHp) * 100f / maxHp);
            else if (currentHp > previosHp)
            {
                if(inCombat) 
                    Fire(TriggerKind.HealReceived, (currentHp - previosHp) * 100f / maxHp);
            }

            if (previosHp > 0 && currentHp == 0)
                controller.Stop(); //Dea has been a bad tank and died, no good vibes for her.
            else if (previosHp == 0 && currentHp > 0)
            {
                //Getting revived? Smh. Reducing the strength of vibes until debuff runs out.
                Fire(TriggerKind.Revive, 100f);
            }
        }
        prevHp = currentHp;
        prevMax = maxHp;

        // Low HP: fire once when crossing the threshold, re-arm after recovering 10% above it.
        var lowThreshold = Threshold(TriggerKind.LowHp, 25f);
        if (currentHp > 0 && hpPct <= lowThreshold && !lowFired)
        {
            lowFired = true;
            Fire(TriggerKind.LowHp, 100f - hpPct);
        }
        else if (hpPct > lowThreshold + 10f)
        {
            lowFired = false;
        }

        if (inCombat != wasInCombat)
        {
#if DEBUG
            log.Debug($"Triggering in combat {inCombat}");   
#endif
            wasInCombat = inCombat;
            Fire(inCombat ? TriggerKind.CombatStart : TriggerKind.CombatEnd, 100f);
        }
    }

    private float Threshold(TriggerKind kind, float fallback)
        => cfg.Triggers.FirstOrDefault(t => t.Kind == kind)?.Threshold ?? fallback;

    private void Fire(TriggerKind kind, float amountPct)
    {
        if (!cfg.Enabled) return;

        var binding = cfg.Triggers.FirstOrDefault(t => t.Kind == kind);
        if (binding is not { Enabled: true }) return;

        if ((kind is TriggerKind.DamageTaken or TriggerKind.HealReceived) && amountPct < binding.Threshold)
            return;

        var now = Environment.TickCount64;
        if (lastFire.TryGetValue(kind, out var last) && now - last < binding.CooldownMs)
            return;

        var pattern = cfg.Patterns.FirstOrDefault(p => p.Id == binding.PatternId);
        if (pattern is null) return;

        lastFire[kind] = now;

        var scale = binding.ScaleWithAmount
            ? Math.Clamp(amountPct / Math.Max(1f, cfg.ScaleFullAtPercent), 0.25f, 1f)
            : 1f;
        
        //Bad Dea!
        if (weaknessDebuffed) //Both Debuffs can't be active at the same time
            scale *= 0.75f;
        if(brinkOfDeathDebuffed)
            scale *= 0.5f;

        controller.Play(pattern, scale, binding.ToyIds);
    }
}
