namespace XIV.fm.Plugin.Network;

public sealed class ServerSyncException : Exception
{
    public ServerSyncException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    public string Code { get; }
}
