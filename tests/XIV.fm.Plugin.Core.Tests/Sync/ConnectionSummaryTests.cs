using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Sync;

namespace XIV.fm.Plugin.Core.Tests.Sync;

public sealed class ConnectionSummaryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly ListeningState Playing = new(
        ListeningStatus.Playing,
        false,
        Now,
        new Track("Song", "Band", null, null, null, null));
    private static readonly ListeningState Unavailable =
        new(ListeningStatus.Unavailable, false, null, null);

    [Fact]
    public void StoredCredentialAloneIsNotAConnection()
    {
        var summary = Create(new SyncRuntimeState(SyncRuntimeStatus.Waiting, Now), Unavailable);

        Assert.Equal("Connecting", summary.Title);
        Assert.NotEqual(ConnectionTone.Success, summary.Tone);
    }

    [Fact]
    public void MissingLinkIsNotConnected()
    {
        var summary = ConnectionSummary.Create(
            false,
            null,
            false,
            SyncRuntimeState.Disabled,
            Unavailable,
            Now);

        Assert.Equal("Not connected", summary.Title);
    }

    [Theory]
    [InlineData(SyncRuntimeStatus.Healthy)]
    [InlineData(SyncRuntimeStatus.Syncing)]
    [InlineData(SyncRuntimeStatus.Waiting)]
    public void RecentSuccessStaysConnectedBetweenRequests(SyncRuntimeStatus status)
    {
        var summary = Create(
            new SyncRuntimeState(status, Now, LastSuccessAt: Now.AddSeconds(-6)),
            Playing);

        Assert.Equal("Connected", summary.Title);
        Assert.Equal("Listener · Song by Band", summary.Detail);
        Assert.Equal(ConnectionTone.Success, summary.Tone);
    }

    [Fact]
    public void OldSuccessIsNotReportedAsConnected()
    {
        var summary = Create(
            new SyncRuntimeState(SyncRuntimeStatus.Waiting, Now, LastSuccessAt: Now.AddMinutes(-2)),
            Unavailable);

        Assert.Equal("Connecting", summary.Title);
    }

    [Theory]
    [InlineData(SyncRuntimeStatus.Failed, 12, "Next attempt in 12 seconds.")]
    [InlineData(SyncRuntimeStatus.Failed, 1, "Next attempt in 1 second.")]
    [InlineData(SyncRuntimeStatus.Syncing, 0, "Retrying now.")]
    public void FailureIsVisibleEvenAfterARecentSuccess(SyncRuntimeStatus status, int seconds, string expected)
    {
        var summary = Create(
            new SyncRuntimeState(
                status,
                Now,
                Now.AddSeconds(seconds),
                Error: "The XIV.fm server is unavailable.",
                LastSuccessAt: Now.AddSeconds(-10)),
            Playing);

        Assert.Equal("Reconnecting", summary.Title);
        Assert.Contains(expected, summary.Detail, StringComparison.Ordinal);
        Assert.Equal(ConnectionTone.Warning, summary.Tone);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedCredentialAsksForANewLink(bool isInDuty)
    {
        var summary = ConnectionSummary.Create(
            true,
            "Listener",
            isInDuty,
            new SyncRuntimeState(
                SyncRuntimeStatus.Failed,
                Now,
                Error: "rejected",
                ErrorCode: ConnectionSummary.CredentialRejectedCode),
            Unavailable,
            Now);

        Assert.True(summary.RequiresRelink);
        Assert.Equal(ConnectionTone.Danger, summary.Tone);
    }

    [Fact]
    public void LostRelayIsExplained()
    {
        var summary = Create(
            new SyncRuntimeState(
                SyncRuntimeStatus.Failed,
                Now,
                Error: "rejected",
                ErrorCode: ConnectionSummary.RelayMembershipRequiredCode),
            Playing);

        Assert.Equal("Updating Relays", summary.Title);
        Assert.False(summary.RequiresRelink);
    }

    [Fact]
    public void DutyPausesWithoutReportingAFailure()
    {
        var summary = ConnectionSummary.Create(
            true,
            "Listener",
            true,
            new SyncRuntimeState(SyncRuntimeStatus.SuspendedDuty, Now, Error: "old failure"),
            Playing,
            Now);

        Assert.Equal("Paused in duty", summary.Title);
    }

    [Fact]
    public void MissingLastFmDataIsAWarning()
    {
        var summary = Create(
            new SyncRuntimeState(SyncRuntimeStatus.Healthy, Now, LastSuccessAt: Now),
            Unavailable);

        Assert.Equal("Connected", summary.Title);
        Assert.Equal(ConnectionTone.Warning, summary.Tone);
        Assert.Contains("privacy settings", summary.Detail, StringComparison.Ordinal);
    }

    private static ConnectionSummary Create(SyncRuntimeState sync, ListeningState listening) =>
        ConnectionSummary.Create(true, "Listener", false, sync, listening, Now);
}
