namespace XIV.fm.Plugin.Core.Policy;

/// <summary>
/// Defines whether XIV.fm may participate while the local player is bound by duty.
/// The same decision gates presentation and every future server request.
/// </summary>
public readonly record struct DutyParticipationPolicy(bool IsInDuty)
{
    public bool AllowsOverlay => !this.IsInDuty;

    public bool AllowsServerRequests => !this.IsInDuty;
}
