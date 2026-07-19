using System.Text;

namespace XIV.fm.Server.Domain.Accounts;

public sealed record LastFmAccountIdentity
{
    public LastFmAccountIdentity(string canonicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        var normalized = canonicalName.Normalize(NormalizationForm.FormKC).Trim();
        if (normalized.Length is 0 or > 128 || normalized.Any(char.IsControl))
            throw new ArgumentException("The Last.fm account name is invalid.", nameof(canonicalName));

        this.CanonicalName = normalized;
        this.NormalizedName = normalized.ToUpperInvariant();
    }

    public string CanonicalName { get; }

    public string NormalizedName { get; }
}
