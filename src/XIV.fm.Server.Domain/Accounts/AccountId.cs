namespace XIV.fm.Server.Domain.Accounts;

public readonly record struct AccountId
{
    public AccountId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Account IDs cannot be empty.", nameof(value));

        this.Value = value;
    }

    public Guid Value { get; }
}
