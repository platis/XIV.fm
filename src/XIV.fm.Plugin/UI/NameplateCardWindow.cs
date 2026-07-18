using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.UI;

/// <summary>
/// Temporary renderer used to validate that an XIV.fm card follows the local character.
/// Final visual design is deliberately deferred until the product behavior is complete.
/// </summary>
public sealed class NameplateCardWindow : Window
{
    private static readonly Vector4 AccentColor = new(0.88f, 0.23f, 0.36f, 1f);

    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;
    private readonly Func<bool> isEnabled;

    public NameplateCardWindow(IObjectTable objectTable, IGameGui gameGui, Func<bool> isEnabled)
        : base(
            "XIV.fm placeholder card###XIV.fm.NameplateCard",
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoMouseInputs)
    {
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.isEnabled = isEnabled;
        this.IsOpen = true;
        this.RespectCloseHotkey = false;
    }

    public override bool DrawConditions() =>
        this.isEnabled() &&
        this.objectTable.LocalPlayer is not null &&
        this.TryGetScreenAnchor(out _);

    public override void PreDraw()
    {
        if (!this.TryGetScreenAnchor(out var screenAnchor))
            return;

        // The bottom center of the card sits above the projected character/nameplate area.
        screenAnchor.Y -= 10f;
        ImGui.SetNextWindowPos(screenAnchor, ImGuiCond.Always, new Vector2(0.5f, 1f));
        ImGui.SetNextWindowBgAlpha(0.82f);
    }

    public override void Draw()
    {
        var card = PlaceholderCard.Default;

        ImGui.TextColored(AccentColor, "XIV.fm");
        ImGui.Separator();
        ImGui.TextUnformatted(card.Title);
        ImGui.TextDisabled(card.Artist);
    }

    private bool TryGetScreenAnchor(out Vector2 screenAnchor)
    {
        var localPlayer = this.objectTable.LocalPlayer;
        if (localPlayer is null)
        {
            screenAnchor = default;
            return false;
        }

        var worldAnchor = OverlayAnchor.AboveCharacter(localPlayer.Position);
        return this.gameGui.WorldToScreen(worldAnchor, out screenAnchor);
    }
}
