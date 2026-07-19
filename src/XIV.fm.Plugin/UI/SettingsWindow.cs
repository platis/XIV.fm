using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Overlay;
using XIV.fm.Plugin.Core.Policy;
using XIV.fm.Plugin.Core.Sync;
using XIV.fm.Plugin.Network;

namespace XIV.fm.Plugin.UI;

public sealed class SettingsWindow : Window
{
    private static readonly Vector4 Accent = new(0.88f, 0.23f, 0.36f, 1f);
    private readonly PluginConfiguration configuration;
    private readonly Action saveConfiguration;
    private readonly Func<string?> startAccountLink;
    private readonly Action cancelAccountLink;
    private readonly Action openLastFm;
    private readonly Action requestSync;
    private readonly Func<bool> hasInstallationCredential;
    private readonly Func<DutyParticipationPolicy> dutyPolicy;
    private readonly Func<AccountLinkRuntimeState> linkState;
    private readonly Func<SyncRuntimeState> syncState;
    private readonly Func<OverlaySnapshot> overlaySnapshot;
    private readonly Func<OverlayRenderDiagnostics> renderDiagnostics;
    private string? interactionMessage;

    public SettingsWindow(
        PluginConfiguration configuration,
        Action saveConfiguration,
        Func<string?> startAccountLink,
        Action cancelAccountLink,
        Action openLastFm,
        Action requestSync,
        Func<bool> hasInstallationCredential,
        Func<DutyParticipationPolicy> dutyPolicy,
        Func<AccountLinkRuntimeState> linkState,
        Func<SyncRuntimeState> syncState,
        Func<OverlaySnapshot> overlaySnapshot,
        Func<OverlayRenderDiagnostics> renderDiagnostics)
        : base("XIV.fm Settings###XIV.fm.Settings")
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.startAccountLink = startAccountLink;
        this.cancelAccountLink = cancelAccountLink;
        this.openLastFm = openLastFm;
        this.requestSync = requestSync;
        this.hasInstallationCredential = hasInstallationCredential;
        this.dutyPolicy = dutyPolicy;
        this.linkState = linkState;
        this.syncState = syncState;
        this.overlaySnapshot = overlaySnapshot;
        this.renderDiagnostics = renderDiagnostics;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560f, 440f),
            MaximumSize = new Vector2(960f, 860f),
        };
    }

    public override void Draw()
    {
        DrawHeader();
        ImGui.Spacing();
        if (!ImGui.BeginTabBar("XIV.fm.Settings.Tabs"))
            return;

        if (ImGui.BeginTabItem("Account"))
        {
            this.DrawAccountTab();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Overlay"))
        {
            this.DrawOverlayTab();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Privacy"))
        {
            this.DrawPrivacyTab();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Custom Relays"))
        {
            this.DrawRelaysTab();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Diagnostics"))
        {
            this.DrawDiagnosticsTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private static void DrawHeader()
    {
        ImGui.TextColored(Accent, "XIV.fm");
        ImGui.SameLine();
        ImGui.TextDisabled("Last.fm listening presence for FFXIV");
        ImGui.Separator();
    }

    private void DrawAccountTab()
    {
        var duty = this.dutyPolicy();
        var state = this.linkState();
        var linked = this.hasInstallationCredential();

        ImGui.TextUnformatted("Last.fm account");
        ImGui.TextWrapped("Connect in your browser to prove ownership. XIV.fm never stores your Last.fm password, and the temporary Last.fm session is discarded after linking.");
        ImGui.Spacing();

        if (linked)
        {
            DrawStatusBadge("Connected", new Vector4(0.32f, 0.78f, 0.48f, 1f));
            ImGui.SameLine();
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(this.configuration.LinkedLastFmAccountName)
                ? "Linked account"
                : this.configuration.LinkedLastFmAccountName);
            ImGui.Spacing();
            if (ImGui.Button("Open Last.fm"))
                this.openLastFm();
            ImGui.SameLine();
            if (ImGui.Button("Sync now"))
                this.requestSync();
            ImGui.Spacing();
            ImGui.TextDisabled("Account disconnection will be added with credential-revocation confirmation before Phase 6 is completed.");
            return;
        }

        switch (state.Status)
        {
            case AccountLinkRuntimeStatus.Starting:
                DrawStatusBadge("Starting", new Vector4(0.95f, 0.71f, 0.25f, 1f));
                ImGui.TextWrapped("Creating a secure browser-link session…");
                break;
            case AccountLinkRuntimeStatus.WaitingForBrowser:
                DrawStatusBadge("Waiting for browser", new Vector4(0.95f, 0.71f, 0.25f, 1f));
                ImGui.TextWrapped("Approve XIV.fm in the browser. This page updates automatically when authorization completes.");
                if (ImGui.Button("Cancel and start over"))
                {
                    this.cancelAccountLink();
                    this.interactionMessage = "The pending link was cleared.";
                }
                break;
            case AccountLinkRuntimeStatus.SuspendedDuty:
                DrawStatusBadge("Suspended in duty", new Vector4(0.95f, 0.54f, 0.25f, 1f));
                ImGui.TextWrapped("Leave the duty before linking. XIV.fm makes no server requests while bound by duty.");
                break;
            case AccountLinkRuntimeStatus.Failed:
                DrawStatusBadge("Link failed", new Vector4(0.95f, 0.35f, 0.35f, 1f));
                ImGui.TextWrapped(state.Error ?? "Account linking failed. You can try again.");
                if (this.configuration.PendingLinkSessionId is not null)
                {
                    ImGui.TextDisabled("XIV.fm will retry automatically while this session remains valid.");
                    if (ImGui.Button("Cancel and start over"))
                    {
                        this.cancelAccountLink();
                        this.interactionMessage = "The pending link was cleared.";
                    }
                }
                else
                {
                    this.DrawLinkButton(duty);
                }
                break;
            default:
                DrawStatusBadge("Not connected", new Vector4(0.65f, 0.65f, 0.65f, 1f));
                ImGui.TextWrapped("Connect Last.fm to show your real listening state in game.");
                this.DrawLinkButton(duty);
                break;
        }

        if (!string.IsNullOrWhiteSpace(this.interactionMessage))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(this.interactionMessage);
        }
    }

    private void DrawLinkButton(DutyParticipationPolicy duty)
    {
        if (!duty.AllowsServerRequests)
            ImGui.BeginDisabled();
        if (ImGui.Button("Connect Last.fm in browser", new Vector2(230f, 0f)))
        {
            var error = this.startAccountLink();
            this.interactionMessage = error ?? "Opening Last.fm authorization in your browser…";
        }
        if (!duty.AllowsServerRequests)
            ImGui.EndDisabled();
    }

    private void DrawOverlayTab()
    {
        var changed = false;
        var cards = this.configuration.ShowPlaceholderCards;
        if (ImGui.Checkbox("Show listening cards", ref cards))
        {
            this.configuration.ShowPlaceholderCards = cards;
            changed = true;
        }

        var range = this.configuration.NormalizedRemoteCardDistanceYalms;
        if (ImGui.SliderInt("Remote card distance", ref range, OverlayVisibility.MinimumRemoteDistanceYalms, OverlayVisibility.MaximumRemoteDistanceYalms, "%d yalms"))
        {
            this.configuration.RemoteCardDistanceYalms = OverlayVisibility.NormalizeRemoteDistance(range);
            changed = true;
        }
        ImGui.TextDisabled("Remote cards are matched to loaded characters and filtered locally. Default: 8 yalms.");

        if (changed)
            this.saveConfiguration();
    }

    private void DrawPrivacyTab()
    {
        ImGui.TextUnformatted("Who can receive your listening presence?");
        ImGui.TextWrapped("Private still retrieves your music for your local card, but publishes no social presence.");
        ImGui.Spacing();

        var visibility = this.configuration.Visibility;
        if (ImGui.RadioButton("Private", visibility == VisibilityMode.Private))
        {
            this.configuration.Visibility = VisibilityMode.Private;
            this.saveConfiguration();
            this.requestSync();
        }
        ImGui.TextDisabled("Only you see your card.");

        if (ImGui.RadioButton("Public", visibility == VisibilityMode.Public))
        {
            this.configuration.Visibility = VisibilityMode.Public;
            this.saveConfiguration();
            this.requestSync();
        }
        ImGui.TextDisabled("Nearby XIV.fm users in the same game location may receive your presence.");

        ImGui.BeginDisabled();
        ImGui.RadioButton("Custom Relays", visibility == VisibilityMode.Custom);
        ImGui.EndDisabled();
        ImGui.TextDisabled("Relay selection is the next Phase 6 settings slice.");
    }

    private void DrawRelaysTab()
    {
        ImGui.TextUnformatted("Custom Relays");
        if (!this.hasInstallationCredential())
        {
            ImGui.TextWrapped("Connect Last.fm first. Relays are invitation-based audiences tied to your linked XIV.fm account.");
            return;
        }

        ImGui.TextWrapped("The Relay server behavior is complete. Creation, invitations, membership, and audience selection are being connected to this settings page during Phase 6.");
        ImGui.Spacing();
        ImGui.TextDisabled("Until this page is enabled, keep visibility Private or Public.");
    }

    private void DrawDiagnosticsTab()
    {
        var duty = this.dutyPolicy();
        var link = this.linkState();
        var sync = this.syncState();
        var snapshot = this.overlaySnapshot();
        var render = this.renderDiagnostics();

        ImGui.TextUnformatted("Runtime");
        ImGui.BulletText($"Duty: {(duty.IsInDuty ? "bound — participation suspended" : "not bound")}");
        ImGui.BulletText($"Link: {link.Status}");
        ImGui.BulletText($"Sync: {sync.Status}");
        ImGui.BulletText($"Visibility: {this.configuration.Visibility}");
        ImGui.BulletText($"Snapshot cards: {snapshot.Cards.Length}");
        ImGui.BulletText($"Location: {snapshot.Location?.ToString() ?? "unavailable"}");
        ImGui.BulletText($"Render requested/matched/in-range/projected/drawn: {render.RequestedCards}/{render.MatchedPlayers}/{render.InRangePlayers}/{render.ProjectedAnchors}/{render.RenderedCards}");
        if (!string.IsNullOrWhiteSpace(sync.Error))
            ImGui.TextWrapped($"Sync error: {sync.Error}");
        if (!string.IsNullOrWhiteSpace(link.Error))
            ImGui.TextWrapped($"Link error: {link.Error}");

        ImGui.Separator();
        if (ImGui.CollapsingHeader("Development server"))
        {
            ImGui.TextWrapped("For private testing only. HTTP is accepted only for loopback addresses; production servers require HTTPS.");
            var enabled = this.configuration.DeveloperServerEnabled;
            if (ImGui.Checkbox("Use development server", ref enabled))
            {
                this.cancelAccountLink();
                this.configuration.DeveloperServerEnabled = enabled;
                this.saveConfiguration();
                this.requestSync();
            }

            var baseUrl = this.configuration.DeveloperServerBaseUrl;
            if (ImGui.InputText("Server URL", ref baseUrl, 512))
            {
                this.cancelAccountLink();
                this.configuration.DeveloperServerBaseUrl = baseUrl.Trim();
                this.saveConfiguration();
            }

            var mocks = this.configuration.DeveloperMockRemoteCards;
            if (ImGui.Checkbox("Show remote mock cards", ref mocks))
            {
                this.configuration.DeveloperMockRemoteCards = mocks;
                this.saveConfiguration();
            }
        }
    }

    private static void DrawStatusBadge(string text, Vector4 color) => ImGui.TextColored(color, $"● {text}");
}
