// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Server.AdminApi.AuditLog;

namespace Game.Server.Tests
{
    public class ExampleTests
    {
        [Test]
        public void ExampleTest()
        {
            // Example: Check the event code isn't changed accidentally.
            Assert.That(GameAuditLogEventCodes.SetWallet, Is.EqualTo(10_001));
        }
    }
}
