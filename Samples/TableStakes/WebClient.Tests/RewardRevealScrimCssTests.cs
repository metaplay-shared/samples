namespace WebClient.Tests;

/// <summary>
/// The reward reveal's backdrop (<c>.m-reveal</c>) is a radial gradient from <c>--scrim</c> at the centre to the
/// near-black <c>--scrim-deep</c> at the edges, so the screen behind stays visible. The gradient is a static
/// background with no animation, so it does not change the reveal's entrance or exit timing (see
/// <see cref="RevealCascadeTests"/>).
/// <para>
/// Like <see cref="MedallionCssTests"/>, the tests read the stylesheets as text. Each rule is taken as the
/// brace-balanced block that follows its selector (<see cref="CssSource.RuleBlock"/>).
/// </para>
/// </summary>
[TestFixture]
public class RewardRevealScrimCssTests
{
    [Test]
    public void TheScrimIsARadialVignette()
    {
        string reveal = CssSource.RuleBlock(CssSource.MetaCss(), ".m-reveal");

        Assert.That(reveal, Is.Not.Empty, "meta-shell.css has no .m-reveal rule — the scrim is gone");
        Assert.That(reveal, Does.Contain("radial-gradient("),
            ".m-reveal's background is not the radial vignette");
        Assert.That(reveal, Does.Contain("var(--scrim-deep)"),
            ".m-reveal's vignette does not reach the deep stop — the edges would not fall to near-black");
        Assert.That(reveal, Does.Not.Match(@"(^|[;{])\s*animation\s*:"),
            ".m-reveal animates itself — the scrim must appear with the first frame so the entrance " +
            "budget and the exit's opacity animation stay untouched");
    }

    [Test]
    public void TheVignettesEdgeStopIsADeclaredToken()
    {
        Assert.That(CssSource.AppCss(), Does.Contain("--scrim-deep:"),
            "app.css does not declare --scrim-deep, the stop the vignette's edges fall to");
    }
}
