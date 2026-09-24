using System;
using System.Linq;
using System.Reflection;
using Game.Logic;
using WebClient.Meta;
using WebClient.Meta.Fixtures;
using WebClient.Meta.Ui;

namespace WebClient.Tests;

/// <summary>
/// Tests for the button label rule in <see cref="ButtonLabels"/>: a label says what tapping does, in at most two
/// words, and contains no digits. Amounts, costs and conditions are drawn beside the button instead.
/// <para>
/// <c>Events.CallToActionLabel</c> (through <see cref="ButtonLabels.For"/>) and <c>NextUpCard.CallToAction</c> return
/// labels from <see cref="ButtonLabels"/>, so these tests check the class constants and the labels
/// <see cref="ButtonLabels.For"/> returns for every fixture view.
/// </para>
/// </summary>
[TestFixture]
public class ButtonLabelsTests
{
    [Test]
    public void EveryLabelTheClassCarriesIsShort()
    {
        FieldInfo[] labels = typeof(ButtonLabels)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string) && field.IsLiteral)
            .ToArray();

        Assert.That(labels, Has.Length.AtLeast(14),
            "the class carries the shell's verbs; a label lost here is a call site falling back to literals");

        foreach (FieldInfo label in labels)
        {
            string value = (string)label.GetValue(null)!;
            Assert.That(ButtonLabels.IsShort(value), Is.True,
                $"{label.Name} = \"{value}\" breaks the rule it is meant to enforce");
        }
    }

    [TestCase("Claim reward", ExpectedResult = true)]   // The longest label the rule allows.
    [TestCase("Try again", ExpectedResult = true)]
    [TestCase("Claim rewards", ExpectedResult = false)] // One character over the length limit.
    [TestCase("Claim 150 Coins", ExpectedResult = false)]
    [TestCase("Spin · 1 token", ExpectedResult = false)]
    [TestCase("Play tournament match again", ExpectedResult = false)]
    public bool TheRuleDrawsItsBoundaryWhereTheScreenNeedsIt(string label)
    {
        return ButtonLabels.IsShort(label);
    }

    [Test]
    public void EveryCtaTheEventsHubChoosesIsShort()
    {
        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            MetaSnapshot snapshot = MetaFixtures.Build(scenario, TimeSpan.Zero);
            foreach (MetaFeature feature in Enum.GetValues<MetaFeature>())
            {
                if (snapshot.ViewOf(feature) is not IFeatureView view)
                {
                    continue;
                }

                string label = ButtonLabels.For(view);
                Assert.That(ButtonLabels.IsShort(label), Is.True,
                    $"{scenario}/{feature}: the hub's chooser produced \"{label}\" — a hub CTA comes from " +
                    "ButtonLabels, never from a literal written beside the call");
            }
        }
    }
}
