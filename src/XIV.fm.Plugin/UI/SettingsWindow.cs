using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
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
    private readonly Action requestSync;
    private readonly Func<bool> hasInstallationCredential;
    private readonly Func<DutyParticipationPolicy> dutyPolicy;
    private readonly Func<AccountLinkRuntimeState> linkState;
    private readonly Func<AccountDisconnectRuntimeState> disconnectState;
    private readonly Func<SyncRuntimeState> syncState;
    private readonly Func<OverlaySnapshot> overlaySnapshot;
    private readonly Func<OverlayRenderDiagnostics> renderDiagnostics;
    private string? interactionMessage;
    private bool confirmingDisconnect;

    public SettingsWindow(
        PluginConfiguration configuration,
        Action saveConfiguration,
        Func<string?> startAccountLink,
        Action cancelAccountLink,
        Func<string?> disconnectAccount,
        Action openLastFm,
        Action requestSync,
        Func<bool> hasInstallationCredential,
        Func<DutyParticipationPolicy> dutyPolicy,
        Func<AccountLinkRuntimeState> linkState,
        Func<AccountDisconnectRuntimeState> disconnectState,
        Func<SyncRuntimeState> syncState,
        Func<OverlaySnapshot> overlaySnapshot,
        Func<OverlayRenderDiagnostics> renderDiagnostics)
        : base("XIV.fm###XIV.fm.Settings")
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.startAccountLink = startAccountLink;
        this.cancelAccountLink = cancelAccountLink;
        this.disconnectAccount = disconnectAccount;
        this.openLastFm = openLastFm;
        this.requestSync = requestSync;
        this.hasInstallationCredential = hasInstallationCredential;
        this.dutyPolicy = dutyPolicy;
        this.linkState = linkState;
        this.disconnectState = disconnectState;
        this.syncState = syncState;
        this.overlaySnapshot = overlaySnapshot;
        this.renderDiagnostics = renderDiagnostics;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620f, 500f),
            MaximumSize = new Vector2(960f, 860f),
        };
    }

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

            if (ImGui.BeginTabItem("Diagnostics"))
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

            if (DrawPrimaryButton("Open Last.fm profile"))
                this.openLastFm();
            ImGui.SameLine();
            if (DrawSecondaryButton("Sync now"))
                this.requestSync();

            ImGui.Spacing();
            if (disconnect.Status == AccountDisconnectRuntimeStatus.Failed)
            {
                DrawStatusPanel(
                    "Couldn’t disconnect",
                    "XIV.fm couldn’t remove the link. You can try again.",
                    Danger);
            }

            if (!this.confirmingDisconnect)
            {
                if (DrawSecondaryButton("Disconnect Last.fm"))
                    this.confirmingDisconnect = true;
                return;
            }

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
                    "Waiting for your browser",
                    "Approve XIV.fm in the browser. This screen updates automatically when authorization completes.",
                    Warning);
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
            this.interactionMessage = error ?? "Opening Last.fm authorization in your browser…";
        }

        if (!duty.AllowsServerRequests)
            ImGui.EndDisabled();

        if (!duty.AllowsServerRequests)
            ImGui.TextDisabled("Unavailable while duty-bound.");
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
        ImGui.TextDisabled("Hides both your card and nearby listeners’ cards without changing your privacy setting.");

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

        DrawChoiceCard(
            "XIV.fm.Visibility.Custom",
            "Custom Relays · Coming soon",
            "Share only with invitation-based groups you choose.",
            visibility == VisibilityMode.Custom,
            false);

        ImGui.Spacing();
        ImGui.TextDisabled("Changing this setting requests an immediate sync.");
    }

    private void DrawRelaysTab()
    {
        DrawSectionHeader(
            "Custom Relays",
            "Private, invitation-based audiences for the people you choose.");

        var body = this.hasInstallationCredential()
            ? "Relay creation, invitations, membership, and audience selection are coming to this screen. Until then, choose Private or Public."
            : "Connect Last.fm first. Relays will be tied securely to your linked XIV.fm account.";
        DrawStatusPanel("Coming soon", body, Neutral);
    }

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
            this.configuration.DeveloperServerEnabled = enabled;
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
            this.configuration.DeveloperServerBaseUrl = baseUrl.Trim();
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
        var height = 66f * scale;
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

        var textOrigin = origin + new Vector2(13f * scale, 10f * scale);
        drawList.AddText(textOrigin, ImGui.GetColorU32(titleColor), title);
        drawList.AddText(
            textOrigin + new Vector2(0f, 25f * scale),
            ImGui.GetColorU32(descriptionColor),
            description);

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
