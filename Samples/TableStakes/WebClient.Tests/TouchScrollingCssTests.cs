using NUnit.Framework;

namespace WebClient.Tests;

/// <summary>
/// No stylesheet sets <c>-webkit-overflow-scrolling</c>. On iOS the property makes a scroll surface a stacking
/// context, so a sheet opened inside the shell's scroll surface is drawn under the navigation bar however high its
/// z-index is. Only iOS WebKit does this, so no browser test can catch it and this test reads the stylesheets as text.
/// </summary>
[TestFixture]
public class TouchScrollingCssTests
{
    [Test]
    public void NoRuleSetsWebkitOverflowScrolling()
    {
        foreach ((string name, string css) in CssSource.Stylesheets())
        {
            Assert.That(CssSource.WithoutComments(css), Does.Not.Contain("-webkit-overflow-scrolling"),
                $"{name} sets -webkit-overflow-scrolling, which on iOS puts the navigation bar over every sheet a screen opens");
        }
    }
}
