using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Overlay;
using XIV.fm.Plugin.Core.Policy;
using XIV.fm.Plugin.Core.Presence;
using XIV.fm.Plugin.Core.Sync;
using XIV.fm.Plugin.Network;

namespace XIV.fm.Plugin.UI;

public sealed class SettingsWindow : Window
{
    private static readonly Vector4 Accent = new(0.88f, 0.23f, 0.36f, 1f);
    private static readonly Vector4 Success = new(0.32f, 0.78f, 0.48f, 1f);
    private static readonly Vector4 Warning = new(0.95f, 0.71f, 0.25f, 1f);
    private static readonly Vector4 Danger = new(0.95f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 Neutral = new(0.62f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 PanelSurface = new(0.169f, 0.169f, 0.169f, 0.82f);
    private static readonly Vector4 PanelSurfaceHovered = new(0.215f, 0.215f, 0.215f, 0.9f);
    private static readonly Vector4 PanelBorder = new(0.42f, 0.42f, 0.44f, 0.5f);

    private readonly PluginConfiguration configuration;
    private readonly Action saveConfiguration;
    private readonly Func<string?> startAccountLink;
    private readonly Action cancelAccountLink;
    private readonly Func<string?> disconnectAccount;
    private readonly Action openLastFm;
    private readonly Func<Uri, string?> openAuthorizationLink;
    private readonly Action requestSync;
    private readonly Func<bool> hasInstallationCredential;
    private readonly Func<DutyParticipationPolicy> dutyPolicy;
    private readonly Func<AccountLinkRuntimeState> linkState;
    private readonly Func<AccountDisconnectRuntimeState> disconnectState;
    private readonly Func<SyncRuntimeState> syncState;
    private readonly Func<OverlaySnapshot> overlaySnapshot;
    private readonly Func<OverlayRenderDiagnostics> renderDiagnostics;
    private readonly RelayCoordinator relayCoordinator;
    private string? interactionMessage;
    private string newRelayName = string.Empty;
    private string invitationToken = string.Empty;
    private string renameRelayName = string.Empty;
    private Guid? renameRelayId;
    private Guid? confirmingLeaveRelayId;
    private Guid? confirmingDeleteRelayId;
    private Guid? confirmingKickMembershipId;
    private Guid? expandedRelayId;
    private Guid? pendingManagementRelayId;
    private bool confirmingDisconnect;

    public SettingsWindow(
        PluginConfiguration configuration,
        Action saveConfiguration,
        Func<string?> startAccountLink,
        Action cancelAccountLink,
        Func<string?> disconnectAccount,
        Action openLastFm,
        Func<Uri, string?> openAuthorizationLink,
        Action requestSync,
        Func<bool> hasInstallationCredential,
        Func<DutyParticipationPolicy> dutyPolicy,
        Func<AccountLinkRuntimeState> linkState,
        Func<AccountDisconnectRuntimeState> disconnectState,
        Func<SyncRuntimeState> syncState,
        Func<OverlaySnapshot> overlaySnapshot,
        Func<OverlayRenderDiagnostics> renderDiagnostics,
        RelayCoordinator relayCoordinator)
        : base("XIV.fm###XIV.fm.Settings")
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.startAccountLink = startAccountLink;
        this.cancelAccountLink = cancelAccountLink;
        this.disconnectAccount = disconnectAccount;
        this.openLastFm = openLastFm;
        this.openAuthorizationLink = openAuthorizationLink;
        this.requestSync = requestSync;
        this.hasInstallationCredential = hasInstallationCredential;
        this.dutyPolicy = dutyPolicy;
        this.linkState = linkState;
        this.disconnectState = disconnectState;
        this.syncState = syncState;
        this.overlaySnapshot = overlaySnapshot;
        this.renderDiagnostics = renderDiagnostics;
        this.relayCoordinator = relayCoordinator;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(434f, 350f),
            MaximumSize = new Vector2(672f, 602f),
        };
    }

    public bool DiagnosticsVisible { get; set; }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 8f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 6f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 5f * scale);
        try
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

            if (ImGui.BeginTabItem("Relays"))
            {
                this.DrawRelaysTab();
                ImGui.EndTabItem();
            }

            if (this.DiagnosticsVisible && ImGui.BeginTabItem("Diagnostics"))
            {
                this.DrawDiagnosticsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
        finally
        {
            ImGui.PopStyleVar(4);
        }
    }

    private static void DrawHeader()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var height = 58f * scale;
        var origin = ImGui.GetCursorScreenPos();
        var maximum = origin + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();

        drawList.AddRectFilled(origin, maximum, ImGui.GetColorU32(PanelSurface), 7f * scale);
        drawList.AddRect(origin, maximum, ImGui.GetColorU32(PanelBorder), 7f * scale);
        drawList.AddRectFilled(
            origin,
            new Vector2(origin.X + (3f * scale), maximum.Y),
            ImGui.GetColorU32(Accent),
            7f * scale);

        var textOrigin = origin + new Vector2(14f, 10f) * scale;
        drawList.AddText(textOrigin, ImGui.GetColorU32(Accent), "XIV.fm");
        drawList.AddText(
            textOrigin + new Vector2(0f, 24f * scale),
            ImGui.GetColorU32(style.Colors[(int)ImGuiCol.TextDisabled]),
            "Your soundtrack, above your nameplate.");
        ImGui.SetCursorScreenPos(new Vector2(origin.X, maximum.Y + (4f * scale)));
    }

    private void DrawAccountTab()
    {
        var duty = this.dutyPolicy();
        var state = this.linkState();
        var linked = this.hasInstallationCredential();

        DrawSectionHeader(
            "Last.fm account",
            "Connect securely in your browser. XIV.fm never sees or stores your Last.fm password.");

        if (linked)
        {
            var disconnect = this.disconnectState();
            var accountName = string.IsNullOrWhiteSpace(this.configuration.LinkedLastFmAccountName)
                ? "your linked Last.fm account"
                : this.configuration.LinkedLastFmAccountName;
            DrawStatusPanel(
                "Connected",
                $"Listening as {accountName}. Your temporary authorization session was discarded after linking.",
                Success);

            if (disconnect.Status == AccountDisconnectRuntimeStatus.Disconnecting)
            {
                DrawStatusPanel(
                    "Disconnecting",
                    "Removing this XIV.fm link…",
                    Warning);
                return;
            }

            const string profileLabel = "Open Last.fm profile";
            const string syncLabel = "Sync now";
            const string disconnectLabel = "Disconnect Last.fm";
            var actionSpacing = ImGui.GetStyle().ItemSpacing.X;
            var inlineActions = this.confirmingDisconnect || ImGui.GetContentRegionAvail().X >=
                GetButtonWidth(profileLabel) + GetButtonWidth(syncLabel) + GetButtonWidth(disconnectLabel) +
                (actionSpacing * 2f);

            if (DrawPrimaryButton(profileLabel))
                this.openLastFm();
            ImGui.SameLine();
            if (DrawSecondaryButton(syncLabel))
                this.requestSync();

            if (!this.confirmingDisconnect)
            {
                if (inlineActions)
                    ImGui.SameLine();
                else
                    ImGui.Spacing();
                AlignNextButtonRight(disconnectLabel);
                if (DrawOutlinedDangerButton(disconnectLabel))
                    this.confirmingDisconnect = true;
            }

            ImGui.Spacing();
            if (disconnect.Status == AccountDisconnectRuntimeStatus.Failed)
            {
                DrawStatusPanel(
                    "Couldn’t disconnect",
                    "XIV.fm couldn’t remove the link. You can try again.",
                    Danger);
            }

            if (!this.confirmingDisconnect)
                return;

            DrawStatusPanel(
                "Disconnect Last.fm?",
                "This removes the XIV.fm link from this device. Your Last.fm account and listening history won’t be changed.",
                Warning);

            if (!duty.AllowsServerRequests)
                ImGui.BeginDisabled();
            if (DrawDangerButton("Disconnect"))
            {
                var error = this.disconnectAccount();
                this.interactionMessage = error;
                if (error is null)
                    this.confirmingDisconnect = false;
            }

            if (!duty.AllowsServerRequests)
                ImGui.EndDisabled();
            ImGui.SameLine();
            if (DrawSecondaryButton("Cancel"))
                this.confirmingDisconnect = false;

            if (!duty.AllowsServerRequests)
                ImGui.TextDisabled("You can disconnect after leaving the duty.");
            if (!string.IsNullOrWhiteSpace(this.interactionMessage))
                ImGui.TextWrapped(this.interactionMessage);
            return;
        }

        switch (state.Status)
        {
            case AccountLinkRuntimeStatus.Starting:
                DrawStatusPanel(
                    "Preparing a secure link",
                    "Creating a short-lived browser authorization session…",
                    Warning);
                break;
            case AccountLinkRuntimeStatus.WaitingForBrowser:
                DrawStatusPanel(
                    "Waiting for Last.fm",
                    state.Error ?? "Approve XIV.fm on Last.fm. If your browser did not open automatically, use the buttons below.",
                    Warning);
                this.DrawAuthorizationActions(state);
                if (DrawSecondaryButton("Cancel and start over"))
                {
                    this.cancelAccountLink();
                    this.interactionMessage = "The pending link was cleared.";
                }

                break;
            case AccountLinkRuntimeStatus.SuspendedDuty:
                DrawStatusPanel(
                    "Paused while in duty",
                    "Leave the duty before linking. XIV.fm makes no server requests while you are duty-bound.",
                    Warning);
                break;
            case AccountLinkRuntimeStatus.Failed:
                DrawStatusPanel(
                    "Couldn’t connect",
                    state.Error ?? "Account linking failed. You can try again.",
                    Danger);
                if (this.configuration.PendingLinkSessionId is not null)
                {
                    ImGui.TextDisabled("XIV.fm will keep retrying while this link session remains valid.");
                    this.DrawAuthorizationActions(state);
                    if (DrawSecondaryButton("Cancel and start over"))
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
                DrawStatusPanel(
                    "Not connected",
                    "Connect Last.fm to show your real listening state above your character.",
                    Neutral);
                this.DrawLinkButton(duty);
                break;
        }

        if (!string.IsNullOrWhiteSpace(this.interactionMessage))
        {
            ImGui.Spacing();
            DrawStatusPanel("Status", this.interactionMessage, Neutral);
        }
    }

    private void DrawLinkButton(DutyParticipationPolicy duty)
    {
        if (!duty.AllowsServerRequests)
            ImGui.BeginDisabled();

        if (DrawPrimaryButton("Connect Last.fm in browser", new Vector2(240f * ImGuiHelpers.GlobalScale, 0f)))
        {
            var error = this.startAccountLink();
            this.interactionMessage = error ?? "Creating a Last.fm connection link…";
        }

        if (!duty.AllowsServerRequests)
            ImGui.EndDisabled();

        if (!duty.AllowsServerRequests)
            ImGui.TextDisabled("Unavailable while duty-bound.");
    }

    private void DrawAuthorizationActions(AccountLinkRuntimeState state)
    {
        var authorizationUri = state.AuthorizationUri;
        if (authorizationUri is null)
        {
            ImGui.TextDisabled("The connection link is unavailable. Cancel and start over to create a new one.");
            ImGui.Spacing();
            return;
        }

        const string openLabel = "Open Last.fm authorization";
        const string copyLabel = "Copy connection link";
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var actionsFitInline = ImGui.GetContentRegionAvail().X >=
            GetButtonWidth(openLabel) + GetButtonWidth(copyLabel) + spacing;

        if (DrawPrimaryButton(openLabel))
        {
            this.interactionMessage = this.openAuthorizationLink(authorizationUri) ??
                "Last.fm authorization was opened in your default browser.";
        }

        if (actionsFitInline)
            ImGui.SameLine();
        else
            ImGui.Spacing();

        if (DrawSecondaryButton(copyLabel))
        {
            ImGui.SetClipboardText(authorizationUri.AbsoluteUri);
            this.interactionMessage = "Connection link copied to the clipboard.";
        }

        ImGui.Spacing();
    }

    private void DrawOverlayTab()
    {
        DrawSectionHeader(
            "Listening cards",
            "Choose when cards appear and how close another listener must be before their card is shown.");

        var changed = false;
        var cards = this.configuration.ShowPlaceholderCards;
        if (ImGui.Checkbox("Show listening cards", ref cards))
        {
            this.configuration.ShowPlaceholderCards = cards;
            changed = true;
        }

        ImGui.SameLine();
        ImGui.TextDisabled(cards ? "Visible" : "Hidden");

        if (!cards)
            ImGui.BeginDisabled();
        ImGui.SameLine();
        var ownCard = this.configuration.ShowOwnListeningCard;
        if (ImGui.Checkbox("Show my card", ref ownCard))
        {
            this.configuration.ShowOwnListeningCard = ownCard;
            changed = true;
        }

        if (!cards)
            ImGui.EndDisabled();

        ImGui.TextDisabled(cards && !ownCard
            ? "Nearby listening cards remain visible while your own card is hidden."
            : "Card visibility does not change your privacy setting.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (!cards)
            ImGui.BeginDisabled();

        ImGui.TextUnformatted("Card background opacity");
        ImGui.TextDisabled("Adjust the card surface without fading its text or artwork.");
        ImGui.Spacing();

        var opacity = this.configuration.NormalizedCardOpacityPercent;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderInt(
                "##XIV.fm.CardOpacity",
                ref opacity,
                CardAppearance.MinimumOpacityPercent,
                CardAppearance.MaximumOpacityPercent,
                "%d%%"))
        {
            this.configuration.CardOpacityPercent = CardAppearance.NormalizeOpacityPercent(opacity);
            changed = true;
        }

        if (opacity != CardAppearance.DefaultOpacityPercent)
        {
            if (DrawSecondaryButton("Reset to 60%"))
            {
                this.configuration.CardOpacityPercent = CardAppearance.DefaultOpacityPercent;
                changed = true;
            }
        }
        else
        {
            ImGui.TextDisabled("60% · default");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Card size");
        ImGui.TextDisabled("Scale your card and other players’ cards independently.");
        ImGui.Spacing();

        ImGui.TextUnformatted("My card");
        var ownCardSize = this.configuration.NormalizedOwnCardSizePercent;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderInt(
                "##XIV.fm.OwnCardSize",
                ref ownCardSize,
                CardAppearance.MinimumSizePercent,
                CardAppearance.MaximumSizePercent,
                "%d%%"))
        {
            this.configuration.OwnCardSizePercent = CardAppearance.NormalizeSizePercent(ownCardSize);
            changed = true;
        }

        ImGui.TextUnformatted("Other players’ cards");
        ImGui.TextDisabled("Cards shrink smoothly as players approach the distance limit.");
        var otherCardSize = this.configuration.NormalizedOtherCardSizePercent;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderInt(
                "##XIV.fm.OtherCardSize",
                ref otherCardSize,
                CardAppearance.MinimumSizePercent,
                CardAppearance.MaximumSizePercent,
                "%d%%"))
        {
            this.configuration.OtherCardSizePercent = CardAppearance.NormalizeSizePercent(otherCardSize);
            changed = true;
        }

        if (ownCardSize != CardAppearance.DefaultSizePercent ||
            otherCardSize != CardAppearance.DefaultSizePercent)
        {
            if (DrawSecondaryButton("Reset sizes to 100%"))
            {
                this.configuration.OwnCardSizePercent = CardAppearance.DefaultSizePercent;
                this.configuration.OtherCardSizePercent = CardAppearance.DefaultSizePercent;
                changed = true;
            }
        }
        else
        {
            ImGui.TextDisabled("100% · default");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Nearby listener distance");
        ImGui.TextDisabled("Only cards belonging to other players are filtered by distance.");
        ImGui.Spacing();

        var range = this.configuration.NormalizedRemoteCardDistanceYalms;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderInt(
                "##XIV.fm.RemoteCardDistance",
                ref range,
                OverlayVisibility.MinimumRemoteDistanceYalms,
                OverlayVisibility.MaximumRemoteDistanceYalms,
                "%d yalms"))
        {
            this.configuration.RemoteCardDistanceYalms = OverlayVisibility.NormalizeRemoteDistance(range);
            changed = true;
        }

        if (range != OverlayVisibility.DefaultRemoteDistanceYalms)
        {
            if (DrawSecondaryButton("Reset to 8 yalms"))
            {
                this.configuration.RemoteCardDistanceYalms = OverlayVisibility.DefaultRemoteDistanceYalms;
                changed = true;
            }
        }
        else
        {
            ImGui.TextDisabled("8 yalms · recommended");
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Hold Alt and click a listening card to open its track on Last.fm.");

        if (!cards)
            ImGui.EndDisabled();

        if (changed)
            this.saveConfiguration();
    }

    private void DrawPrivacyTab()
    {
        DrawSectionHeader(
            "Who can receive your listening presence?",
            "Private still retrieves your music for your own card, but publishes no social presence.");

        var visibility = this.configuration.Visibility;
        if (DrawChoiceCard(
                "XIV.fm.Visibility.Private",
                "Private",
                "Only you can see your listening card.",
                visibility == VisibilityMode.Private,
                true))
        {
            this.SetVisibility(VisibilityMode.Private);
        }

        if (DrawChoiceCard(
                "XIV.fm.Visibility.Public",
                "Public",
                "Nearby XIV.fm listeners in your current game location can receive your presence.",
                visibility == VisibilityMode.Public,
                true))
        {
            this.SetVisibility(VisibilityMode.Public);
        }

        var relayState = this.relayCoordinator.State;
        var selectedRelayCount = this.configuration.SelectedRelayIds.Count;
        var hasLinkedAccount = this.hasInstallationCredential();
        var customEnabled = hasLinkedAccount && selectedRelayCount > 0;
        if (DrawChoiceCard(
                "XIV.fm.Visibility.Custom",
                "Custom Relays",
                customEnabled
                    ? $"Share and receive presence through {selectedRelayCount} selected Relay{(selectedRelayCount == 1 ? string.Empty : "s")}."
                    : hasLinkedAccount
                        ? "Select at least one joined Relay below."
                        : "Connect Last.fm to choose a private Relay audience.",
                visibility == VisibilityMode.Custom,
                customEnabled))
        {
            this.SetVisibility(VisibilityMode.Custom);
        }

        if (hasLinkedAccount)
        {
            ImGui.TextUnformatted("Relay audience");
            ImGui.TextDisabled("Choose up to five Relays to share and receive presence.");
            if (relayState.Relays.IsEmpty)
            {
                ImGui.TextDisabled(relayState.Status is RelayRuntimeStatus.Idle or RelayRuntimeStatus.Loading
                    ? "Loading your joined Relays…"
                    : "Create or join a Relay on the Relays tab first.");
            }
            else
            {
                foreach (var relay in relayState.Relays)
                {
                    ImGui.PushID($"PrivacyRelay.{relay.RelayId:D}");
                    var selected = this.configuration.SelectedRelayIds.Contains(relay.RelayId);
                    var canSelect = selected || RelaySelection.CanSelect(
                        this.configuration.SelectedRelayIds,
                        relay.RelayId);
                    if (!canSelect)
                        ImGui.BeginDisabled();
                    if (ImGui.Checkbox(relay.Name, ref selected))
                        this.SetRelaySelected(relay.RelayId, selected);
                    if (!canSelect)
                        ImGui.EndDisabled();
                    ImGui.SameLine();
                    ImGui.TextDisabled(relay.IsOwner ? "Owner" : "Member");
                    ImGui.PopID();
                }

                if (selectedRelayCount >= RelaySelection.MaximumSelectedRelays)
                    ImGui.TextDisabled("Five Relays selected · deselect one to choose another.");
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Changing this setting requests an immediate sync.");
    }

    private void DrawRelaysTab()
    {
        DrawSectionHeader(
            "Custom Relays",
            "Create, join, and manage private invitation-based groups.");

        if (!this.hasInstallationCredential())
        {
            DrawStatusPanel(
                "Connect Last.fm first",
                "Relays are tied securely to your linked XIV.fm account.",
                Neutral);
            return;
        }

        var duty = this.dutyPolicy();
        var state = this.relayCoordinator.State;
        var requestBusy = state.Status is RelayRuntimeStatus.Loading or RelayRuntimeStatus.Working;
        var busy = requestBusy || !string.IsNullOrWhiteSpace(state.CreatedInvitationToken);
        if (state.Status == RelayRuntimeStatus.SuspendedDuty || !duty.AllowsServerRequests)
        {
            DrawStatusPanel(
                "Paused while in duty",
                "Relay information remains visible, but XIV.fm makes no Relay requests while you are duty-bound.",
                Warning);
        }
        else if (requestBusy)
        {
            DrawStatusPanel("Working", state.Message ?? "Updating Custom Relays…", Warning);
        }
        else if (!string.IsNullOrWhiteSpace(state.Error))
        {
            DrawStatusPanel("Relay action failed", state.Error, Danger);
        }
        else if (!string.IsNullOrWhiteSpace(state.Message))
        {
            DrawStatusPanel("Custom Relays", state.Message, Success);
        }

        if (!duty.AllowsServerRequests || busy)
            ImGui.BeginDisabled();
        if (DrawSecondaryButton("Refresh"))
            this.RunRelayAction(this.relayCoordinator.TryRefresh);
        if (!duty.AllowsServerRequests || busy)
            ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("Join with an invitation");
        ImGui.TextDisabled("Invitation tokens are secret, expiring, and single-use.");
        const string joinRelayLabel = "Join Relay";
        ImGui.SetNextItemWidth(GetInlineInputWidth(joinRelayLabel));
        ImGui.InputText(
            "##XIV.fm.RelayInvitationToken",
            ref this.invitationToken,
            512,
            ImGuiInputTextFlags.Password);
        ImGui.SameLine();
        if (!duty.AllowsServerRequests || busy)
            ImGui.BeginDisabled();
        if (DrawPrimaryButton(joinRelayLabel))
        {
            if (this.relayCoordinator.TryAcceptInvitation(this.invitationToken, out var error))
            {
                this.invitationToken = string.Empty;
                this.interactionMessage = null;
            }
            else
            {
                this.interactionMessage = error;
            }
        }

        if (!duty.AllowsServerRequests || busy)
            ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("Create a Relay");
        ImGui.TextDisabled("Names are 3–48 characters. You can own up to three active Relays.");
        const string createRelayLabel = "Create Relay";
        ImGui.SetNextItemWidth(GetInlineInputWidth(createRelayLabel));
        ImGui.InputText("##XIV.fm.NewRelayName", ref this.newRelayName, 96);
        ImGui.SameLine();
        if (!duty.AllowsServerRequests || busy)
            ImGui.BeginDisabled();
        if (DrawPrimaryButton(createRelayLabel))
        {
            if (this.relayCoordinator.TryCreate(this.newRelayName, out var error))
            {
                this.newRelayName = string.Empty;
                this.interactionMessage = null;
            }
            else
            {
                this.interactionMessage = error;
            }
        }

        if (!duty.AllowsServerRequests || busy)
            ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("Your Relays");
        if (state.Relays.IsEmpty)
        {
            ImGui.TextDisabled(state.Status == RelayRuntimeStatus.Idle
                ? "Refresh to load your joined Relays."
                : "You haven’t joined any Relays yet.");
        }
        else
        {
            this.DrawRelayTable(state, duty, busy);
        }

        if (!string.IsNullOrWhiteSpace(this.interactionMessage))
        {
            ImGui.Spacing();
            DrawStatusPanel("Status", this.interactionMessage, Neutral);
        }
    }

    private void DrawRelayTable(
        RelayRuntimeState state,
        DutyParticipationPolicy duty,
        bool busy)
    {
        var tableFlags = ImGuiTableFlags.BordersOuter |
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("XIV.fm.Relays", 3, tableFlags))
        {
            ImGui.TableSetupColumn("Group", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Access", ImGuiTableColumnFlags.WidthFixed, 72f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Members", ImGuiTableColumnFlags.WidthFixed, 72f * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();

            foreach (var relay in state.Relays)
            {
                ImGui.PushID(relay.RelayId.ToString("D"));
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                var expanded = this.expandedRelayId == relay.RelayId;
                if (ImGui.ArrowButton("##Details", expanded ? ImGuiDir.Down : ImGuiDir.Right))
                {
                    if (expanded)
                    {
                        this.expandedRelayId = null;
                        this.pendingManagementRelayId = null;
                    }
                    else
                    {
                        this.expandedRelayId = relay.RelayId;
                        this.confirmingLeaveRelayId = null;
                        this.confirmingDeleteRelayId = null;
                        this.confirmingKickMembershipId = null;
                        if (relay.IsOwner && state.ManagedRelayId != relay.RelayId)
                        {
                            this.renameRelayId = relay.RelayId;
                            this.renameRelayName = relay.Name;
                            this.pendingManagementRelayId = relay.RelayId;
                        }
                    }
                }

                ImGui.SameLine();
                ImGui.TextUnformatted(relay.Name);
                ImGui.TableSetColumnIndex(1);
                if (relay.IsOwner)
                    ImGui.TextColored(Accent, "Owner");
                else
                    ImGui.TextUnformatted("Member");
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(relay.MemberCount.ToString(CultureInfo.InvariantCulture));
                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (this.expandedRelayId is not Guid expandedRelayId)
            return;

        var expandedRelay = state.Relays.FirstOrDefault(relay => relay.RelayId == expandedRelayId);
        if (expandedRelay is null)
        {
            this.expandedRelayId = null;
            this.pendingManagementRelayId = null;
            return;
        }

        if (expandedRelay.IsOwner &&
            state.ManagedRelayId != expandedRelay.RelayId &&
            this.pendingManagementRelayId == expandedRelay.RelayId &&
            duty.AllowsServerRequests &&
            !busy)
        {
            this.pendingManagementRelayId = null;
            this.RunRelayAction((out string? error) =>
                this.relayCoordinator.TryLoadManagement(expandedRelay.RelayId, out error));
        }
        else if (state.ManagedRelayId == expandedRelay.RelayId)
        {
            this.pendingManagementRelayId = null;
        }

        this.DrawRelayDetails(expandedRelay, state, duty, busy);
    }

    private void DrawRelayDetails(
        RelayResponse relay,
        RelayRuntimeState state,
        DutyParticipationPolicy duty,
        bool busy)
    {
        ImGui.PushID($"Details.{relay.RelayId:D}");
        try
        {
            ImGui.Spacing();
            DrawStatusPanel(
                relay.Name,
                relay.IsOwner
                    ? "You own this Relay and can manage its name, invitations, members, and deletion."
                    : "You are a member of this Relay and can receive its selected listening presence.",
                relay.IsOwner ? Accent : Neutral);
            DrawKeyValue("Access", relay.IsOwner ? "Owner" : "Member");
            DrawKeyValue("Members", relay.MemberCount.ToString(CultureInfo.InvariantCulture));
            DrawKeyValue("Created", FormatTimestamp(relay.CreatedAt));
            DrawKeyValue("Last updated", FormatTimestamp(relay.UpdatedAt));

            if (relay.IsOwner)
            {
                if (state.ManagedRelayId == relay.RelayId)
                {
                    this.DrawRelayManagement(relay, state, duty, busy);
                }
                else if (busy)
                {
                    ImGui.TextDisabled("Loading owner controls…");
                }
                else if (!duty.AllowsServerRequests)
                {
                    ImGui.TextDisabled("Owner controls will load automatically after you leave the duty.");
                }
                else if (!string.IsNullOrWhiteSpace(state.Error))
                {
                    ImGui.TextDisabled("Owner controls could not be loaded. Close and reopen this group to retry.");
                }
            }
            else
            {
                this.DrawRelayMemberActions(relay, duty, busy);
            }
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private void DrawRelayMemberActions(
        RelayResponse relay,
        DutyParticipationPolicy duty,
        bool busy)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Membership options");
        if (this.confirmingLeaveRelayId != relay.RelayId)
        {
            if (!duty.AllowsServerRequests || busy)
                ImGui.BeginDisabled();
            if (DrawOutlinedDangerButton("Leave Relay"))
                this.confirmingLeaveRelayId = relay.RelayId;
            if (!duty.AllowsServerRequests || busy)
                ImGui.EndDisabled();
            return;
        }

        DrawStatusPanel(
            "Leave this Relay?",
            "You’ll stop receiving its presence immediately and will need a new invitation to return.",
            Warning);
        if (!duty.AllowsServerRequests || busy)
            ImGui.BeginDisabled();
        if (DrawDangerButton("Leave"))
        {
            if (this.relayCoordinator.TryLeave(relay.RelayId, out var error))
            {
                this.confirmingLeaveRelayId = null;
                this.expandedRelayId = null;
            }
            else
            {
                this.interactionMessage = error;
            }
        }

        if (!duty.AllowsServerRequests || busy)
            ImGui.EndDisabled();
        ImGui.SameLine();
        if (DrawSecondaryButton("Cancel"))
            this.confirmingLeaveRelayId = null;
    }

    private void DrawRelayManagement(
        RelayResponse relay,
        RelayRuntimeState state,
        DutyParticipationPolicy duty,
        bool busy)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Owner controls");

        if (this.renameRelayId != relay.RelayId)
        {
            this.renameRelayId = relay.RelayId;
            this.renameRelayName = relay.Name;
        }

        const string renameRelayLabel = "Rename";
        ImGui.SetNextItemWidth(GetInlineInputWidth(renameRelayLabel));
        ImGui.InputText("##RenameRelay", ref this.renameRelayName, 96);
        ImGui.SameLine();
        if (!duty.AllowsServerRequests || busy)
            ImGui.BeginDisabled();
        if (DrawSecondaryButton(renameRelayLabel))
            this.RunRelayAction((out string? error) => this.relayCoordinator.TryRename(relay.RelayId, this.renameRelayName, out error));
        if (!duty.AllowsServerRequests || busy)
            ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.TextUnformatted("Invitations");
        ImGui.TextDisabled("Each invitation expires within seven days and can be used once.");
        if (!duty.AllowsServerRequests || busy)
            ImGui.BeginDisabled();
        if (DrawPrimaryButton("Create invitation"))
            this.RunRelayAction((out string? error) => this.relayCoordinator.TryCreateInvitation(relay.RelayId, out error));
        if (!duty.AllowsServerRequests || busy)
            ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(state.CreatedInvitationToken))
        {
            DrawStatusPanel(
                "Copy this invitation now",
                "The secret is shown only once. Send it privately to one person.",
                Warning);
            var secret = state.CreatedInvitationToken;
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##CreatedInvitationToken", ref secret, 512, ImGuiInputTextFlags.ReadOnly);
            if (DrawPrimaryButton("Copy invitation"))
            {
                ImGui.SetClipboardText(secret);
                this.interactionMessage = "Invitation copied to the clipboard.";
            }

            ImGui.SameLine();
            if (DrawSecondaryButton("I’ve saved it"))
                this.relayCoordinator.ClearInvitationSecret();
            return;
        }

        if (state.Invitations.IsEmpty)
        {
            ImGui.TextDisabled("No invitations have been created yet.");
        }
        else if (ImGui.BeginTable(
                     "RelayInvitations",
                     3,
                     ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 64f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Expires", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Option", ImGuiTableColumnFlags.WidthFixed, 78f * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();
            foreach (var invitation in state.Invitations)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(invitation.AcceptedAt is not null ? "Used" : "Active");
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(FormatTimestamp(invitation.ExpiresAt));
                ImGui.TableSetColumnIndex(2);
                if (invitation.AcceptedAt is null)
                {
                    if (!duty.AllowsServerRequests || busy)
                        ImGui.BeginDisabled();
                    if (DrawOutlinedDangerButton($"Revoke##{invitation.InvitationId:D}"))
                    {
                        this.RunRelayAction((out string? error) => this.relayCoordinator.TryRevokeInvitation(
                            relay.RelayId,
                            invitation.InvitationId,
                            out error));
                    }

                    if (!duty.AllowsServerRequests || busy)
                        ImGui.EndDisabled();
                }
                else
                {
                    ImGui.TextDisabled("—");
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Members");
        if (ImGui.BeginTable(
                "RelayMembers",
                3,
                ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Account", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Joined", ImGuiTableColumnFlags.WidthFixed, 112f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Option", ImGuiTableColumnFlags.WidthFixed, 152f * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();
            foreach (var member in state.Members)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(member.LastFmAccountName);
                if (member.IsOwner)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Accent, "Owner");
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(FormatTimestamp(member.JoinedAt));
                ImGui.TableSetColumnIndex(2);
                if (member.IsOwner)
                {
                    ImGui.TextDisabled("—");
                    continue;
                }

                if (this.confirmingKickMembershipId == member.MembershipId)
                {
                    if (!duty.AllowsServerRequests || busy)
                        ImGui.BeginDisabled();
                    if (DrawDangerButton($"Confirm##{member.MembershipId:D}"))
                    {
                        if (this.relayCoordinator.TryKickMember(relay.RelayId, member.MembershipId, out var error))
                            this.confirmingKickMembershipId = null;
                        else
                            this.interactionMessage = error;
                    }

                    if (!duty.AllowsServerRequests || busy)
                        ImGui.EndDisabled();
                    ImGui.SameLine();
                    if (DrawSecondaryButton($"Cancel##{member.MembershipId:D}"))
                        this.confirmingKickMembershipId = null;
                }
                else
                {
                    if (!duty.AllowsServerRequests || busy)
                        ImGui.BeginDisabled();
                    if (DrawOutlinedDangerButton($"Remove##{member.MembershipId:D}"))
                        this.confirmingKickMembershipId = member.MembershipId;
                    if (!duty.AllowsServerRequests || busy)
                        ImGui.EndDisabled();
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (this.confirmingDeleteRelayId != relay.RelayId)
        {
            if (DrawOutlinedDangerButton("Delete Relay"))
                this.confirmingDeleteRelayId = relay.RelayId;
        }
        else
        {
            DrawStatusPanel(
                "Delete this Relay?",
                "This immediately removes every membership, invitation, and active Relay publication. It cannot be undone.",
                Danger);
            if (!duty.AllowsServerRequests || busy)
                ImGui.BeginDisabled();
            if (DrawDangerButton("Delete permanently"))
            {
                if (this.relayCoordinator.TryDelete(relay.RelayId, out var error))
                    this.confirmingDeleteRelayId = null;
                else
                    this.interactionMessage = error;
            }

            if (!duty.AllowsServerRequests || busy)
                ImGui.EndDisabled();
            ImGui.SameLine();
            if (DrawSecondaryButton("Cancel"))
                this.confirmingDeleteRelayId = null;
        }
    }

    private void SetRelaySelected(Guid relayId, bool selected)
    {
        var relayIds = RelaySelection.Normalize(this.configuration.SelectedRelayIds).ToList();
        if (selected)
        {
            if (!RelaySelection.CanSelect(relayIds, relayId) || relayIds.Contains(relayId))
                return;
            relayIds.Add(relayId);
        }
        else
        {
            relayIds.Remove(relayId);
        }

        this.configuration.SelectedRelayIds = RelaySelection.Normalize(relayIds).ToList();
        this.configuration.Visibility = this.configuration.SelectedRelayIds.Count == 0
            ? VisibilityMode.Private
            : VisibilityMode.Custom;
        this.saveConfiguration();
        this.requestSync();
    }

    private void RunRelayAction(RelayAction action)
    {
        if (!action(out var error))
            this.interactionMessage = error;
        else
            this.interactionMessage = null;
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private delegate bool RelayAction(out string? error);

    private void DrawDiagnosticsTab()
    {
        var duty = this.dutyPolicy();
        var link = this.linkState();
        var sync = this.syncState();
        var snapshot = this.overlaySnapshot();
        var render = this.renderDiagnostics();

        DrawSectionHeader(
            "Runtime diagnostics",
            "Live state for troubleshooting account, sync, matching, and card rendering issues.");

        DrawKeyValue("Duty", duty.IsInDuty ? "Bound · participation paused" : "Not bound");
        DrawKeyValue("Account link", link.Status.ToString());
        DrawKeyValue("Sync", sync.Status.ToString());
        DrawKeyValue("Visibility", this.configuration.Visibility.ToString());
        DrawKeyValue("Card opacity", $"{this.configuration.NormalizedCardOpacityPercent}%");
        DrawKeyValue("My card size", $"{this.configuration.NormalizedOwnCardSizePercent}%");
        DrawKeyValue("Other card size", $"{this.configuration.NormalizedOtherCardSizePercent}%");
        DrawKeyValue("Own card", this.configuration.ShowOwnListeningCard ? "Visible" : "Hidden");
        DrawKeyValue("Location", snapshot.Location?.ToString() ?? "Unavailable");
        DrawKeyValue("Snapshot cards", snapshot.Cards.Length.ToString(CultureInfo.InvariantCulture));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("Render pipeline");
        ImGui.TextDisabled("Requested → matched → in range → projected → drawn");
        DrawKeyValue(
            "Cards",
            $"{render.RequestedCards} → {render.MatchedPlayers} → {render.InRangePlayers} → {render.ProjectedAnchors} → {render.RenderedCards}");
        DrawKeyValue(
            "Local anchor height",
            render.LocalNameplateHeightYalms is float height ? $"{height:F2} yalms" : "Unavailable");

        if (!string.IsNullOrWhiteSpace(sync.Error))
        {
            ImGui.Spacing();
            DrawStatusPanel("Sync error", sync.Error, Danger);
        }

        if (!string.IsNullOrWhiteSpace(link.Error))
        {
            ImGui.Spacing();
            DrawStatusPanel("Link error", link.Error, Danger);
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.CollapsingHeader("Development server"))
            this.DrawDevelopmentServerSettings();
    }

    private void DrawDevelopmentServerSettings()
    {
        ImGui.TextWrapped("Private testing only. Production servers require HTTPS; HTTP is accepted only for loopback addresses.");
        ImGui.Spacing();

        var enabled = this.configuration.DeveloperServerEnabled;
        if (ImGui.Checkbox("Use development server", ref enabled))
        {
            this.cancelAccountLink();
            this.relayCoordinator.Reset();
            this.configuration.DeveloperServerEnabled = enabled;
            this.configuration.Visibility = VisibilityMode.Private;
            this.configuration.SelectedRelayIds.Clear();
            this.saveConfiguration();
            this.requestSync();
        }

        if (enabled)
        {
            ImGui.Spacing();
            DrawStatusPanel(
                "Development mode is active",
                "Account linking and sync use the development server below.",
                Warning);
        }

        if (!enabled)
            ImGui.BeginDisabled();

        var baseUrl = this.configuration.DeveloperServerBaseUrl;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##XIV.fm.DevelopmentServerUrl", ref baseUrl, 512))
        {
            this.cancelAccountLink();
            this.relayCoordinator.Reset();
            this.configuration.DeveloperServerBaseUrl = baseUrl.Trim();
            this.configuration.Visibility = VisibilityMode.Private;
            this.configuration.SelectedRelayIds.Clear();
            this.saveConfiguration();
        }

        ImGui.TextDisabled("Development server URL");

        var mocks = this.configuration.DeveloperMockRemoteCards;
        if (ImGui.Checkbox("Show remote mock cards", ref mocks))
        {
            this.configuration.DeveloperMockRemoteCards = mocks;
            this.saveConfiguration();
        }

        if (!enabled)
            ImGui.EndDisabled();
    }

    private void SetVisibility(VisibilityMode visibility)
    {
        this.configuration.Visibility = visibility;
        this.saveConfiguration();
        this.requestSync();
    }

    private static void DrawSectionHeader(string title, string description)
    {
        ImGui.Spacing();
        ImGui.TextColored(Accent, title);
        ImGui.TextWrapped(description);
        ImGui.Spacing();
    }

    private static void DrawStatusPanel(string title, string body, Vector4 tone)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var padding = 12f * scale;
        var textWidth = MathF.Max(1f, width - (padding * 2f));
        var titleHeight = ImGui.GetTextLineHeight();
        var bodySize = ImGui.CalcTextSize(body, false, textWidth);
        var gap = 5f * scale;
        var height = MathF.Max(72f * scale, (padding * 2f) + titleHeight + gap + bodySize.Y);
        var origin = ImGui.GetCursorScreenPos();
        var maximum = origin + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(origin, maximum, ImGui.GetColorU32(PanelSurface), 6f * scale);
        drawList.AddRect(origin, maximum, ImGui.GetColorU32(PanelBorder), 6f * scale);
        drawList.AddRectFilled(
            origin,
            new Vector2(origin.X + (3f * scale), maximum.Y),
            ImGui.GetColorU32(tone),
            6f * scale);

        ImGui.SetCursorScreenPos(origin + new Vector2(padding, padding));
        ImGui.TextColored(tone, title);
        ImGui.SetCursorScreenPos(origin + new Vector2(padding, padding + titleHeight + gap));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
        ImGui.TextUnformatted(body);
        ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(new Vector2(origin.X, maximum.Y + (8f * scale)));
    }

    private static bool DrawChoiceCard(
        string id,
        string title,
        string description,
        bool selected,
        bool enabled)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var horizontalPadding = 13f * scale;
        var indicatorSpace = 42f * scale;
        var descriptionWidth = MathF.Max(1f, width - horizontalPadding - indicatorSpace);
        var titleHeight = ImGui.GetTextLineHeight();
        var descriptionSize = ImGui.CalcTextSize(description, false, descriptionWidth);
        var textGap = 5f * scale;
        var height = MathF.Max(
            66f * scale,
            (20f * scale) + titleHeight + textGap + descriptionSize.Y);
        var origin = ImGui.GetCursorScreenPos();

        if (!enabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, height));
        var hovered = enabled && ImGui.IsItemHovered();
        if (!enabled)
            ImGui.EndDisabled();

        var style = ImGui.GetStyle();
        var drawList = ImGui.GetWindowDrawList();
        var background = selected
            ? Vector4.Lerp(PanelSurface, WithAlpha(Accent, PanelSurface.W), 0.16f)
            : hovered ? PanelSurfaceHovered : PanelSurface;
        var border = selected
            ? WithAlpha(Accent, 0.82f)
            : PanelBorder;
        var titleColor = enabled
            ? style.Colors[(int)ImGuiCol.Text]
            : style.Colors[(int)ImGuiCol.TextDisabled];
        var descriptionColor = style.Colors[(int)ImGuiCol.TextDisabled];
        var maximum = origin + new Vector2(width, height);

        drawList.AddRectFilled(origin, maximum, ImGui.GetColorU32(background), 6f * scale);
        drawList.AddRect(origin, maximum, ImGui.GetColorU32(border), 6f * scale);
        if (selected)
        {
            drawList.AddRectFilled(
                origin,
                new Vector2(origin.X + (3f * scale), maximum.Y),
                ImGui.GetColorU32(Accent),
                6f * scale);
        }

        var textOrigin = origin + new Vector2(horizontalPadding, 10f * scale);
        drawList.AddText(textOrigin, ImGui.GetColorU32(titleColor), title);
        drawList.AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize(),
            textOrigin + new Vector2(0f, titleHeight + textGap),
            ImGui.GetColorU32(descriptionColor),
            description,
            descriptionWidth);

        var indicatorCenter = new Vector2(maximum.X - (18f * scale), origin.Y + (height / 2f));
        drawList.AddCircle(
            indicatorCenter,
            6f * scale,
            ImGui.GetColorU32(selected ? Accent : border),
            0,
            1.5f * scale);
        if (selected)
            drawList.AddCircleFilled(indicatorCenter, 3f * scale, ImGui.GetColorU32(Accent));

        ImGui.Spacing();
        return enabled && clicked;
    }

    private static void DrawKeyValue(string label, string value)
    {
        var startX = ImGui.GetCursorPosX();
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        ImGui.SetCursorPosX(startX + (170f * ImGuiHelpers.GlobalScale));
        ImGui.TextWrapped(value);
    }

    private static bool DrawPrimaryButton(string label, Vector2 size = default)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var hovered = new Vector4(
            MathF.Min(1f, Accent.X + 0.08f),
            MathF.Min(1f, Accent.Y + 0.08f),
            MathF.Min(1f, Accent.Z + 0.08f),
            Accent.W);
        var active = new Vector4(Accent.X * 0.86f, Accent.Y * 0.86f, Accent.Z * 0.86f, Accent.W);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * scale);
        ImGui.PushStyleColor(ImGuiCol.Button, Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, active);
        try
        {
            return ImGui.Button(label, size);
        }
        finally
        {
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar();
        }
    }

    private static float GetButtonWidth(string label) =>
        ImGui.CalcTextSize(label).X + (ImGui.GetStyle().FramePadding.X * 2f);

    private static float GetInlineInputWidth(string buttonLabel) =>
        MathF.Max(
            1f,
            ImGui.GetContentRegionAvail().X - GetButtonWidth(buttonLabel) - ImGui.GetStyle().ItemSpacing.X);

    private static void AlignNextButtonRight(string label)
    {
        var buttonWidth = GetButtonWidth(label);
        var availableWidth = ImGui.GetContentRegionAvail().X;
        if (availableWidth > buttonWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - buttonWidth);
    }

    private static bool DrawOutlinedDangerButton(string label)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.Text, Danger);
        ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(Danger, 0.78f));
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, WithAlpha(Danger, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, WithAlpha(Danger, 0.22f));
        try
        {
            return ImGui.Button(label);
        }
        finally
        {
            ImGui.PopStyleColor(5);
            ImGui.PopStyleVar();
        }
    }

    private static bool DrawDangerButton(string label)
    {
        var hovered = new Vector4(
            MathF.Min(1f, Danger.X + 0.06f),
            MathF.Min(1f, Danger.Y + 0.06f),
            MathF.Min(1f, Danger.Z + 0.06f),
            Danger.W);
        var active = new Vector4(Danger.X * 0.86f, Danger.Y * 0.86f, Danger.Z * 0.86f, Danger.W);
        ImGui.PushStyleColor(ImGuiCol.Button, Danger);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, active);
        try
        {
            return ImGui.Button(label);
        }
        finally
        {
            ImGui.PopStyleColor(3);
        }
    }

    private static bool DrawSecondaryButton(string label)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * ImGuiHelpers.GlobalScale);
        try
        {
            return ImGui.Button(label);
        }
        finally
        {
            ImGui.PopStyleVar();
        }
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, alpha);
}
