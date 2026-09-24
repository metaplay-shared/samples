using Game.Server;
using NUnit.Framework;
using System;
using System.IO;

namespace Game.Server.Tests;

public class WebClientCachePolicyTests
{
    [TestCase("index.html")]
    [TestCase("_framework/dotnet.js")]
    [TestCase("_framework/blazor.webassembly.js")]
    [TestCase("_framework/blazor.boot.json")]
    [TestCase("_framework/Client.wasm")]
    [TestCase("metagame.css")]
    [TestCase("board-targeting.js")]
    [TestCase("appsettings.json")]
    public void StableUrlsAlwaysRevalidate(string path)
        => Assert.That(WebClientCachePolicy.CacheControl(path), Is.EqualTo("no-cache"));

    [TestCase("_framework/Client.hjv3au171h.wasm")]
    [TestCase("_framework/dotnet.runtime.2tx45g8lli.js")]
    [TestCase("_framework/Client.hjv3au171h.wasm.br")]
    public void ContentAddressedFrameworkFilesRemainCacheable(string path)
        => Assert.That(WebClientCachePolicy.CacheControl(path), Does.Contain("immutable"));

    [Test]
    public void EntryPointVersionChangesForCodeAndStylesButNotTimestamps()
    {
        string root = Path.Combine(Path.GetTempPath(), "stickypaws-cache-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "_framework"));
        try
        {
            string css = Path.Combine(root, "metagame.css");
            string loader = Path.Combine(root, "_framework", "dotnet.js");
            File.WriteAllText(css, "body{}");
            File.WriteAllText(loader, "old assembly manifest");
            string first = WebClientCachePolicy.Version(root);
            File.SetLastWriteTimeUtc(css, DateTime.UtcNow.AddDays(1));
            Assert.That(WebClientCachePolicy.Version(root), Is.EqualTo(first));
            File.WriteAllText(loader, "new assembly manifest");
            string second = WebClientCachePolicy.Version(root);
            Assert.That(second, Is.Not.EqualTo(first));
            File.WriteAllText(css, "body{color:red}");
            Assert.That(WebClientCachePolicy.Version(root), Is.Not.EqualTo(second));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
