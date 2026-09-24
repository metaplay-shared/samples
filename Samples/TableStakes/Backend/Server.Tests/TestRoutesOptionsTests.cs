using NUnit.Framework;
using System.IO;

namespace Game.Server.Tests
{
    /// <summary>
    /// The <c>test/</c> routes are unauthenticated, so they must be off on every server that the E2E harness did
    /// not start (<c>docs/architecture.md</c>, "HTTP endpoints"). The harness turns them on from the command line,
    /// so neither the default value nor any options file may turn them on.
    /// </summary>
    [TestFixture]
    public class TestRoutesOptionsTests
    {
        [Test]
        public void TheTestRoutesAreOffByDefault()
        {
            Assert.That(new TestRoutesOptions().Enabled, Is.False);
        }

        [Test]
        public void NoOptionsFileTurnsTheTestRoutesOn()
        {
            string configDir = Path.Combine(GameConfigBuildTests.RepoRoot(), "Backend", "Server", "Config");
            string[] files = Directory.GetFiles(configDir, "Options.*.yaml");
            Assert.That(files, Is.Not.Empty, $"no options files found in {configDir}");

            foreach (string file in files)
                Assert.That(File.ReadAllText(file), Does.Not.Contain("TestRoutes"), Path.GetFileName(file));
        }
    }
}
