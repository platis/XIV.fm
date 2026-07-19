namespace XIV.fm.Server.Domain.AccountLinks;

public readonly record struct AccountLinkSessionId
{
    public AccountLinkSessionId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Account-link session IDs cannot be empty.", nameof(value));

        this.Value = value;
    }

    public Guid Value { get; }
}
