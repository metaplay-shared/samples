using Game.Server.Match;
using NUnit.Framework;
using System;

namespace Game.Server.Tests
{
    /// <summary>
    /// The bounds and relationships between match options that are invariants rather than defaults. Every one
    /// of them fails <em>silently</em> when violated, which is what earns it a startup throw and a test
    /// against the shipped numbers: a table evicted while a seat is still on grace, a table lost before its
    /// players can reach it, a retry budget with no room to spend a single retry, or a strike count that reads
    /// as switched off.
    /// </summary>
    [TestFixture]
    public class MatchOptionsInvariantTests
    {
        MatchOptions Options => new MatchOptions();

        [Test]
        public void PresentationDoesNotHoldRulesOrRemoveBotThinking()
        {
            MatchOptions options = Options;
            Game.Logic.MatchTimings timings = options.ToMatchTimings();
            Assert.That(options.BotActionDelay, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(options.BotActionDelayJitter, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(timings.TurnDeadline.Milliseconds, Is.EqualTo(63000));
            Assert.That(timings.MulliganDeadline.Milliseconds, Is.EqualTo(33000));
            Assert.That(timings.EffectChoiceDeadline.Milliseconds, Is.EqualTo(23000));

            SetOption(options, nameof(MatchOptions.TurnDeadline), TimeSpan.Zero);
            Assert.That(options.ToMatchTimings().TurnDeadline.Milliseconds, Is.Zero);
            SetOption(options, nameof(MatchOptions.PresentationAllowance), TimeSpan.FromSeconds(-1));
            Assert.That(() => options.OnLoadedAsync().GetAwaiter().GetResult(), Throws.InvalidOperationException);
        }

        [Test]
        public void TheActorLingerExceedsTheGraceItCanArm()
        {
            // A shorter linger evicts the actor mid-grace, the grace never lapses, and the play-out that makes
            // leaving cost what staying would never happens.
            MatchOptions options = Options;
            Assert.That(options.ActorLinger, Is.GreaterThan(options.DisconnectGrace));
        }

        [Test]
        public void TheActorLingerExceedsTheJoinWindow()
        {
            // The linger is also the initial wait for a FIRST subscriber, which the SDK otherwise fixes at
            // 30 s. A table nobody reaches in time is lost outright — it is the only copy of its own game —
            // so this margin is deliberately much larger than the window it has to clear.
            MatchOptions options = Options;
            Assert.That(options.ActorLinger, Is.GreaterThan(options.JoinWindow));
        }

        [Test]
        public void TheStrikeBudgetIsTheOneTheDesignQuotesAndZeroIsSupported()
        {
            // What match.md's timing table quotes: one lapse is being slow, two is being gone. Zero is a
            // supported configuration and means the count is off, so the invariant is a sign check rather than
            // a floor of one — a negative value reads as "off" too, and would quietly leave an idle seat that
            // nobody ever takes over.
            MatchOptions options = Options;
            Assert.That(options.StrikesBeforeCover, Is.EqualTo(2));

            MatchOptions disabled = new MatchOptions();
            SetCount(disabled, nameof(MatchOptions.StrikesBeforeCover), 0);
            Assert.That(() => disabled.OnLoadedAsync().GetAwaiter().GetResult(), Throws.Nothing);

            MatchOptions negative = new MatchOptions();
            SetCount(negative, nameof(MatchOptions.StrikesBeforeCover), -1);
            Assert.That(() => negative.OnLoadedAsync().GetAwaiter().GetResult(), Throws.InvalidOperationException);
        }

        [Test]
        public void TheDeliveryWindowHasRoomForEveryAttemptTheBudgetAllows()
        {
            // The wakelock's lifetime caps the window, so the window has to clear the last attempt rather than
            // the first retry.
            MatchOptions options = Options;

            TimeSpan lastAttemptAt = (options.ResultDeliveryAttempts - 1) * options.ResultRetryInterval + options.TimerPadding;
            Assert.That(options.ResultDeliveryWindow, Is.GreaterThan(lastAttemptAt),
                $"the last of {options.ResultDeliveryAttempts} attempts fires at {lastAttemptAt}");

            Assert.That(options.ResultDeliveryAttempts, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void EveryShippedModelClockDurationIsAWholeNumberOfTicks()
        {
            // Every stamp in the model is the model's clock plus one of these, and the clock moves a tick at a
            // time. A duration off the tick grid would lapse up to a tick late, which is what the one-second
            // rate on MatchModel is chosen against.
            MatchOptions options = Options;
            long         tickMs  = 1000 / new Game.Logic.MatchModel().TicksPerSecond;

            TimeSpan[] stamped =
            {
                options.MulliganDeadline, options.TurnDeadline, options.TurnReserveExtension, options.EffectChoiceDeadline,
                options.PresentationAllowance, options.JoinWindow, options.DisconnectGrace, options.HeistPickDeadline,
            };

            foreach (TimeSpan duration in stamped)
                Assert.That((long)duration.TotalMilliseconds % tickMs, Is.Zero, $"{duration} is not a whole number of model ticks");
        }

        [Test]
        public void TheOptionsRefuseToLoadWhenAnInvariantIsBroken()
        {
            // Each relationship fails silently when violated, so a config that breaks one has to fail loudly
            // at load instead. The validation is what this asserts; the defaults above are what it protects.
            MatchOptions shortLinger = new MatchOptions();
            SetOption(shortLinger, nameof(MatchOptions.ActorLinger), TimeSpan.FromSeconds(5));
            Assert.That(() => shortLinger.OnLoadedAsync().GetAwaiter().GetResult(), Throws.InvalidOperationException);

            // A linger that clears the grace but not the join window: the case a "must exceed every grace"
            // rule would have passed, and the one that loses a formed table before its players arrive.
            MatchOptions tightJoin = new MatchOptions();
            SetOption(tightJoin, nameof(MatchOptions.JoinWindow), TimeSpan.FromSeconds(600));
            Assert.That(() => tightJoin.OnLoadedAsync().GetAwaiter().GetResult(), Throws.InvalidOperationException);

            MatchOptions shortWindow = new MatchOptions();
            SetOption(shortWindow, nameof(MatchOptions.ResultDeliveryWindow), TimeSpan.FromSeconds(5));
            Assert.That(() => shortWindow.OnLoadedAsync().GetAwaiter().GetResult(), Throws.InvalidOperationException);

            // And a window that clears the first retry but not the last attempt — the shape the shipped
            // configuration was in, and the one a weaker "> one interval" invariant admits.
            MatchOptions tightBudget = new MatchOptions();
            SetOption(tightBudget, nameof(MatchOptions.ResultDeliveryWindow), TimeSpan.FromSeconds(45));
            Assert.That(() => tightBudget.OnLoadedAsync().GetAwaiter().GetResult(), Throws.InvalidOperationException);

            // And the shipped defaults load without complaint.
            Assert.That(() => new MatchOptions().OnLoadedAsync().GetAwaiter().GetResult(), Throws.Nothing);
        }

        /// <summary>
        /// Runtime option setters are private, as they are for every options class: the binder writes them by
        /// reflection and so does this.
        /// </summary>
        static void SetOption(MatchOptions options, string name, TimeSpan value)
            => typeof(MatchOptions).GetProperty(name)!.SetValue(options, value);

        /// <summary> The same, for the one count among the durations. </summary>
        static void SetCount(MatchOptions options, string name, int value)
            => typeof(MatchOptions).GetProperty(name)!.SetValue(options, value);
    }
}
