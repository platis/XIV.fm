using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using XIV.fm.Plugin.Core.Policy;

namespace XIV.fm.Plugin.UI;

public sealed class OnboardingWindow : Window
{
    private static readonly Vector4 Accent = new(0.88f, 0.23f, 0.36f, 1f);
    private static readonly Vector4 PanelSurface = new(0.169f, 0.169f, 0.169f, 0.9f);
    private static readonly Vector4 PanelBorder = new(0.42f, 0.42f, 0.44f, 0.5f);

    private readonly Func<string?> startAccountLink;
    private readonly Action openSettings;
    private readonly Action openGitHub;
    private readonly Action completeOnboarding;
    private readonly Func<bool> hasInstallationCredential;
    private readonly Func<DutyParticipationPolicy> dutyPolicy;
    private bool completed;
    private string? interactionMessage;

    public OnboardingWindow(
        Func<string?> startAccountLink,
        Action openSettings,
        Action openGitHub,
        Action completeOnboarding,
        Func<bool> hasInstallationCredential,
        Func<DutyParticipationPolicy> dutyPolicy)
        : base("Welcome to XIV.fm###XIV.fm.Onboarding")
    {
        this.startAccountLink = startAccountLink;
        this.openSettings = openSettings;
        this.openGitHub = openGitHub;
        this.completeOnboarding = completeOnboarding;
        this.hasInstallationCredential = hasInstallationCredential;
        this.dutyPolicy = dutyPolicy;
        this.Size = new Vector2(560f, 430f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500f, 400f),
            MaximumSize = new Vector2(700f, 560f),
        };
        this.RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 10f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12f, 7f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * scale);
        try
        {
            DrawWelcomePanel();
            ImGui.Spacing();

            ImGui.TextColored(Accent, "Bring your music into Eorzea.");
            ImGui.TextWrapped(
                "XIV.fm shows the song you’re listening to above your character. You can also discover what nearby listeners are playing.");
            ImGui.Spacing();
            ImGui.TextDisabled("Your listening card starts private. You can change who sees it anytime.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            this.DrawActions();

            if (!string.IsNullOrWhiteSpace(this.interactionMessage))
            {
                ImGui.Spacing();
                ImGui.TextWrapped(this.interactionMessage);
            }
        }
        finally
        {
            ImGui.PopStyleVar(3);
        }
    }

    public override void OnClose() => this.Complete();

    public void Complete()
    {
        if (!this.completed)
        {
            this.completed = true;
            this.completeOnboarding();
        }

        this.IsOpen = false;
    }

    private static void DrawWelcomePanel()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var height = 82f * scale;
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

        var symbolOrigin = origin + new Vector2(18f, 18f) * scale;
        drawList.AddText(symbolOrigin, ImGui.GetColorU32(Accent), "♫");
        var textOrigin = origin + new Vector2(52f, 15f) * scale;
        drawList.AddText(textOrigin, ImGui.GetColorU32(style.Colors[(int)ImGuiCol.Text]), "XIV.fm");
        drawList.AddText(
            textOrigin + new Vector2(0f, 28f * scale),
            ImGui.GetColorU32(style.Colors[(int)ImGuiCol.TextDisabled]),
            "Your soundtrack, above your nameplate.");
        ImGui.SetCursorScreenPos(new Vector2(origin.X, maximum.Y + (4f * scale)));
    }

    private void DrawActions()
    {
        var duty = this.dutyPolicy();
        var linked = this.hasInstallationCredential();
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);

        if (!linked && !duty.AllowsServerRequests)
            ImGui.BeginDisabled();

        if (DrawPrimaryButton(linked ? "Continue to XIV.fm" : "Link Last.fm", new Vector2(width, 0f)))
        {
            if (linked)
            {
                this.Complete();
                this.openSettings();
            }
            else
            {
                var error = this.startAccountLink();
                if (error is null)
                {
                    this.Complete();
                    this.openSettings();
                }
                else
                {
                    this.interactionMessage = "Couldn’t open Last.fm right now. Please try again in a moment.";
                }
            }
        }

        if (!linked && !duty.AllowsServerRequests)
            ImGui.EndDisabled();

        if (!linked && !duty.AllowsServerRequests)
            ImGui.TextDisabled("You can link Last.fm after leaving the duty.");

        var gap = ImGui.GetStyle().ItemSpacing.X;
        var secondaryWidth = MathF.Max(1f, (width - gap) / 2f);
        if (ImGui.Button("View on GitHub", new Vector2(secondaryWidth, 0f)))
            this.openGitHub();
        ImGui.SameLine();
        if (ImGui.Button("Maybe later", new Vector2(secondaryWidth, 0f)))
            this.Complete();
    }

    private static bool DrawPrimaryButton(string label, Vector2 size)
    {
        var hovered = new Vector4(
            MathF.Min(1f, Accent.X + 0.08f),
            MathF.Min(1f, Accent.Y + 0.08f),
            MathF.Min(1f, Accent.Z + 0.08f),
            Accent.W);
        var active = new Vector4(Accent.X * 0.86f, Accent.Y * 0.86f, Accent.Z * 0.86f, Accent.W);

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
        }
    }
}
