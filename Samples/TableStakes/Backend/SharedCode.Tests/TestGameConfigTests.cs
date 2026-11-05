using Metaplay.Core;
using NUnit.Framework;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The wallet values that <see cref="TestGameConfig"/> copies into the config it builds in code are the ones the
    /// committed archive ships, so the tests that build a config of their own run against the shipped wallet.
    /// </summary>
    [TestFixture]
    public class TestGameConfigTests
    {
        [Test]
        public async Task TheCopiedWalletIsTheShippedOne()
        {
            SharedGameConfig shipped = await TestGameConfig.LoadSharedAsync();

            Assert.That(PrettyPrint.Verbose(TestGameConfig.StartingWallet()).ToString(),
                Is.EqualTo(PrettyPrint.Verbose(shipped.Global.StartingWallet).ToString()), "the starting wallet");
            Assert.That(shipped.Global.MaxCoins,      Is.EqualTo(TestGameConfig.MaxCoins));
            Assert.That(shipped.Global.MaxGems,       Is.EqualTo(TestGameConfig.MaxGems));
            Assert.That(shipped.Global.MaxSpinTokens, Is.EqualTo(TestGameConfig.MaxSpinTokens));
        }
    }
}
