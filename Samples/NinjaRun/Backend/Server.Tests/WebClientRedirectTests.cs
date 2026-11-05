using NUnit.Framework;

namespace Game.Server.Tests
{
    /// <summary>Where the root of the public web host sends a player: the web client on the public CDN.</summary>
    [TestFixture]
    public class WebClientRedirectTests
    {
        const string CdnUrl = "https://whole-impalas-strive-quickly-assets.p2-eu.metaplay.dev/";

        [Test]
        public void TheRedirectKeepsTheQuery()
        {
            Assert.That(WebClientRedirectController.WebClientUrl(CdnUrl, ""), Is.EqualTo(CdnUrl));
            Assert.That(WebClientRedirectController.WebClientUrl(CdnUrl, null), Is.EqualTo(CdnUrl));
            Assert.That(WebClientRedirectController.WebClientUrl(CdnUrl, "?offline"), Is.EqualTo(CdnUrl + "?offline"));
            Assert.That(WebClientRedirectController.WebClientUrl(CdnUrl, "?wsPort=9380&cdnPort=5552"),
                Is.EqualTo(CdnUrl + "?wsPort=9380&cdnPort=5552"));
        }

        [Test]
        public void WithNoPublicCdnThereIsNoRedirect()
        {
            Assert.That(WebClientRedirectController.WebClientUrl(null, "?offline"), Is.Null);
            Assert.That(WebClientRedirectController.WebClientUrl("", ""), Is.Null);
        }
    }
}
