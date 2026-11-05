using NUnit.Framework;

namespace Game.Server.Tests
{
    /// <summary> Where the public web host's root sends a player: the web client at the root of the environment's CDN. </summary>
    [TestFixture]
    public class WebClientRedirectTests
    {
        const string CdnUrl = "https://silver-cobras-hunt-quickly-assets.p2-eu.metaplay.dev/";

        [Test]
        public void TheRootOfTheCdnIsTheWebClient()
        {
            Assert.That(WebClientRedirectController.WebClientUrl(CdnUrl, ""), Is.EqualTo(CdnUrl));
        }

        [Test]
        public void TheQueryCarriesOver()
        {
            Assert.That(WebClientRedirectController.WebClientUrl(CdnUrl, "?offline"), Is.EqualTo(CdnUrl + "?offline"));
            Assert.That(WebClientRedirectController.WebClientUrl(CdnUrl, "?wsPort=9380&cdnPort=5552"),
                Is.EqualTo(CdnUrl + "?wsPort=9380&cdnPort=5552"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void ThereIsNoWebClientWithoutAPublicCdn(string cdnUrl)
        {
            Assert.That(WebClientRedirectController.WebClientUrl(cdnUrl, "?offline"), Is.Null);
        }
    }
}
