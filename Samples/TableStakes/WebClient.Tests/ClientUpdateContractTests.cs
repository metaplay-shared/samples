using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// Checks the parts of the client update path that the E2E suite cannot see, because it runs the page with the
/// build ID placeholder unreplaced: the placeholder in <c>WebClient/wwwroot/index.html</c>, which
/// <c>tools/ServerImageBuild.cs</c> replaces when it stages a build, and the boot script that adds the build ID to
/// the framework script URLs.
/// </summary>
[TestFixture]
public class ClientUpdateContractTests
{
    private const string Placeholder = "<meta name=\"ts-build-id\" content=\"dev\" />";

    /// <summary>The image build fails unless index.html holds the placeholder exactly once.</summary>
    [Test]
    public void IndexHtmlHoldsTheBuildIdPlaceholderOnce()
    {
        string html = File.ReadAllText(IndexHtmlPath());

        Assert.That(Regex.Matches(html, Regex.Escape(Placeholder)).Count, Is.EqualTo(1),
            "index.html must hold the build ID placeholder exactly once, or the image build stops at staging");
    }

    [Test]
    public void TheImageBuildReplacesTheSamePlaceholder()
    {
        string tool = File.ReadAllText(CssSource.RepoPath("tools/ServerImageBuild.cs"));

        Assert.That(tool, Does.Contain(Placeholder.Replace("\"", "\\\"")),
            "tools/ServerImageBuild.cs looks for a different placeholder than index.html holds");
    }

    /// <summary>
    /// A static script tag would load <c>blazor.webassembly.js</c>, and through it <c>dotnet.js</c>, from the same URL
    /// in every build, so a browser could serve an older build's files from its cache.
    /// </summary>
    [Test]
    public void BlazorIsStartedWithTheBuildIdInTheFrameworkScriptUrls()
    {
        string html = File.ReadAllText(IndexHtmlPath());

        Assert.That(html, Does.Not.Match(@"<script[^>]*\ssrc=""_framework/blazor\.webassembly\.js"""),
            "index.html loads blazor.webassembly.js with a static script tag, which carries no build ID");
        Assert.That(html, Does.Contain("setAttribute('autostart', 'false')"),
            "the boot script must stop Blazor starting itself, or loadBootResource is never passed");
        Assert.That(html, Does.Contain("type === 'dotnetjs'"),
            "the boot script does not give dotnet.js the build ID, so the boot manifest can come from an old cache");
        Assert.That(html, Does.Contain("<script src=\"client-update.js\"></script>"),
            "index.html does not load client-update.js, so a running client never notices a deploy");
    }

    private static string IndexHtmlPath() => CssSource.RepoPath("WebClient/wwwroot/index.html");
}
