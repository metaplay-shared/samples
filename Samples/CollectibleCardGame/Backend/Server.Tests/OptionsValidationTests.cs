using Game.Server.Match;
using Game.Server.Matchmaking;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// The load-time validation of the match and matchmaking options. Every relationship it checks fails silently
    /// when violated — a table evicted while a seat is still on grace, a table lost before its players reach it, a
    /// retry budget with no room for its last attempt, a search bound that outlasts the table it seats a player at —
    /// so a configuration that breaks one fails loudly at load instead.
    /// </summary>
    [TestFixture]
    public class OptionsValidationTests
    {
        static IEnumerable<TestCaseData> BrokenMatchOptions()
        {
            yield return new TestCaseData(nameof(MatchOptions.ActorLinger), TimeSpan.FromSeconds(5), "DisconnectGrace")
                .SetName("Match: a linger inside the disconnect grace");
            // Clears the grace but not the join window, and loses a formed table before its players arrive.
            yield return new TestCaseData(nameof(MatchOptions.JoinWindow), TimeSpan.FromSeconds(600), "JoinWindow")
                .SetName("Match: a linger inside the join window");
            yield return new TestCaseData(nameof(MatchOptions.ResultDeliveryWindow), TimeSpan.FromSeconds(5), "ResultDeliveryWindow")
                .SetName("Match: a delivery window inside the first retry");
            // Clears the first retry but not the last attempt.
            yield return new TestCaseData(nameof(MatchOptions.ResultDeliveryWindow), TimeSpan.FromSeconds(45), "ResultDeliveryAttempts")
                .SetName("Match: a delivery window inside the last attempt");
            yield return new TestCaseData(nameof(MatchOptions.PresentationAllowance), TimeSpan.FromSeconds(-1), "PresentationAllowance")
                .SetName("Match: a negative presentation allowance");
            // Zero turns the strike count off; a negative count would read as off too and leave an idle seat
            // nobody ever takes over.
            yield return new TestCaseData(nameof(MatchOptions.StrikesBeforeCover), -1, "StrikesBeforeCover")
                .SetName("Match: a negative strike budget");
            // The bot would still draw its own seed, and the game would still vary.
            yield return new TestCaseData(nameof(MatchOptions.FixedDealSeed), 1ul, "together")
                .SetName("Match: a fixed deal seed without a fixed bot seed");
        }

        [TestCaseSource(nameof(BrokenMatchOptions))]
        public void TheMatchOptionsRefuseToLoadWhenAnInvariantIsBroken(string option, object value, string named)
        {
            MatchOptions options = new MatchOptions();
            typeof(MatchOptions).GetProperty(option)!.SetValue(options, value);

            Assert.That(() => options.OnLoadedAsync().GetAwaiter().GetResult(),
                Throws.InvalidOperationException.With.Message.Contains(named));
        }

        [Test]
        public void TheShippedMatchOptionsLoadAndAZeroStrikeBudgetIsSupported()
        {
            Assert.That(() => new MatchOptions().OnLoadedAsync().GetAwaiter().GetResult(), Throws.Nothing);

            MatchOptions countOff = new MatchOptions();
            typeof(MatchOptions).GetProperty(nameof(MatchOptions.StrikesBeforeCover))!.SetValue(countOff, 0);
            Assert.That(() => countOff.OnLoadedAsync().GetAwaiter().GetResult(), Throws.Nothing);
        }

        [Test]
        public void FixedSeedsLoadOnlyInALocalOrDevelopmentEnvironment()
        {
            RuntimeEnvironmentInfo previous       = RuntimeEnvironmentInfo.Instance;
            string                 previousFamily = Environment.GetEnvironmentVariable("METAPLAY_ENVIRONMENT_FAMILY");
            try
            {
                foreach (EnvironmentFamily family in Enum.GetValues<EnvironmentFamily>())
                {
                    Environment.SetEnvironmentVariable("METAPLAY_ENVIRONMENT_FAMILY", family.ToString());
                    RuntimeEnvironmentInfo.Instance = new RuntimeEnvironmentInfo("Test");

                    MatchOptions options = new MatchOptions();
                    typeof(MatchOptions).GetProperty(nameof(MatchOptions.FixedDealSeed))!.SetValue(options, 1ul);
                    typeof(MatchOptions).GetProperty(nameof(MatchOptions.FixedBotSeed))!.SetValue(options, 2ul);

                    if (family == EnvironmentFamily.Local || family == EnvironmentFamily.Development)
                        Assert.That(() => options.OnLoadedAsync().GetAwaiter().GetResult(), Throws.Nothing, family.ToString());
                    else
                        Assert.That(() => options.OnLoadedAsync().GetAwaiter().GetResult(), Throws.InvalidOperationException, family.ToString());
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("METAPLAY_ENVIRONMENT_FAMILY", previousFamily);
                RuntimeEnvironmentInfo.Instance = previous;
            }
        }

        static IEnumerable<TestCaseData> BrokenMatchmakingOptions()
        {
            yield return new TestCaseData(new (string, TimeSpan)[] { (nameof(MatchmakingOptions.SearchingBound), TimeSpan.FromSeconds(10)) }, "SearchingBound")
                .SetName("Matchmaking: a searching bound inside the search it backs");
            yield return new TestCaseData(new (string, TimeSpan)[] { (nameof(MatchmakingOptions.SeatedBound), TimeSpan.FromSeconds(5)) }, "SeatedBound")
                .SetName("Matchmaking: a seated bound inside the formation it backs");
            // Each bound clears its own step, but together they outlast the table's initial wait: the relationship
            // neither options class can see on its own.
            yield return new TestCaseData(
                    new (string, TimeSpan)[]
                    {
                        (nameof(MatchmakingOptions.SearchingBound), TimeSpan.FromSeconds(200)),
                        (nameof(MatchmakingOptions.SeatedBound), TimeSpan.FromSeconds(60)),
                    },
                    "ActorLinger")
                .SetName("Matchmaking: both bounds together outlast the match linger");
            yield return new TestCaseData(new (string, TimeSpan)[] { (nameof(MatchmakingOptions.FillWait), TimeSpan.FromSeconds(-1)) }, "FillWait")
                .SetName("Matchmaking: a negative fill wait");
        }

        [TestCaseSource(nameof(BrokenMatchmakingOptions))]
        public void TheMatchmakingOptionsNameTheRelationshipTheyBreak((string Option, TimeSpan Value)[] changes, string named)
        {
            MatchmakingOptions options = new MatchmakingOptions();
            foreach ((string option, TimeSpan value) in changes)
                typeof(MatchmakingOptions).GetProperty(option)!.SetValue(options, value);

            Assert.That(MatchmakingOptions.DescribeViolation(options, new MatchOptions()), Does.Contain(named));
        }

        [Test]
        public void TheShippedMatchmakingOptionsBreakNoRelationship()
        {
            Assert.That(MatchmakingOptions.DescribeViolation(new MatchmakingOptions(), new MatchOptions()), Is.Null);
        }
    }
}
