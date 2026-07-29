using Dalamud.Plugin.Services;
using XIV.fm.Plugin.Core.Overlay;
using XIV.fm.Plugin.Core.Policy;

namespace XIV.fm.Plugin.UI;

/// <summary>
/// Observes immutable card state outside rendering and schedules bounded artwork preparation.
/// </summary>
public sealed class AlbumArtworkCoordinator : IDisposable
{
    private readonly IFramework framework;
    private readonly OverlayStateStore stateStore;
    private readonly AlbumArtworkCache cache;
    private readonly Func<DutyParticipationPolicy> dutyPolicy;
    private DateTimeOffset nextPrepareAt = DateTimeOffset.MinValue;
    private bool wasSuspended;

    public AlbumArtworkCoordinator(
        IFramework framework,
        OverlayStateStore stateStore,
        AlbumArtworkCache cache,
        Func<DutyParticipationPolicy> dutyPolicy)
    {
        this.framework = framework;
        this.stateStore = stateStore;
        this.cache = cache;
        this.dutyPolicy = dutyPolicy;
        this.framework.Update += this.OnFrameworkUpdate;
    }

    public void Dispose() => this.framework.Update -= this.OnFrameworkUpdate;

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.dutyPolicy().AllowsServerRequests)
        {
            if (!this.wasSuspended)
                this.cache.Suspend();
            this.wasSuspended = true;
            return;
        }

        this.wasSuspended = false;
        var now = DateTimeOffset.UtcNow;
        if (now < this.nextPrepareAt)
            return;

        this.nextPrepareAt = now.AddSeconds(1);
        this.cache.Prepare(
            this.stateStore.Current.Cards
                .OrderByDescending(static card => card.IsLocal)
                .Select(static card => card.ArtworkUrl)
                .OfType<Uri>());
    }
}
