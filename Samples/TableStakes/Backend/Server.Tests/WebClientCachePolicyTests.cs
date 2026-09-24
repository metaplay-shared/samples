using NUnit.Framework;

namespace Game.Server.Tests
{
    /// <summary>
    /// Tests which files of the published web client a browser may cache permanently
    /// (<see cref="WebClientCachePolicy"/>).
    /// <para>
    /// Only a file whose URL changes with its content may be immutable. If an unhashed file such as
    /// <c>_framework/dotnet.js</c> were immutable, a browser would keep its boot manifest and run the old client
    /// after every deploy until its cache was cleared. The file names below follow the patterns a .NET 10
    /// publish writes.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WebClientCachePolicyTests
    {
        [TestCase("_framework/WebClient.k2uek0leux.wasm")]
        [TestCase("_framework/System.Private.CoreLib.1cg3xccq1k.wasm")]
        [TestCase("_framework/dotnet.native.rdro6vsp75.wasm")]
        [TestCase("_framework/dotnet.native.acqoaayi41.js")]
        [TestCase("_framework/dotnet.runtime.r2kbxkuujc.js")]
        public void AContentHashedFrameworkFileIsImmutable(string path)
        {
            Assert.That(WebClientCachePolicy.CacheControlFor(path), Is.EqualTo(WebClientCachePolicy.Immutable));
        }

        /// <summary>
        /// Framework scripts that the publish does not hash. <c>dotnet.js</c> decides which build runs.
        /// </summary>
        [TestCase("_framework/dotnet.js")]
        [TestCase("_framework/blazor.webassembly.js")]
        public void AnUnhashedFrameworkFileIsRevalidated(string path)
        {
            Assert.That(WebClientCachePolicy.CacheControlFor(path), Is.EqualTo(WebClientCachePolicy.Revalidate));
        }

        /// <summary>
        /// Only the publish names files under <c>_framework/</c>, so a name that looks hashed elsewhere is not
        /// trusted.
        /// </summary>
        [TestCase("index.html")]
        [TestCase("build-info.json")]
        [TestCase("client-update.js")]
        [TestCase("app.css")]
        [TestCase("manifest.webmanifest")]
        [TestCase("icons/icon-192.png")]
        [TestCase("Assets/SharedGameConfig.mpa")]
        [TestCase("_content/WebClientBase/css/app-base.css")]
        [TestCase("Assets/Archive.k2uek0leux.mpa", Description = "hash-shaped name")]
        public void AFileOutsideTheFrameworkDirectoryIsRevalidated(string path)
        {
            Assert.That(WebClientCachePolicy.CacheControlFor(path), Is.EqualTo(WebClientCachePolicy.Revalidate));
        }

        /// <summary>The controller passes a path built with the server's own directory separator.</summary>
        [Test]
        public void AWindowsSeparatorIsAccepted()
        {
            Assert.That(WebClientCachePolicy.CacheControlFor("_framework\\WebClient.k2uek0leux.wasm"), Is.EqualTo(WebClientCachePolicy.Immutable));
            Assert.That(WebClientCachePolicy.CacheControlFor("_framework\\dotnet.js"), Is.EqualTo(WebClientCachePolicy.Revalidate));
        }

        [TestCase("WebClient.K2UEK0LEUX.wasm", Description = "uppercase")]
        [TestCase("WebClient.k2uek0leu.wasm", Description = "nine characters")]
        [TestCase("WebClient.k2uek0leuxx.wasm", Description = "eleven characters")]
        [TestCase("WebClient.k2uek-leux.wasm", Description = "a character outside letters and digits")]
        [TestCase("k2uek0leux.wasm", Description = "no name before the hash")]
        [TestCase("dotnet.native.js", Description = "no hash segment")]
        public void ANameThatOnlyResemblesAHashHasNone(string fileName)
        {
            Assert.That(WebClientCachePolicy.HasContentHash(fileName), Is.False);
        }
    }
}
