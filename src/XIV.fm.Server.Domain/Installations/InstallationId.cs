namespace XIV.fm.Server.Domain.Installations;

public readonly record struct InstallationId
{
    public InstallationId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Installation ID cannot be empty.", nameof(value));

        this.Value = value;
    }

    public Guid Value { get; }

    public override string ToString() => this.Value.ToString("N");
}
