using NUnit.Framework;

namespace Game.Server.Tests
{
    [TestFixture]
    public class WebClientRedirectTests
    {
        const string CdnUrl = "https://lovely-wombats-assets.example.test/";

        [Test]
        public void TheRootRedirectsToTheCdnRootWithItsQuery()
        {
            Assert.That(WebClientRedirectController.RedirectTarget(CdnUrl, "?offline&wsPort=9380"),
                Is.EqualTo(CdnUrl + "?offline&wsPort=9380"));
            Assert.That(WebClientRedirectController.RedirectTarget(CdnUrl, ""), Is.EqualTo(CdnUrl));
        }

        [TestCase(null)]
        [TestCase("")]
        public void WithoutAPublicCdnThereIsNoRedirect(string cdnUrl)
        {
            Assert.That(WebClientRedirectController.RedirectTarget(cdnUrl, "?offline"), Is.Null);
        }
    }
}
