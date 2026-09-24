using WebClient.Meta.Ui;

namespace WebClient.Tests;

/// <summary>
/// The CSS class string that <see cref="MetaButtonClasses.For"/> returns for each button variant, size, block flag,
/// icon-only flag and forced state.
/// <para>
/// The stylesheet and the components both depend on these exact strings, so the tests assert the strings and
/// their order, not the resulting style.
/// </para>
/// </summary>
[TestFixture]
public class MetaButtonClassesTests
{
    [TestCase(MetaButtonVariant.Hero,      "m-btn m-btn--primary m-btn--key")]
    [TestCase(MetaButtonVariant.Primary,   "m-btn m-btn--primary")]
    [TestCase(MetaButtonVariant.Claim,     "m-btn m-btn--claim")]
    [TestCase(MetaButtonVariant.Live,      "m-btn m-btn--live")]
    [TestCase(MetaButtonVariant.Secondary, "m-btn")]
    [TestCase(MetaButtonVariant.Ghost,     "m-btn m-btn--quiet")]
    [TestCase(MetaButtonVariant.Danger,    "m-btn m-btn--danger")]
    public void EachVariantIsItsModifierOnTheBaseBox(MetaButtonVariant variant, string expected)
    {
        Assert.That(MetaButtonClasses.For(variant, MetaButtonSize.Md, block: false, iconOnly: false), Is.EqualTo(expected));
    }

    [TestCase(MetaButtonSize.Sm, "m-btn m-btn--sm")]
    [TestCase(MetaButtonSize.Md, "m-btn")]
    [TestCase(MetaButtonSize.Lg, "m-btn m-btn--lg")]
    public void TheSizesAreModifiersAndMdIsTheUnadornedBox(MetaButtonSize size, string expected)
    {
        Assert.That(MetaButtonClasses.For(MetaButtonVariant.Secondary, size, block: false, iconOnly: false), Is.EqualTo(expected));
    }

    /// <summary>Block extends the box across the column, and icon-only makes it a square.</summary>
    [TestCase(MetaButtonVariant.Primary, MetaButtonSize.Lg, true,  false, "m-btn m-btn--primary m-btn--lg m-btn--block")]
    [TestCase(MetaButtonVariant.Ghost,   MetaButtonSize.Md, false, true,  "m-btn m-btn--quiet m-btn--icon")]
    public void TheShapeFlagsAreModifiersAfterTheSize(MetaButtonVariant variant, MetaButtonSize size, bool block, bool iconOnly, string expected)
    {
        Assert.That(MetaButtonClasses.For(variant, size, block: block, iconOnly: iconOnly), Is.EqualTo(expected));
    }

    /// <summary>
    /// Icon-only wins over block: an icon-only button is square, so it must not stretch to the column width even
    /// when the caller also passes <c>block: true</c>.
    /// </summary>
    [Test]
    public void IconOnlyNeverIncludesBlock()
    {
        foreach (MetaButtonVariant variant in Enum.GetValues<MetaButtonVariant>())
        {
            foreach (MetaButtonSize size in Enum.GetValues<MetaButtonSize>())
            {
                string classes = MetaButtonClasses.For(variant, size, block: true, iconOnly: true);
                Assert.That(classes, Does.Contain("m-btn--icon"), $"{variant} {size}");
                Assert.That(classes, Does.Not.Contain("m-btn--block"), $"{variant} {size}");
            }
        }
    }

    /// <summary>
    /// Hero is always the primary style plus the <c>m-btn--key</c> modifier, at every size. Screens use Hero for
    /// their single main action.
    /// </summary>
    [Test]
    public void HeroAlwaysIncludesTheKey()
    {
        foreach (MetaButtonSize size in Enum.GetValues<MetaButtonSize>())
        {
            string classes = MetaButtonClasses.For(MetaButtonVariant.Hero, size, block: true, iconOnly: false);
            Assert.That(classes, Does.Contain("m-btn--primary"), size.ToString());
            Assert.That(classes, Does.Contain("m-btn--key"), size.ToString());
        }
    }

    [TestCase(MetaButtonForceState.None,   "m-btn")]
    [TestCase(MetaButtonForceState.Hover,  "m-btn m-btn--force-hover")]
    [TestCase(MetaButtonForceState.Active, "m-btn m-btn--force-active")]
    [TestCase(MetaButtonForceState.Focus,  "m-btn m-btn--force-focus")]
    public void AForcedStateIsAModifierTheGalleryAdds(MetaButtonForceState forceState, string expected)
    {
        Assert.That(MetaButtonClasses.For(MetaButtonVariant.Secondary, MetaButtonSize.Md, block: false, iconOnly: false, forceState), Is.EqualTo(expected));
    }

    /// <summary>
    /// A forced state adds exactly one <c>m-btn--force-*</c> class, and <see cref="MetaButtonForceState.None"/> adds
    /// none. Two force classes would apply two conflicting pointer styles. The test counts occurrences rather
    /// than matching the string, so a duplicate anywhere in the string fails it.
    /// </summary>
    [Test]
    public void AForcedStateIsExactlyOneForceClassOnEveryBox()
    {
        foreach (MetaButtonForceState forceState in Enum.GetValues<MetaButtonForceState>())
        {
            foreach (MetaButtonVariant variant in Enum.GetValues<MetaButtonVariant>())
            {
                foreach (MetaButtonSize size in Enum.GetValues<MetaButtonSize>())
                {
                    string classes = MetaButtonClasses.For(variant, size, block: true, iconOnly: false, forceState);
                    int forceClasses = CountOccurrences(classes, "m-btn--force-");
                    int expected = forceState == MetaButtonForceState.None ? 0 : 1;
                    Assert.That(forceClasses, Is.EqualTo(expected), $"{variant} {size} {forceState}: {classes}");
                }
            }
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    [Test]
    public void EveryVariantCarriesTheBaseBox()
    {
        foreach (MetaButtonVariant variant in Enum.GetValues<MetaButtonVariant>())
        {
            string classes = MetaButtonClasses.For(variant, MetaButtonSize.Md, block: false, iconOnly: false);
            Assert.That(classes == "m-btn" || classes.StartsWith("m-btn "), Is.True, $"{variant}: {classes}");
        }
    }
}
