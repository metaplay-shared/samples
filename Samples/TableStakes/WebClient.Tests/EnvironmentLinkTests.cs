using WebClientBase.Utilities;

namespace WebClient.Tests;

/// <summary>
/// Tests that <see cref="EnvironmentLink"/> carries the <c>env</c> override from the page URL onto in-app links. The
/// string overloads take the page URL instead of a <c>NavigationManager</c>, so no browser is needed.
/// </summary>
[TestFixture]
public class EnvironmentLinkTests
{
    /// <summary>
    /// A page with no override leaves the path alone. Otherwise the override, and only the override, is appended to
    /// the path, joins a query the path already has, and is escaped on the way out. A URI that cannot be parsed
    /// carries no override.
    /// </summary>
    [TestCase("http://localhost:5290/",                                          "/shop",                "/shop")]
    [TestCase("http://localhost:5290/events?wsPort=41337",                       "/shop",                "/shop")]
    [TestCase("http://localhost:5290/?env=offline",                              "/shop",                "/shop?env=offline")]
    [TestCase("http://localhost:5290/?env=offline",                              "/",                    "/?env=offline")]
    [TestCase("http://localhost:5290/table?wsPort=41337&env=offline&matchSeed=7", "/events/spin?spinMs=0", "/events/spin?spinMs=0&env=offline")]
    [TestCase("http://localhost:5290/?env=a%20b",                                "/shop",                "/shop?env=a%20b")]
    [TestCase("not a uri",                                                       "/shop",                "/shop")]
    public void TheLinkCarriesOnlyTheEnvironmentOverride(string pageUri, string path, string expected)
    {
        Assert.That(EnvironmentLink.Href(pageUri, path), Is.EqualTo(expected));
    }

    [TestCase("http://localhost:5290/events?wsPort=41337", null)]
    [TestCase("http://localhost:5290/?env=offline",        "offline")]
    [TestCase("http://localhost:5290/?env=a%20b",          "a b")]
    [TestCase("not a uri",                                 null)]
    public void TheOverrideIsReadFromThePageUri(string pageUri, string? expected)
    {
        Assert.That(EnvironmentLink.CurrentOverride(pageUri), Is.EqualTo(expected));
    }
}
