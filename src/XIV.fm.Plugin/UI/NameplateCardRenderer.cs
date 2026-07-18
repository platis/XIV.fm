using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using XIV.fm.Plugin.Adapters;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.UI;

/// <summary>
/// Renders immutable local and remote card state through one player/nameplate anchoring path.
/// </summary>
public sealed class NameplateCardRenderer
{
    private static readonly Vector4 AccentColor = new(0.88f, 0.23f, 0.36f, 1f);

    private const ImGuiWindowFlags CardWindowFlags =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.AlwaysAutoResize |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoMouseInputs;

    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;
    private readonly OverlayStateStore stateStore;
    private readonly Func<bool> isEnabled;
    private readonly Func<int> remoteDistanceYalms;
    private OverlayRenderDiagnostics diagnostics = OverlayRenderDiagnostics.Empty;
    private DateTimeOffset nextDiagnosticsPublishAt = DateTimeOffset.MinValue;

    public NameplateCardRenderer(
        IObjectTable objectTable,
        IGameGui gameGui,
        OverlayStateStore stateStore,
        Func<bool> isEnabled,
        Func<int> remoteDistanceYalms)
    {
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.stateStore = stateStore;
        this.isEnabled = isEnabled;
        this.remoteDistanceYalms = remoteDistanceYalms;
    }

    public OverlayRenderDiagnostics Diagnostics => Volatile.Read(ref this.diagnostics);

    public void Draw()
    {
        var snapshot = this.stateStore.Current;
        var localPlayer = this.objectTable.LocalPlayer;
        if (!this.isEnabled() || localPlayer is null || snapshot.Cards.IsEmpty)
        {
            this.PublishDiagnostics(snapshot.Cards.Length, 0, 0, 0, 0);
            return;
        }

        var matchedPlayers = 0;
        var inRangePlayers = 0;
        var projectedAnchors = 0;
        var renderedCards = 0;
        var loadedPlayers = this.objectTable.PlayerObjects.OfType<IPlayerCharacter>().ToArray();
        foreach (var card in snapshot.Cards)
        {
            var target = card.IsLocal
                ? localPlayer
                : FindLoadedPlayer(card.Character, loadedPlayers);

            if (target is null)
                continue;

            matchedPlayers++;
            if (!card.IsLocal && !OverlayVisibility.IsRemoteWithinRange(
                    localPlayer.Position,
                    target.Position,
                    this.remoteDistanceYalms()))
            {
                continue;
            }

            inRangePlayers++;
            if (!this.TryGetScreenAnchor(target, out var screenAnchor))
                continue;

            projectedAnchors++;
            DrawCard(card, screenAnchor);
            renderedCards++;
        }

        this.PublishDiagnostics(
            snapshot.Cards.Length,
            matchedPlayers,
            inRangePlayers,
            projectedAnchors,
            renderedCards);
    }

    private static IPlayerCharacter? FindLoadedPlayer(
        CharacterIdentity character,
        IReadOnlyList<IPlayerCharacter> loadedPlayers)
    {
        foreach (var player in loadedPlayers)
        {
            if (character.Matches(DalamudCharacterIdentity.From(player)))
                return player;
        }

        return null;
    }

    private static void DrawCard(OverlayCard card, Vector2 screenAnchor)
    {
        // The card's bottom center is kept just above the projected character/nameplate area.
        screenAnchor.Y -= 10f;
        ImGui.SetNextWindowPos(screenAnchor, ImGuiCond.Always, new Vector2(0.5f, 1f));
        ImGui.SetNextWindowBgAlpha(0.82f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(9f, 7f));

        var windowId = $"XIV.fm card###XIV.fm.Card.{card.Character.Name}.{card.Character.HomeWorldId}";
        try
        {
            var shouldDrawContents = ImGui.Begin(windowId, CardWindowFlags);
            try
            {
                if (shouldDrawContents)
                {
                    ImGui.TextColored(AccentColor, "XIV.fm");
                    ImGui.Separator();
                    ImGui.TextUnformatted(card.Title);
                    ImGui.TextDisabled(card.Artist);
                }
            }
            finally
            {
                ImGui.End();
            }
        }
        finally
        {
            ImGui.PopStyleVar(2);
        }
    }

    private bool TryGetScreenAnchor(IPlayerCharacter player, out Vector2 screenAnchor)
    {
        var worldAnchor = OverlayAnchor.AboveCharacter(player.Position);
        return this.gameGui.WorldToScreen(worldAnchor, out screenAnchor);
    }

    private void PublishDiagnostics(
        int requestedCards,
        int matchedPlayers,
        int inRangePlayers,
        int projectedAnchors,
        int renderedCards)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < this.nextDiagnosticsPublishAt)
            return;

        this.nextDiagnosticsPublishAt = now.AddSeconds(1);
        Interlocked.Exchange(
            ref this.diagnostics,
            new OverlayRenderDiagnostics(
                requestedCards,
                matchedPlayers,
                inRangePlayers,
                projectedAnchors,
                renderedCards,
                now));
    }
}
