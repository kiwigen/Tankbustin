using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Tankbustin.Windows;

namespace Tankbustin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/tankbustin";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly Configuration cfg;
    private readonly LovenseController controller;
    private readonly TriggerEngine engine;
    private readonly WindowSystem windows = new("Tankbustin");
    private readonly ConfigWindow configWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commands,
        IChatGui chat,
        IPluginLog log,
        IFramework framework,
        IObjectTable objects,
        ICondition condition)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.chat = chat;

        cfg = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        cfg.Initialize(pluginInterface);

        controller = new LovenseController(cfg, log);
        engine = new TriggerEngine(framework, objects, condition, log, cfg, controller);

        configWindow = new ConfigWindow(cfg, controller);
        windows.AddWindow(configWindow);

        commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Tankbustin Link settings. Use '/tankbustin stop' to stop the toy, '/tankbustin toggle' to enable/disable triggers.",
        });

        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleConfig;
        pluginInterface.UiBuilder.OpenMainUi += ToggleConfig;

        if (cfg.AutoConnect)
            controller.Connect();
    }

    private void ToggleConfig() => configWindow.Toggle();

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "stop":
                controller.Stop();
                chat.Print("Lovense Link: stop sent.");
                break;

            case "toggle":
                cfg.Enabled = !cfg.Enabled;
                cfg.Save();
                if (!cfg.Enabled) controller.Stop();
                chat.Print($"Lovense Link: triggers {(cfg.Enabled ? "enabled" : "disabled")}.");
                break;

            default:
                configWindow.Toggle();
                break;
        }
    }

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleConfig;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleConfig;

        commands.RemoveHandler(CommandName);
        windows.RemoveAllWindows();

        engine.Dispose();
        controller.Dispose(); // sends a final Stop before shutting down
    }
}
