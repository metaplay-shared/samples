using Metaplay.Core;
using Metaplay.Core.Model;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The follower mirror's teeth. Each case plants a deliberate break and asserts the check catches it —
    /// because a comparison whose positive case is twenty thousand green games says nothing until somebody
    /// shows it can go red.
    /// <para>
    /// <b>Three of the five are broken actions</b> (<see cref="LeakyPlayCard"/>, <see cref="RngPlayCard"/>,
    /// <see cref="ChattyPlayCard"/>). The other two plant elsewhere for a reason given at each case, and one
    /// of them — NC4 — is caught by the invariant catalog rather than by the mirror, which is the point of
    /// it: the two checks cover different halves.
    /// </para>
    /// <para>
    /// Every plant is run at <b>both</b> zero and shipped timings. A control that only fires at one of them
    /// is a control that a change of host configuration can switch off, and this fixture is the reason to
    /// believe the twenty thousand games mean something.
    /// </para>
    /// <para>
    /// Two of these are not the plants the spec sketched, and the reason is that those two could not fail.
    /// Both are noted at the case that replaced them: the mirror runs the <em>same action</em> on both sides,
    /// so a rule body that is simply wrong — a hard-coded duration, a forgotten decrement — is wrong
    /// identically on both and the comparison sees nothing. What the mirror can see is a divergence between
    /// the two sides, and each control here produces one.
    /// </para>
    /// </summary>
    [TestFixture]
    public class FollowerMirrorNegativeControlTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        static readonly MatchTimings[] BothTimings = { MatchTimings.Instant, MatchTimings.Default };

        /// <summary> A game at the mulligan, with a mirror already watching. </summary>
        MatchEngine Dealt(MatchTimings timings, out FollowerMirror mirror)
        {
            MatchEngine engine = MatchEngine.Create(
                new MatchSetup(9090, Config, timings, TestDecks.Standard(Config), TestDecks.Alternate(Config)));

            mirror = FollowerMirror.Of(engine.Model, Config);
            return engine;
        }

        /// <summary> A game in the playing phase with a card in hand and mana to spend, mirror watching. </summary>
        MatchEngine Playing(MatchTimings timings, out FollowerMirror mirror, out CardInstanceId card)
        {
            Scenario scenario = new Scenario(Config);
            card = scenario.Seat(0).Hand("PondFrog");

            // Two more cards left in hand after the play, deliberately: a plant that writes the hand count
            // from the secret is invisible when the right answer happens to be zero, and a control that
            // cannot differ is not a control.
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Hand("BusyBeaver");
            scenario.Seat(0).Mana(9, 9).DeckOf(8);
            scenario.Seat(1).DeckOf(8);

            MatchEngine engine = scenario.OnTurn(0).Build(timings);
            mirror = FollowerMirror.Of(engine.Model, Config);
            return engine;
        }

        /// <summary>
        /// Run one action on both sides and return the divergence, or null when there was none.
        /// <para>
        /// The plants are built complete, the way MatchIntent.Prepare builds a real one: an action reaches a
        /// follower with its payload already finished, so a plant missing its reveal would diverge on that
        /// rather than on what it plants.
        /// </para>
        /// </summary>
        static string Diverged(FollowerMirror mirror, MatchEngine engine, MatchAction action)
        {
            int historyBefore = engine.Model.History.Count;
            action.InvokeExecute(engine.Model, commit: true);
            List<MatchEvent> events = engine.Model.History.GetRange(historyBefore, engine.Model.History.Count - historyBefore);

            try
            {
                mirror.Apply(action, engine.Model, events);
                return null;
            }
            catch (FollowerDivergence divergence)
            {
                return divergence.Message;
            }
        }

        // ---------------------------------------------------------------- NC1: a secret read feeding a public write

        [Test]
        public void NC1_APublicCountWrittenFromTheSecretHandIsCaught()
        {
            // The central prohibition — no public value written from the secret — and the mistake this
            // design invites most: `Hand.Count` is a
            // natural thing to write and it no longer exists as a public number, so reaching for the secret's
            // length is one keystroke away.
            foreach (MatchTimings timings in BothTimings)
            {
                MatchEngine engine = Playing(timings, out FollowerMirror mirror, out CardInstanceId card);
                HandCard    held   = SecretOps.HandCardOf(engine.Model, card).Value;

                string diverged = Diverged(mirror, engine,
                    new LeakyPlayCard(0, card, held.Card, held.Rank, EffectTargetRef.None));

                Assert.That(diverged, Is.Not.Null, $"at {(timings.TurnDeadline == MetaDuration.Zero ? "zero" : "shipped")} timings: a hand count read off the secret must be caught");
                Assert.That(diverged, Does.Contain("HandCount"), "and the diff must name the member");
            }
        }

        // ---------------------------------------------------------------- NC2: the seeded stream

        [Test]
        public void NC2_APublicValueDrawnFromTheSeededStreamIsCaught()
        {
            // The one hole the design cannot survive, generalized from the memo's random-target walk: a
            // follower has no stream, so a public value that came out of one cannot be reproduced. The plant
            // dereferences the stream directly, which is what a rule body reaching for randomness would do.
            foreach (MatchTimings timings in BothTimings)
            {
                MatchEngine engine = Playing(timings, out FollowerMirror mirror, out CardInstanceId card);
                HandCard    held   = SecretOps.HandCardOf(engine.Model, card).Value;

                string diverged = Diverged(mirror, engine,
                    new RngPlayCard(0, card, held.Card, held.Rank, EffectTargetRef.None));

                Assert.That(diverged, Is.Not.Null, "a value drawn from the seeded stream must be caught");
            }
        }

        // ---------------------------------------------------------------- NC3: the event stream's own axis

        [Test]
        public void NC3_AListenerToldSomethingTheHistoryWasNotIsCaught()
        {
            // This is the control that earns the mirror's second axis. Every event a rule body records goes
            // on the model's own History, which is public and therefore checksummed — so an event with the
            // wrong CONTENT is caught by the checksum, and an event-comparison that only saw those would be
            // redundant.
            //
            // What it is not redundant with is this: a body that notifies the client listener without
            // recording, whose numbers then differ per side. The checksummed state is byte-identical and only
            // the recorded streams disagree, which is precisely what the second axis is for.
            foreach (MatchTimings timings in BothTimings)
            {
                MatchEngine engine = Playing(timings, out FollowerMirror mirror, out CardInstanceId card);
                HandCard    held   = SecretOps.HandCardOf(engine.Model, card).Value;

                string diverged = Diverged(mirror, engine,
                    new ChattyPlayCard(0, card, held.Card, held.Rank, EffectTargetRef.None));

                Assert.That(diverged, Is.Not.Null, "a listener told what the history was not must be caught");
                Assert.That(diverged, Does.Contain("recorded"), "and by the event axis rather than by the checksum");
            }
        }

        // ---------------------------------------------------------------- NC4: a stored count that stopped being kept

        [Test]
        public void NC4_AStoredCountThatStoppedBeingMaintainedIsCaughtByTheCatalog()
        {
            // "Move the card in the secret and forget DeckCount--" cannot be caught by the mirror: the mirror runs the same body on both sides, so a
            // forgotten decrement is forgotten identically and the two public halves agree perfectly. The
            // model is fine; the *secret* has drifted from the count that stands for it.
            //
            // That mistake has its own check, and it is the one the whole stored-count decision needs: C1b,
            // walked at every step of every game. So the control belongs to the catalog rather than to the
            // mirror, and here it is — which also demonstrates that the two checks cover different halves.
            foreach (MatchTimings timings in BothTimings)
            {
                MatchEngine engine = Playing(timings, out FollowerMirror _, out CardInstanceId _);

                InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.Conservation);
                walk.Capture(engine);
                Assert.That(walk.Check(engine, new List<MatchEvent>()), Is.Null, $"the position starts sound at {timings.TurnDeadline}");

                // The plant: a card leaves the secret deck and the count is not told.
                engine.Model.SecretSeat(0).Deck.RemoveAt(0);

                walk.Capture(engine);
                Assert.That(walk.Check(engine, new List<MatchEvent>()), Does.StartWith("C1b"),
                    $"a count that stopped standing for its list is C1b's to catch, at {timings.TurnDeadline} as much as at zero");
            }
        }

        // ---------------------------------------------------------------- NC5: durations that did not travel

        [Test]
        public void NC5_AFollowerWhoseDurationsDidNotTravelIsCaught()
        {
            // An action arming a deadline from a hard-coded duration cannot be caught by the mirror either, for
            // the same reason as NC4: both sides run the plant, so both hard-code the same number and agree.
            //
            // What such a plant is about is that MatchTimings has to be a public model member, because a
            // follower derives its own stamps from it. So the control tests that decision
            // directly: give the follower different durations, as it would have if they had not been
            // replicated, and end a turn. Every derived stamp then differs.
            // Both directions, because "the durations did not travel" is a disagreement rather than a
            // shortage: a follower left at zero while the table is paced derives no stamps where the
            // authority derives some, and a follower left paced while the table is instant derives stamps
            // where the authority derives none. Either way the two models part company.
            foreach (MatchTimings timings in BothTimings)
            {
                MatchEngine engine = Playing(timings, out FollowerMirror _, out CardInstanceId _);

                FollowerMirror blind = FollowerMirror.Of(engine.Model, Config);
                blind.MakeTimingsDisagree(timings.TurnDeadline > MetaDuration.Zero ? MatchTimings.Instant : MatchTimings.Default);

                string diverged = Diverged(blind, engine, new MatchEndTurn(0));

                Assert.That(diverged, Is.Not.Null, $"a follower with different durations must be caught at {timings.TurnDeadline}");
            }
        }

        // ---------------------------------------------------------------- a follower off the model's clock

        [Test]
        public void AFollowerThatMissedATickIsCaught()
        {
            // Every stamp a rule arms is the model's clock plus a duration, so a follower one tick behind
            // arms every stamp one tick early. The clock is part of the checksummed model, so the comparison
            // sees the missed tick itself, before any stamp differs.
            MatchEngine engine = Playing(MatchTimings.Default, out FollowerMirror mirror, out CardInstanceId _);

            engine.Model.Tick(checksumCtx: null);

            string diverged = Diverged(mirror, engine, new MatchEndTurn(0));

            Assert.That(diverged, Is.Not.Null, "a follower whose clock lags the authority's must be caught");
        }

        // ---------------------------------------------------------------- the baseline

        [Test]
        public void TheUnbrokenActionDivergesFromNothing()
        {
            // The pairing every control needs: the same position, the same action, unbroken. Without it a
            // control could be green because the comparison is broken rather than because the plant is.
            foreach (MatchTimings timings in BothTimings)
            {
                MatchEngine engine = Playing(timings, out FollowerMirror mirror, out CardInstanceId card);
                HandCard    held   = SecretOps.HandCardOf(engine.Model, card).Value;

                string diverged = Diverged(mirror, engine,
                    new MatchPlayCard(0, card, held.Card, held.Rank, EffectTargetRef.None));

                Assert.That(diverged, Is.Null, "the real action must agree on both sides");
            }

            // And through a whole game, at the mulligan and beyond, which is what the self-play suites run.
            MatchEngine dealt = Dealt(MatchTimings.Instant, out FollowerMirror dealtMirror);
            dealt.Mirror = dealtMirror;
            Assert.DoesNotThrow(() => dealt.PlayOutRemainder(TestBots.Strongest),
                "a whole game must be public-equal on a follower");
        }
    }

    // ------------------------------------------------------------------------ the plants
    //
    // Each is MatchPlayCard with exactly one break. They live in the test assembly and take codes well
    // outside the game's 5100-5149 match block, so the reflection guards over the game assembly never see
    // them — and a plant that leaked into production would be a code nobody allocated.

    /// <summary> NC1: the hand count is read off the secret instead of being decremented. </summary>
    [ModelAction(5990)]
    public class LeakyPlayCard : MatchAction
    {
        public int             Seat         { get; private set; }
        public CardInstanceId  Card         { get; private set; }
        public CardId          RevealedCard { get; private set; }
        public int             RevealedRank { get; private set; }
        public EffectTargetRef Target       { get; private set; }

        public LeakyPlayCard() { }

        public LeakyPlayCard(int seat, CardInstanceId card, CardId revealedCard, int revealedRank, EffectTargetRef target)
        {
            Seat = seat; Card = card; RevealedCard = revealedCard; RevealedRank = revealedRank; Target = target;
        }


        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Rules.CountAction(actsOnTurn: true);
            TurnRules.PlayCard(match, Seat, Card, RevealedCard, RevealedRank, Target);

            // The break. On the authority this is the right number; on a follower the secret is default and
            // the hand count becomes zero.
            SeatState state = match.Rules.Seat(Seat);
            state.SetCounts((match.SecretSeat(Seat)?.Hand.Count ?? 0), state.DeckCount);

            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary> NC2: a public stat gains a value drawn from the match's seeded stream. </summary>
    [ModelAction(5991)]
    public class RngPlayCard : MatchAction
    {
        public int             Seat         { get; private set; }
        public CardInstanceId  Card         { get; private set; }
        public CardId          RevealedCard { get; private set; }
        public int             RevealedRank { get; private set; }
        public EffectTargetRef Target       { get; private set; }

        public RngPlayCard() { }

        public RngPlayCard(int seat, CardInstanceId card, CardId revealedCard, int revealedRank, EffectTargetRef target)
        {
            Seat = seat; Card = card; RevealedCard = revealedCard; RevealedRank = revealedRank; Target = target;
        }


        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Rules.CountAction(actsOnTurn: true);
            TurnRules.PlayCard(match, Seat, Card, RevealedCard, RevealedRank, Target);

            // The break. A follower has no stream at all, so this throws there — which the mirror reports as
            // the divergence it is rather than swallowing.
            BoardCritter played = match.Rules.Seat(Seat).FindCritter(Card);
            if (played != null)
                ResolutionRules.BuffCritter(match, played.Id, SecretOps.NextInt(match, 4), 0);

            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary> NC3: the client listener is told numbers the recorded history does not carry. </summary>
    [ModelAction(5992)]
    public class ChattyPlayCard : MatchAction
    {
        public int             Seat         { get; private set; }
        public CardInstanceId  Card         { get; private set; }
        public CardId          RevealedCard { get; private set; }
        public int             RevealedRank { get; private set; }
        public EffectTargetRef Target       { get; private set; }

        public ChattyPlayCard() { }

        public ChattyPlayCard(int seat, CardInstanceId card, CardId revealedCard, int revealedRank, EffectTargetRef target)
        {
            Seat = seat; Card = card; RevealedCard = revealedCard; RevealedRank = revealedRank; Target = target;
        }


        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Rules.CountAction(actsOnTurn: true);
            TurnRules.PlayCard(match, Seat, Card, RevealedCard, RevealedRank, Target);

            // The break. Notifying the listener without recording leaves the checksummed state identical and
            // only the two recorded streams disagreeing — which is the one thing the checksum cannot see.
            match.ClientListener?.OnMatchEvent(new CardDrawnEvent(Seat, (match.SecretSeat(Seat)?.Hand.Count ?? 0), (match.SecretSeat(Seat)?.Deck.Count ?? 0)));

            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }
}
