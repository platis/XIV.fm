using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Adapters;
using XIV.fm.Plugin.Core.Overlay;
using XIV.fm.Plugin.Core.Policy;
using XIV.fm.Plugin.Development;
using XIV.fm.Plugin.Network;
using XIV.fm.Plugin.UI;

namespace XIV.fm.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/xivfm";

    private readonly WindowSystem windowSystem = new("XIV.fm");
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chatGui;
    private readonly ICondition condition;
    private readonly PluginConfiguration configuration;
    private readonly OverlayStateStore stateStore;
    private readonly NameplateCardRenderer cardRenderer;
    private readonly DevelopmentOverlayCoordinator developmentCoordinator;
    private readonly ServerSyncCoordinator serverSyncCoordinator;
    private readonly AccountLinkCoordinator accountLinkCoordinator;
    private readonly SettingsWindow settingsWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IClientState clientState,
        IObjectTable objectTable,
        IGameGui gameGui,
        IFramework framework,
        ICondition condition)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.condition = condition;
        this.configuration = pluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        this.configuration.Version = 4;
        this.configuration.ServerBaseUrl ??= "https://xiv.fm";
        this.configuration.InstallationCredential ??= string.Empty;
        this.configuration.PendingLinkCredential ??= string.Empty;
        this.configuration.DeveloperServerBaseUrl ??= "http://127.0.0.1:5080";
        this.configuration.DeveloperInstallationCredential ??= string.Empty;
        this.configuration.RemoteCardDistanceYalms = this.configuration.NormalizedRemoteCardDistanceYalms;

        this.stateStore = new OverlayStateStore();
        this.cardRenderer = new NameplateCardRenderer(
            objectTable,
            gameGui,
            this.stateStore,
            () => this.configuration.ShowPlaceholderCards && this.CurrentDutyPolicy.AllowsOverlay,
            () => this.configuration.NormalizedRemoteCardDistanceYalms);
        this.serverSyncCoordinator = new ServerSyncCoordinator(
            framework,
            clientState,
            objectTable,
            new ServerSyncApiClient(),
            () => this.CurrentDutyPolicy,
            () => this.GetSyncSettings(),
            () => this.configuration.Visibility,
            typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0");
        this.developmentCoordinator = new DevelopmentOverlayCoordinator(
            framework,
            clientState,
            objectTable,
            this.stateStore,
            (character, now) => this.HasInstallationCredential
                ? OverlayCard.LocalListening(character, this.serverSyncCoordinator.OwnListening, now)
                : OverlayCard.LocalPlaceholder(character),
            now => this.serverSyncCoordinator.GetRemoteCards(now),
            () => this.configuration.DeveloperMockRemoteCards,
            () => this.CurrentDutyPolicy.IsInDuty);
        this.accountLinkCoordinator = new AccountLinkCoordinator(
            framework,
            new ServerSyncApiClient(),
            () => this.CurrentDutyPolicy,
            () => new AccountLinkSettings(this.GetServerBaseUri(), this.GetPendingLink()),
            this.SavePendingLink,
            this.CompleteAccountLink,
            this.ClearPendingLink,
            uri => Util.OpenLink(uri.AbsoluteUri),
            typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0");
        this.settingsWindow = new SettingsWindow(
            this.configuration,
            this.SaveConfiguration,
            this.TryStartAccountLink,
            this.accountLinkCoordinator.CancelPending,
            this.OpenCurrentLastFmPage,
            this.serverSyncCoordinator.RequestImmediateSync,
            () => this.HasInstallationCredential,
            () => this.CurrentDutyPolicy,
            () => this.accountLinkCoordinator.State,
            () => this.serverSyncCoordinator.State,
            () => this.stateStore.Current,
            () => this.cardRenderer.Diagnostics);
        this.windowSystem.AddWindow(this.settingsWindow);
        if (!this.configuration.HasSeenAccountOnboarding)
        {
            this.configuration.HasSeenAccountOnboarding = true;
            this.settingsWindow.IsOpen = true;
            this.SaveConfiguration();
        }

        this.commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open XIV.fm settings. Controls: /xivfm link, /xivfm toggle, /xivfm lastfm, /xivfm visibility <private|public>, /xivfm mock, /xivfm range <1-20>, /xivfm status.",
        });
        this.pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        this.pluginInterface.UiBuilder.Draw += this.cardRenderer.Draw;
        this.pluginInterface.UiBuilder.OpenConfigUi += this.OpenSettings;
    }

    public void Dispose()
    {
        this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenSettings;
        this.pluginInterface.UiBuilder.Draw -= this.cardRenderer.Draw;
        this.pluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        this.windowSystem.RemoveAllWindows();
        this.commandManager.RemoveHandler(CommandName);
        this.accountLinkCoordinator.Dispose();
        this.serverSyncCoordinator.Dispose();
        this.developmentCoordinator.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            this.OpenSettings();
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "link":
                var linkError = this.TryStartAccountLink();
                if (linkError is null)
                    this.chatGui.Print("Opening Last.fm authorization in your browser…", "XIV.fm");
                else
                    this.chatGui.PrintError(linkError, "XIV.fm");
                this.OpenSettings();
                break;

            case "settings":
                this.OpenSettings();
                break;

            case "toggle":
                this.configuration.ShowPlaceholderCards = !this.configuration.ShowPlaceholderCards;
                this.SaveConfiguration();
                this.PrintStatus();
                break;

            case "lastfm":
                this.OpenCurrentLastFmPage();
                break;

            case "visibility" when parts.Length == 2 &&
                Enum.TryParse<VisibilityMode>(parts[1], ignoreCase: true, out var visibility) &&
                visibility is VisibilityMode.Private or VisibilityMode.Public:
                this.configuration.Visibility = visibility;
                this.SaveConfiguration();
                this.serverSyncCoordinator.RequestImmediateSync();
                this.PrintStatus();
                break;

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
                    "Usage: /xivfm, /xivfm link, /xivfm toggle, /xivfm lastfm, /xivfm visibility <private|public>, /xivfm mock, /xivfm range <1-20>, or /xivfm status.",
                    "XIV.fm");
                break;
        }
    }

    private DutyParticipationPolicy CurrentDutyPolicy => new(DalamudDutyState.IsInDuty(this.condition));

    private void OpenSettings() => this.settingsWindow.IsOpen = true;

    private string? TryStartAccountLink()
    {
        if (this.HasInstallationCredential)
            return "A Last.fm account is already linked.";
        if (!this.TryGetServerBaseUri(out _))
            return "The configured XIV.fm server URL is invalid.";
        return this.accountLinkCoordinator.TryStart(out var error)
            ? null
            : error ?? "Account linking could not start.";
    }

    private void SaveConfiguration() => this.pluginInterface.SavePluginConfig(this.configuration);

    private void OpenCurrentLastFmPage()
    {
        var trackUrl = this.serverSyncCoordinator.OwnListening.Track?.TrackUrl;
        if (trackUrl is not null && trackUrl.Scheme == Uri.UriSchemeHttps)
        {
            Util.OpenLink(trackUrl.AbsoluteUri);
            return;
        }

        if (!string.IsNullOrWhiteSpace(this.configuration.LinkedLastFmAccountName))
        {
            Util.OpenLink(
                $"https://www.last.fm/user/{Uri.EscapeDataString(this.configuration.LinkedLastFmAccountName)}");
            return;
        }

        this.chatGui.PrintError("No linked Last.fm page is available yet.", "XIV.fm");
    }

    private bool HasInstallationCredential => this.GetSyncSettings().InstallationCredential.Length >= 32;

    private ServerSyncSettings GetSyncSettings()
    {
        if (this.configuration.DeveloperServerEnabled)
        {
            return new ServerSyncSettings(
                true,
                this.configuration.DeveloperServerBaseUrl,
                this.configuration.DeveloperInstallationCredential);
        }

        return new ServerSyncSettings(
            !string.IsNullOrEmpty(this.configuration.InstallationCredential),
            this.configuration.ServerBaseUrl,
            this.configuration.InstallationCredential);
    }

    private bool TryGetServerBaseUri(out Uri? serverBaseUri)
    {
        var baseUrl = this.configuration.DeveloperServerEnabled
            ? this.configuration.DeveloperServerBaseUrl
            : this.configuration.ServerBaseUrl;
        return ServerSyncCoordinator.TryValidateSettings(
            new ServerSyncSettings(true, baseUrl, new string('x', 32)),
            out serverBaseUri);
    }

    private Uri GetServerBaseUri() => this.TryGetServerBaseUri(out var serverBaseUri)
        ? serverBaseUri!
        : new Uri("https://xiv.fm");

    private PendingAccountLink? GetPendingLink() =>
        this.configuration.PendingLinkSessionId is Guid sessionId &&
        sessionId != Guid.Empty &&
        this.configuration.PendingLinkCredential.Length >= 32 &&
        this.configuration.PendingLinkExpiresAt is DateTimeOffset expiresAt
            ? new PendingAccountLink(sessionId, this.configuration.PendingLinkCredential, expiresAt)
            : null;

    private void SavePendingLink(PendingAccountLink pending)
    {
        this.configuration.PendingLinkSessionId = pending.SessionId;
        this.configuration.PendingLinkCredential = pending.Credential;
        this.configuration.PendingLinkExpiresAt = pending.ExpiresAt;
        this.SaveConfiguration();
    }

    private void CompleteAccountLink(string credential, string accountName)
    {
        if (this.configuration.DeveloperServerEnabled)
            this.configuration.DeveloperInstallationCredential = credential;
        else
            this.configuration.InstallationCredential = credential;
        this.configuration.LinkedLastFmAccountName = accountName;
        this.ClearPendingLink(save: false);
        this.SaveConfiguration();
        this.chatGui.Print($"Linked Last.fm account {accountName}.", "XIV.fm");
    }

    private void ClearPendingLink() => this.ClearPendingLink(save: true);

    private void ClearPendingLink(bool save)
    {
        this.configuration.PendingLinkSessionId = null;
        this.configuration.PendingLinkCredential = string.Empty;
        this.configuration.PendingLinkExpiresAt = null;
        if (save)
            this.SaveConfiguration();
    }

    private void PrintStatus()
    {
        var cards = this.configuration.ShowPlaceholderCards ? "on" : "off";
        var mocks = this.configuration.DeveloperMockRemoteCards ? "on" : "off";
        var snapshot = this.stateStore.Current;
        var diagnostics = this.cardRenderer.Diagnostics;
        var location = snapshot.Location?.ToString() ?? "unavailable";
        var dutyPolicy = this.CurrentDutyPolicy;
        var duty = dutyPolicy.IsInDuty ? "yes" : "no";
        var participation = dutyPolicy.AllowsServerRequests ? "active" : "suspended";
        var sync = this.serverSyncCoordinator.State;
        var syncDetail = sync.Error is null ? sync.Status.ToString().ToLowerInvariant() : $"{sync.Status.ToString().ToLowerInvariant()} ({sync.Error})";
        var link = this.accountLinkCoordinator.State;
        var linkDetail = this.configuration.LinkedLastFmAccountName ??
            (link.Error is null ? link.Status.ToString().ToLowerInvariant() : $"{link.Status.ToString().ToLowerInvariant()} ({link.Error})");
        var anchorHeight = diagnostics.LocalNameplateHeightYalms is float height
            ? $"{height:F2} yalms"
            : "unavailable";
        this.chatGui.Print(
            $"Cards: {cards}; Last.fm: {linkDetail}; visibility: {this.configuration.Visibility.ToString().ToLowerInvariant()}; remote mocks: {mocks}; range: {this.configuration.NormalizedRemoteCardDistanceYalms} yalms; duty: {duty}; participation: {participation}; sync: {syncDetail}; snapshot: {snapshot.Cards.Length}; anchor height: {anchorHeight}; render requested/matched/in-range/projected/drawn: {diagnostics.RequestedCards}/{diagnostics.MatchedPlayers}/{diagnostics.InRangePlayers}/{diagnostics.ProjectedAnchors}/{diagnostics.RenderedCards}; {location}.",
            "XIV.fm");
    }
}
