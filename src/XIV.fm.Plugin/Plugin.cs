using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using XIV.fm.Plugin.Core.Overlay;
using XIV.fm.Plugin.Development;
using XIV.fm.Plugin.UI;

namespace XIV.fm.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/xivfm";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chatGui;
    private readonly PluginConfiguration configuration;
    private readonly OverlayStateStore stateStore;
    private readonly NameplateCardRenderer cardRenderer;
    private readonly DevelopmentOverlayCoordinator developmentCoordinator;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IClientState clientState,
        IObjectTable objectTable,
        IGameGui gameGui,
        IFramework framework)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.configuration = pluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        this.configuration.RemoteCardDistanceYalms = this.configuration.NormalizedRemoteCardDistanceYalms;

        this.stateStore = new OverlayStateStore();
        this.cardRenderer = new NameplateCardRenderer(
            objectTable,
            gameGui,
            this.stateStore,
            () => this.configuration.ShowPlaceholderCards,
            () => this.configuration.NormalizedRemoteCardDistanceYalms);
        this.developmentCoordinator = new DevelopmentOverlayCoordinator(
            framework,
            clientState,
            objectTable,
            this.stateStore,
            () => this.configuration.DeveloperMockRemoteCards);

        this.commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "XIV.fm development controls: /xivfm, /xivfm mock, /xivfm range <1-20>, /xivfm status.",
        });
        this.pluginInterface.UiBuilder.Draw += this.cardRenderer.Draw;
    }

    public void Dispose()
    {
        this.pluginInterface.UiBuilder.Draw -= this.cardRenderer.Draw;
        this.commandManager.RemoveHandler(CommandName);
        this.developmentCoordinator.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            this.configuration.ShowPlaceholderCards = !this.configuration.ShowPlaceholderCards;
            this.SaveConfiguration();
            this.PrintStatus();
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "mock":
                this.configuration.DeveloperMockRemoteCards = !this.configuration.DeveloperMockRemoteCards;
                this.SaveConfiguration();
                this.PrintStatus();
                break;

            case "range" when parts.Length == 2 && int.TryParse(parts[1], out var requestedDistance):
                this.configuration.RemoteCardDistanceYalms = OverlayVisibility.NormalizeRemoteDistance(requestedDistance);
                this.SaveConfiguration();
                this.PrintStatus();
                break;

            case "status":
                this.PrintStatus();
                break;

            default:
                this.chatGui.PrintError(
                    "Usage: /xivfm, /xivfm mock, /xivfm range <1-20>, or /xivfm status.",
                    "XIV.fm");
                break;
        }
    }

    private void SaveConfiguration() => this.pluginInterface.SavePluginConfig(this.configuration);

    private void PrintStatus()
    {
        var cards = this.configuration.ShowPlaceholderCards ? "on" : "off";
        var mocks = this.configuration.DeveloperMockRemoteCards ? "on" : "off";
        var snapshot = this.stateStore.Current;
        var location = snapshot.Location?.ToString() ?? "unavailable";
        this.chatGui.Print(
            $"Cards: {cards}; remote mocks: {mocks}; range: {this.configuration.NormalizedRemoteCardDistanceYalms} yalms; snapshot cards: {snapshot.Cards.Length}; {location}.",
            "XIV.fm");
    }
}
