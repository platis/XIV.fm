namespace XIV.fm.Contracts.V1;

/// <summary>
/// Returns a newly issued opaque installation credential exactly once.
/// </summary>
public sealed record InstallationCredentialResponse(string Credential);
