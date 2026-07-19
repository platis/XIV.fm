using XIV.fm.Plugin.Core.Policy;

namespace XIV.fm.Plugin.Core.Tests.Policy;

public sealed class DutyParticipationPolicyTests
{
    [Fact]
    public void OutsideDutyAllowsOverlayAndServerRequests()
    {
        var policy = new DutyParticipationPolicy(IsInDuty: false);

        Assert.True(policy.AllowsOverlay);
        Assert.True(policy.AllowsServerRequests);
    }

    [Fact]
    public void InDutyBlocksOverlayAndServerRequests()
    {
        var policy = new DutyParticipationPolicy(IsInDuty: true);

        Assert.False(policy.AllowsOverlay);
        Assert.False(policy.AllowsServerRequests);
    }
}
