using Game.ClientBase.Configuration;

namespace Game.Client.Tests;

public sealed class LocalDevelopmentPortsTests
{
    [TestCase(0, 9339, 9380, 5552)]
    [TestCase(10000, 19339, 19380, 15552)]
    public void CheckoutOffsetMovesEveryClientEndpoint(int offset, int tcp, int webSocket, int cdn)
    {
        LocalDevelopmentPorts ports = new LocalDevelopmentPorts(offset);
        Assert.That((ports.Tcp, ports.WebSocket, ports.Cdn), Is.EqualTo((tcp, webSocket, cdn)));
    }

    [TestCase(-1)]
    [TestCase(56156)]
    public void OffsetCannotProduceAnInvalidPort(int offset)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new LocalDevelopmentPorts(offset));
}
