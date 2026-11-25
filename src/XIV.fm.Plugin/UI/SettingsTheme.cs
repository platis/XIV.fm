using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using XIV.fm.Plugin.Core.Sync;

namespace XIV.fm.Plugin.UI;

/// <summary>
/// Colors and controls shared by the XIV.fm windows. Listening cards do not use this palette.
/// </summary>
internal static class SettingsTheme
{
    public static readonly Vector4 Accent = new(0.87f, 0.73f, 0.45f, 1f);
    public static readonly Vector4 Success = new(0.32f, 0.78f, 0.48f, 1f);
    public static readonly Vector4 Warning = new(0.98f, 0.62f, 0.32f, 1f);
    public static readonly Vector4 Danger = new(1f, 0.52f, 0.5f, 1f);
    public static readonly Vector4 Neutral = new(0.72f, 0.74f, 0.78f, 1f);
    public static readonly Vector4 PanelSurface = new(0.169f, 0.169f, 0.169f, 0.82f);
    public static readonly Vector4 PanelSurfaceHovered = new(0.215f, 0.215f, 0.215f, 0.9f);
    public static readonly Vector4 PanelBorder = new(0.42f, 0.42f, 0.44f, 0.5f);

    private static readonly Vector4 AccentHovered = new(0.93f, 0.8f, 0.53f, 1f);
    private static readonly Vector4 AccentActive = new(0.76f, 0.63f, 0.37f, 1f);
    private static readonly Vector4 OnAccent = new(0.11f, 0.1f, 0.08f, 1f);
    private static readonly Vector4 DangerFill = new(0.7f, 0.17f, 0.17f, 1f);
    private static readonly Vector4 DangerFillHovered = new(0.78f, 0.22f, 0.21f, 1f);
    private static readonly Vector4 DangerFillActive = new(0.6f, 0.13f, 0.13f, 1f);
    private static readonly Vector4 OnDanger = new(1f, 1f, 1f, 1f);

    public static Vector4 GetTone(ConnectionTone tone) => tone switch
    {
        ConnectionTone.Success => Success,
        ConnectionTone.Warning => Warning,
        ConnectionTone.Danger => Danger,
        _ => Neutral,
    };

    public static bool DrawPrimaryButton(string label, Vector2 size = default) =>
        DrawButton(label, size, OnAccent, Accent, AccentHovered, AccentActive);

    public static bool DrawDangerButton(string label) =>
        DrawButton(label, default, OnDanger, DangerFill, DangerFillHovered, DangerFillActive);

    public static bool DrawOutlinedDangerButton(string label)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(Danger, 0.78f));
        try
        {
            return DrawButton(
                label,
                default,
                Danger,
                Vector4.Zero,
                WithAlpha(Danger, 0.12f),
                WithAlpha(Danger, 0.22f));
        }
        finally
        {
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
        }
    }

    public static bool DrawSecondaryButton(string label)
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

    public static Vector4 WithAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, alpha);

    private static bool DrawButton(
        string label,
        Vector2 size,
        Vector4 text,
        Vector4 background,
        Vector4 hovered,
        Vector4 active)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.Text, text);
        ImGui.PushStyleColor(ImGuiCol.Button, background);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, active);
        try
        {
            return ImGui.Button(label, size);
        }
        finally
        {
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar();
        }
    }
}
