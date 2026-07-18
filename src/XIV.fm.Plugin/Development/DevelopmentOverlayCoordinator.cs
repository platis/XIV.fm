using Dalamud.Plugin.Services;
using XIV.fm.Plugin.Adapters;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Development;

/// <summary>
/// Temporary framework-thread producer for local and mock remote overlay state.
/// A server sync coordinator will replace its mock presence production later.
/// </summary>
public sealed class DevelopmentOverlayCoordinator : IDisposable
{
    private const int MaximumMockRemoteCards = 32;

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly OverlayStateStore stateStore;
    private readonly Func<bool> showMockRemoteCards;
    private DateTimeOffset nextCaptureAt = DateTimeOffset.MinValue;

    public DevelopmentOverlayCoordinator(
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        OverlayStateStore stateStore,
        Func<bool> showMockRemoteCards)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.stateStore = stateStore;
        this.showMockRemoteCards = showMockRemoteCards;
        this.framework.Update += this.OnFrameworkUpdate;
        this.PublishSnapshot();
    }

    public void Dispose()
    {
        this.framework.Update -= this.OnFrameworkUpdate;
        this.stateStore.Publish(OverlaySnapshot.Empty);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < this.nextCaptureAt)
            return;

        this.nextCaptureAt = now.AddSeconds(1);
        this.PublishSnapshot(now);
    }

    private void PublishSnapshot() => this.PublishSnapshot(DateTimeOffset.UtcNow);

    private void PublishSnapshot(DateTimeOffset capturedAt)
    {
        var localPlayer = this.objectTable.LocalPlayer;
        if (localPlayer is null)
        {
            this.stateStore.Publish(OverlaySnapshot.Empty);
            return;
        }

        var localIdentity = DalamudCharacterIdentity.From(localPlayer);
        var cards = new List<OverlayCard>
        {
            OverlayCard.LocalPlaceholder(localIdentity),
        };

        if (this.showMockRemoteCards())
        {
            var remotePlayers = this.objectTable.PlayerObjects
                .OfType<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()
                .Where(player => player.GameObjectId != localPlayer.GameObjectId)
                .OrderBy(player => System.Numerics.Vector3.DistanceSquared(localPlayer.Position, player.Position))
                .Take(MaximumMockRemoteCards)
                .ToArray();

            for (var index = 0; index < remotePlayers.Length; index++)
            {
                cards.Add(OverlayCard.RemotePlaceholder(DalamudCharacterIdentity.From(remotePlayers[index]), index));
            }
        }

        var location = DalamudLocationScope.Capture(this.clientState, localPlayer);
        this.stateStore.Publish(OverlaySnapshot.Create(cards, capturedAt, location));
    }
}
