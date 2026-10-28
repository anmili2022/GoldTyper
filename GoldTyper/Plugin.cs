global using static GoldTyper.Plugin;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using ECommons.DalamudServices;
using GoldTyper.Game;
using GoldTyper.Windows;

namespace GoldTyper;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "金蝶打字通";
    private const string CommandName = "/gt";
    private const string CommandNameCn = "/打字通";
    public static Plugin P = null!;
    public static Configuration Configuration = null!;
    public IDalamudPluginInterface PluginInterface { get; }
    public WindowSystem WindowSystem { get; } = new("GoldTyper");
    private DebugWindow DebugWindow { get; }

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        P = this;
        PluginInterface = pluginInterface;
        ECommonsMain.Init(pluginInterface, this, new Module[] { });

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        GameAutomation.Instance.Initialize();
        GameDiagnostics.Instance.Initialize();

        DebugWindow = new DebugWindow();
        WindowSystem.AddWindow(DebugWindow);

        Svc.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开金蝶打字通设置"
        });
        Svc.Commands.AddHandler(CommandNameCn, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开金蝶打字通设置"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        Svc.Commands.RemoveHandler(CommandName);
        Svc.Commands.RemoveHandler(CommandNameCn);
        WindowSystem.RemoveAllWindows();
        GameAutomation.Instance.Dispose();
        GameDiagnostics.Instance.Dispose();
        DebugWindow.Dispose();
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args) => DebugWindow.IsOpen = true;

    private void OpenConfigUi() => DebugWindow.IsOpen = true;
}
