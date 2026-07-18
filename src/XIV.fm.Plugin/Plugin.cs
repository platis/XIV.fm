using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using XIV.fm.Plugin.UI;

namespace XIV.fm.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/xivfm";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly WindowSystem windowSystem = new("XIV.fm");
    private readonly NameplateCardWindow cardWindow;
    private readonly PluginConfiguration configuration;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IObjectTable objectTable,
        IGameGui gameGui)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.configuration = pluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        this.cardWindow = new NameplateCardWindow(objectTable, gameGui, () => this.configuration.ShowPlaceholderCard);

        this.windowSystem.AddWindow(this.cardWindow);
        this.commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Toggle the temporary XIV.fm nameplate card.",
        });
        this.pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
    }

    public void Dispose()
    {
        this.pluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        this.commandManager.RemoveHandler(CommandName);
        this.windowSystem.RemoveAllWindows();
    }

    private void OnCommand(string command, string arguments)
    {
        this.configuration.ShowPlaceholderCard = !this.configuration.ShowPlaceholderCard;
        this.pluginInterface.SavePluginConfig(this.configuration);
    }
}
