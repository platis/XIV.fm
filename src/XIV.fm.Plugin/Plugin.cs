using System.Diagnostics;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Adapters;
using XIV.fm.Plugin.Core.Overlay;
using XIV.fm.Plugin.Core.Policy;
using XIV.fm.Plugin.Core.Presence;
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
    private readonly OverlayDtrBarController overlayDtrBarController;
    private readonly OverlayStateStore stateStore;
    private readonly AlbumArtworkCache artworkCache;
    private readonly AlbumArtworkCoordinator artworkCoordinator;
    private readonly NameplateCardRenderer cardRenderer;
    private readonly DevelopmentOverlayCoordinator developmentCoordinator;
    private readonly ServerSyncCoordinator serverSyncCoordinator;
    private readonly AccountLinkCoordinator accountLinkCoordinator;
    private readonly AccountDisconnectCoordinator accountDisconnectCoordinator;
    private readonly RelayCoordinator relayCoordinator;
    private readonly SettingsWindow settingsWindow;
    private readonly OnboardingWindow onboardingWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IClientState clientState,
        IObjectTable objectTable,
        IGameGui gameGui,
        IDtrBar dtrBar,
        ITextureProvider textureProvider,
        IFramework framework,
        ICondition condition)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.condition = condition;
        this.configuration = pluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        this.configuration.Version = 9;
        this.configuration.ServerBaseUrl ??= "https://xiv.fm";
        this.configuration.InstallationCredential ??= string.Empty;
        this.configuration.PendingLinkCredential ??= string.Empty;
        this.configuration.PendingLinkAuthorizationUrl ??= string.Empty;
        if (string.IsNullOrWhiteSpace(this.configuration.DeveloperServerBaseUrl) ||
            string.Equals(
                this.configuration.DeveloperServerBaseUrl,
                PluginConfiguration.LegacyDeveloperServerBaseUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            this.configuration.DeveloperServerBaseUrl = PluginConfiguration.DefaultDeveloperServerBaseUrl;
        }
        this.configuration.DeveloperInstallationCredential ??= string.Empty;
        this.configuration.SelectedRelayIds ??= [];
        this.configuration.SelectedRelayIds = RelaySelection.Normalize(this.configuration.SelectedRelayIds).ToList();
        if (this.configuration.Visibility == VisibilityMode.Custom && this.configuration.SelectedRelayIds.Count == 0)
            this.configuration.Visibility = VisibilityMode.Private;
        this.configuration.RemoteCardDistanceYalms = this.configuration.NormalizedRemoteCardDistanceYalms;
        this.configuration.CardOpacityPercent = this.configuration.NormalizedCardOpacityPercent;
        this.configuration.OwnCardSizePercent = this.configuration.NormalizedOwnCardSizePercent;
        this.configuration.OtherCardSizePercent = this.configuration.NormalizedOtherCardSizePercent;

        this.overlayDtrBarController = new OverlayDtrBarController(
            dtrBar,
            () => this.configuration.ShowPlaceholderCards,
            this.ToggleOverlay,
            this.OpenSettings);
        this.stateStore = new OverlayStateStore();
        this.artworkCache = new AlbumArtworkCache(textureProvider);
        this.artworkCoordinator = new AlbumArtworkCoordinator(
            framework,
            this.stateStore,
            this.artworkCache,
            () => this.CurrentDutyPolicy);
        this.cardRenderer = new NameplateCardRenderer(
            objectTable,
            gameGui,
            this.stateStore,
            this.artworkCache,
            () => this.configuration.ShowPlaceholderCards && this.CurrentDutyPolicy.AllowsOverlay,
            () => this.configuration.ShowOwnListeningCard,
            () => this.configuration.NormalizedRemoteCardDistanceYalms,
            () => CardAppearance.ToOpacity(this.configuration.NormalizedCardOpacityPercent),
            () => CardAppearance.ToScale(this.configuration.NormalizedOwnCardSizePercent),
            () => CardAppearance.ToScale(this.configuration.NormalizedOtherCardSizePercent));
        DevelopmentOverlayCoordinator? overlayCoordinator = null;
        this.serverSyncCoordinator = new ServerSyncCoordinator(
            framework,
            clientState,
            objectTable,
            new ServerSyncApiClient(),
            () => this.CurrentDutyPolicy,
            () => this.GetSyncSettings(),
            this.GetVisibilitySelection,
            () => overlayCoordinator?.RequestCapture(),
            typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0");
        this.developmentCoordinator = new DevelopmentOverlayCoordinator(
            framework,
            clientState,
            objectTable,
            this.stateStore,
            (character, now) => this.HasInstallationCredential
                ? OverlayCard.LocalListening(character, this.serverSyncCoordinator.OwnListening, now)
                : null,
            now => this.serverSyncCoordinator.GetRemoteCards(now),
            () => this.configuration.DeveloperMockRemoteCards,
            () => this.CurrentDutyPolicy.IsInDuty);
        overlayCoordinator = this.developmentCoordinator;
        this.accountLinkCoordinator = new AccountLinkCoordinator(
            framework,
            new ServerSyncApiClient(),
            () => this.CurrentDutyPolicy,
            () => new AccountLinkSettings(this.GetServerBaseUri(), this.GetPendingLink()),
            this.SavePendingLink,
            this.CompleteAccountLink,
            this.ClearPendingLink,
            this.TryOpenAuthorizationLink,
            typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0");
        this.accountDisconnectCoordinator = new AccountDisconnectCoordinator(
            framework,
            new ServerSyncApiClient(),
            () => this.CurrentDutyPolicy,
            () => this.GetSyncSettings(),
            this.CompleteAccountDisconnect,
            typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0");
        this.relayCoordinator = new RelayCoordinator(
            framework,
            new ServerSyncApiClient(),
            () => this.CurrentDutyPolicy,
            () => this.GetSyncSettings(),
            this.ReconcileRelaySelections,
            typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0");
        this.settingsWindow = new SettingsWindow(
            this.configuration,
            this.SaveConfiguration,
            this.TryStartAccountLink,
            this.accountLinkCoordinator.CancelPending,
            this.TryDisconnectAccount,
            this.OpenCurrentLastFmPage,
            this.TryOpenAuthorizationLink,
            this.serverSyncCoordinator.RequestImmediateSync,
            () => this.HasInstallationCredential,
            () => this.CurrentDutyPolicy,
            () => this.accountLinkCoordinator.State,
            () => this.accountDisconnectCoordinator.State,
            () => this.serverSyncCoordinator.State,
            () => this.stateStore.Current,
            () => this.cardRenderer.Diagnostics,
            this.relayCoordinator);
        this.onboardingWindow = new OnboardingWindow(
            this.TryStartAccountLink,
            this.OpenSettings,
            this.OpenGitHub,
            this.CompleteOnboarding,
            () => this.HasInstallationCredential,
            () => this.CurrentDutyPolicy);
        this.windowSystem.AddWindow(this.settingsWindow);
        this.windowSystem.AddWindow(this.onboardingWindow);
        if (!this.configuration.HasSeenAccountOnboarding)
            this.onboardingWindow.IsOpen = true;

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
        this.overlayDtrBarController.Dispose();
        this.artworkCoordinator.Dispose();
        this.artworkCache.Dispose();
        this.accountLinkCoordinator.Dispose();
        this.accountDisconnectCoordinator.Dispose();
        this.relayCoordinator.Dispose();
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
                    this.chatGui.Print("Creating a Last.fm connection link…", "XIV.fm");
                else
                    this.chatGui.PrintError(linkError, "XIV.fm");
                this.OpenSettings();
                break;

            case "settings":
                this.OpenSettings();
                break;

            case "toggle":
                this.ToggleOverlay();
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

    private void OpenSettings()
    {
        this.onboardingWindow.Complete();
        this.settingsWindow.IsOpen = true;
        if (this.HasInstallationCredential)
            this.relayCoordinator.EnsureLoaded();
    }

    private void OpenGitHub() => Util.OpenLink("https://github.com/platis/XIV.fm");

    private string? TryOpenAuthorizationLink(Uri authorizationUri)
    {
        if (authorizationUri.Scheme != Uri.UriSchemeHttps)
            return "The Last.fm authorization link is invalid. Copy the link and open it manually.";

        try
        {
            using var browser = Process.Start(new ProcessStartInfo
            {
                FileName = authorizationUri.AbsoluteUri,
                UseShellExecute = true,
            });
            if (browser is not null)
                return null;
        }
        catch
        {
            // Dalamud's browser helper handles environments where shell execution is unavailable.
        }

        try
        {
            Util.OpenLink(authorizationUri.AbsoluteUri);
            return null;
        }
        catch
        {
            return "Your default browser could not be opened. Copy the connection link and open it manually.";
        }
    }

    private void CompleteOnboarding()
    {
        if (this.configuration.HasSeenAccountOnboarding)
            return;

        this.configuration.HasSeenAccountOnboarding = true;
        this.SaveConfiguration();
    }

    private void ToggleOverlay()
    {
        this.configuration.ShowPlaceholderCards = !this.configuration.ShowPlaceholderCards;
        this.SaveConfiguration();
    }

    private string? TryDisconnectAccount()
    {
        if (!this.accountDisconnectCoordinator.TryStart(out var error))
            return error ?? "Last.fm could not be disconnected.";

        this.serverSyncCoordinator.SuspendForAccountDisconnect();
        return null;
    }

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

    private void SaveConfiguration()
    {
        this.pluginInterface.SavePluginConfig(this.configuration);
        this.overlayDtrBarController.Refresh();
    }

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

    private VisibilitySelection GetVisibilitySelection()
    {
        if (this.configuration.Visibility != VisibilityMode.Custom)
            return new VisibilitySelection(this.configuration.Visibility, []);

        var relayIds = RelaySelection.Normalize(this.configuration.SelectedRelayIds);
        return relayIds.IsEmpty
            ? new VisibilitySelection(VisibilityMode.Private, [])
            : new VisibilitySelection(VisibilityMode.Custom, relayIds);
    }

    private void ReconcileRelaySelections(
        IReadOnlyCollection<Guid> joinedRelayIds,
        Guid? autoSelectedRelayId)
    {
        var joined = joinedRelayIds.ToHashSet();
        var previousRelayIds = this.configuration.SelectedRelayIds;
        var previousVisibility = this.configuration.Visibility;
        var relayIds = RelaySelection.Normalize(previousRelayIds)
            .Where(joined.Contains)
            .ToList();

        if (autoSelectedRelayId is Guid selectedRelayId && joined.Contains(selectedRelayId))
        {
            relayIds = RelaySelection.Select(relayIds, selectedRelayId).ToList();
            this.configuration.Visibility = VisibilityMode.Custom;
        }
        else if (relayIds.Count == 0 && this.configuration.Visibility == VisibilityMode.Custom)
        {
            this.configuration.Visibility = VisibilityMode.Private;
        }

        if (previousRelayIds.SequenceEqual(relayIds) && previousVisibility == this.configuration.Visibility)
            return;

        this.configuration.SelectedRelayIds = relayIds;
        this.SaveConfiguration();
        this.serverSyncCoordinator.RequestImmediateSync();
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

    private PendingAccountLink? GetPendingLink()
    {
        if (this.configuration.PendingLinkSessionId is not Guid sessionId ||
            sessionId == Guid.Empty ||
            this.configuration.PendingLinkCredential.Length < 32 ||
            this.configuration.PendingLinkExpiresAt is not DateTimeOffset expiresAt ||
            !Uri.TryCreate(this.configuration.PendingLinkAuthorizationUrl, UriKind.Absolute, out var authorizationUri) ||
            authorizationUri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return new PendingAccountLink(
            sessionId,
            this.configuration.PendingLinkCredential,
            expiresAt,
            authorizationUri);
    }

    private void SavePendingLink(PendingAccountLink pending)
    {
        this.configuration.PendingLinkSessionId = pending.SessionId;
        this.configuration.PendingLinkCredential = pending.Credential;
        this.configuration.PendingLinkExpiresAt = pending.ExpiresAt;
        this.configuration.PendingLinkAuthorizationUrl = pending.AuthorizationUri.AbsoluteUri;
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
        this.accountDisconnectCoordinator.Reset();
        this.relayCoordinator.Reset();
        this.SaveConfiguration();
        this.relayCoordinator.EnsureLoaded();
        this.chatGui.Print($"Linked Last.fm account {accountName}.", "XIV.fm");
    }

    private void CompleteAccountDisconnect()
    {
        if (this.configuration.DeveloperServerEnabled)
            this.configuration.DeveloperInstallationCredential = string.Empty;
        else
            this.configuration.InstallationCredential = string.Empty;
        this.configuration.LinkedLastFmAccountName = null;
        this.configuration.Visibility = VisibilityMode.Private;
        this.configuration.SelectedRelayIds.Clear();
        this.relayCoordinator.Reset();
        this.ClearPendingLink(save: false);
        this.SaveConfiguration();
        this.serverSyncCoordinator.SuspendForAccountDisconnect();
        this.developmentCoordinator.RequestCapture();
        this.chatGui.Print("Last.fm disconnected from this XIV.fm installation.", "XIV.fm");
    }

    private void ClearPendingLink() => this.ClearPendingLink(save: true);

    private void ClearPendingLink(bool save)
    {
        this.configuration.PendingLinkSessionId = null;
        this.configuration.PendingLinkCredential = string.Empty;
        this.configuration.PendingLinkExpiresAt = null;
        this.configuration.PendingLinkAuthorizationUrl = string.Empty;
        if (save)
            this.SaveConfiguration();
    }

    private void PrintStatus()
    {
        var cards = this.configuration.ShowPlaceholderCards ? "on" : "off";
        var ownCard = this.configuration.ShowOwnListeningCard ? "on" : "off";
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
            $"Cards: {cards}; own card: {ownCard}; Last.fm: {linkDetail}; visibility: {this.configuration.Visibility.ToString().ToLowerInvariant()}; opacity: {this.configuration.NormalizedCardOpacityPercent}%; card size own/others: {this.configuration.NormalizedOwnCardSizePercent}%/{this.configuration.NormalizedOtherCardSizePercent}%; remote mocks: {mocks}; range: {this.configuration.NormalizedRemoteCardDistanceYalms} yalms; duty: {duty}; participation: {participation}; sync: {syncDetail}; snapshot: {snapshot.Cards.Length}; anchor height: {anchorHeight}; render requested/matched/in-range/projected/drawn: {diagnostics.RequestedCards}/{diagnostics.MatchedPlayers}/{diagnostics.InRangePlayers}/{diagnostics.ProjectedAnchors}/{diagnostics.RenderedCards}; {location}.",
            "XIV.fm");
    }
}
