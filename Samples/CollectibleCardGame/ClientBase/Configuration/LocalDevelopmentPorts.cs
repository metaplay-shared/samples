namespace Game.ClientBase.Configuration;

/// <summary> A matching port range for an independently running local client and server checkout. </summary>
public readonly record struct LocalDevelopmentPorts
{
    public int Tcp { get; }
    public int WebSocket { get; }
    public int Cdn { get; }

    public LocalDevelopmentPorts(int offset)
    {
        if (offset < 0 || offset > 65535 - 9380)
            throw new ArgumentOutOfRangeException(nameof(offset));

        Tcp = 9339 + offset;
        WebSocket = 9380 + offset;
        Cdn = 5552 + offset;
    }
}
