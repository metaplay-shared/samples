using Game.Server.Match;
using Game.Server.Matchmaking;
using NUnit.Framework;
using System;

namespace Game.Server.Tests
{
    /// <summary>
    /// The bound ordering <c>matchmaking.md</c> states — each bound longer than the whole of the step it backs
    /// — plus the one relationship that spans two options classes. Every one of them fails silently when
    /// violated, which is what earns it a load failure and a test against the shipped numbers.
    /// </summary>
    [TestFixture]
    public class MatchmakingOptionsInvariantTests
    {
        static MatchmakingOptions Matchmaking => new MatchmakingOptions();

        static MatchOptions Match => new MatchOptions();

        [Test]
        public void TheShippedNumbersAreTheDesignsOwn()
        {
            MatchmakingOptions options = Matchmaking;

            // The fill wait is the number the whole design rests on: it is the bound on a ranked tap's worst
            // case, and it is what makes "the queue always produces a game" a promise rather than a hope.
            Assert.That(options.FillWait, Is.EqualTo(TimeSpan.FromSeconds(45)));
            Assert.That(options.SeatAskTimeout, Is.LessThan(options.FillWait));
        }

        [Test]
        public void TheSearchingBoundOutlastsTheSearchItBacks()
        {
            // An actor that gave up on a search the matchmaker still went on to honour would tell a player the
            // search failed and then pull them into a match a moment later.
            MatchmakingOptions options = Matchmaking;

            Assert.That(options.SearchingBound, Is.GreaterThan(options.FillWait + options.SeatAskTimeout));
        }

        [Test]
        public void TheSeatedBoundOutlastsTheFormationItBacks()
        {
            MatchmakingOptions options = Matchmaking;

            Assert.That(options.SeatedBound, Is.GreaterThan(options.SeatAskTimeout + options.MintTimeout));
        }

        [Test]
        public void BothBoundsTogetherStayInsideTheTablesOwnInitialWait()
        {
            // The linger is also how long a formed table waits for its first subscriber, so a search whose
            // bounds outlast it could seat a player at a table that has already been lost. This is the
            // relationship neither options class can see on its own.
            Assert.That(Matchmaking.SearchingBound + Matchmaking.SeatedBound, Is.LessThan(Match.ActorLinger));
            Assert.That(MatchmakingOptions.DescribeViolation(Matchmaking, Match), Is.Null);
        }

        [Test]
        public void EachRelationshipIsRefusedWhenBroken()
        {
            MatchmakingOptions tightSearch = Matchmaking;
            SetOption(tightSearch, nameof(MatchmakingOptions.SearchingBound), TimeSpan.FromSeconds(10));
            Assert.That(MatchmakingOptions.DescribeViolation(tightSearch, Match), Does.Contain("SearchingBound"));

            MatchmakingOptions tightSeated = Matchmaking;
            SetOption(tightSeated, nameof(MatchmakingOptions.SeatedBound), TimeSpan.FromSeconds(5));
            Assert.That(MatchmakingOptions.DescribeViolation(tightSeated, Match), Does.Contain("SeatedBound"));

            // The cross-options one: bounds that individually clear their own step but together outlast the
            // table's initial wait. Nothing inside either options class can catch this.
            MatchmakingOptions longBounds = Matchmaking;
            SetOption(longBounds, nameof(MatchmakingOptions.SearchingBound), TimeSpan.FromSeconds(200));
            SetOption(longBounds, nameof(MatchmakingOptions.SeatedBound), TimeSpan.FromSeconds(60));
            Assert.That(MatchmakingOptions.DescribeViolation(longBounds, Match), Does.Contain("ActorLinger"));

            MatchmakingOptions negativeFill = Matchmaking;
            SetOption(negativeFill, nameof(MatchmakingOptions.FillWait), TimeSpan.FromSeconds(-1));
            Assert.That(MatchmakingOptions.DescribeViolation(negativeFill, Match), Does.Contain("FillWait"));
        }

        [Test]
        public void TheEndToEndProfilesFillWaitStillClearsEveryBound()
        {
            // Options.e2e.yaml shortens the fill wait so the solo case does not cost 45 s of suite time per
            // run. A shortened wait only makes the bounds roomier, and this is where that stays true if the
            // profile's number ever moves.
            MatchmakingOptions e2e = Matchmaking;
            SetOption(e2e, nameof(MatchmakingOptions.FillWait), TimeSpan.FromSeconds(8));

            Assert.That(MatchmakingOptions.DescribeViolation(e2e, Match), Is.Null);
        }

        [Test]
        public void TheScheduleCarriesTheDesignsBandsAndTheConfiguredWait()
        {
            MatchmakingBandSchedule schedule = Matchmaking.ToSchedule();

            Assert.That(schedule.Steps, Is.SameAs(MatchmakingPolicy.DesignSteps));
            Assert.That(schedule.FillWait.Milliseconds, Is.EqualTo(45_000));
        }

        /// <summary>
        /// Runtime option setters are private, as they are for every options class: the binder writes them by
        /// reflection and so does this.
        /// </summary>
        static void SetOption(MatchmakingOptions options, string name, TimeSpan value)
            => typeof(MatchmakingOptions).GetProperty(name)!.SetValue(options, value);
    }
}
