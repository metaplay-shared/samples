using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The one interactive resolution. Look at the top of your deck and keep part of it cannot decide at cast
    /// time, because the information does not exist until the effect reveals it, and it cannot be made by
    /// rule, because choosing is the card's whole point — so this one primitive pauses the queue for its
    /// owner's choice (<c>Docs/effects.md</c>).
    /// </summary>
    [TestFixture]
    public class EffectChoiceTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        /// <summary> Seat 0 holding Pebble Collector over a deck whose top three are named and distinct. </summary>
        Scenario Peeking()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector");   // Hello: look at 3, keep 1
            scenario.Seat(0).Deck("MeadowMouse");       // cost 1
            scenario.Seat(0).Deck("MooseWanderer");     // cost 6 — the deterministic default
            scenario.Seat(0).Deck("BusyBeaver");        // cost 2
            scenario.Seat(0).DeckOf(4, "GreyOwl");
            scenario.Seat(0).Mana(9, 9);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            return scenario.OnTurn(0);
        }

        [Test]
        public void PeekHoldsTheResolutionForItsOwner()
        {
            MatchEngine        engine    = Peeking().Build();

            engine.Play(0, engine.SecretHand(0)[0], EffectTargetRef.None);

            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingEffectChoice));
            Assert.That(engine.Pending.Seat, Is.EqualTo(0));
            Assert.That(engine.Revealed(engine.Rules.PendingChoice.Seat).Count, Is.EqualTo(3));
            Assert.That(engine.Rules.PendingChoice.KeepCount, Is.EqualTo(1));

            EffectChoiceRequestedEvent requested = engine.LastEventOf<EffectChoiceRequestedEvent>();
            Assert.That(requested, Is.Not.Null);
            Assert.That(requested.RevealedCount, Is.EqualTo(3), "the board publicly shows a resolution held on that seat, and nothing more");
        }

        [Test]
        public void AnAnswerNamingTheHeldChoiceIsAccepted()
        {
            MatchEngine engine = Peeking().Build();
            engine.Play(0, engine.SecretHand(0)[0]);

            int            before = engine.ActionCount;
            CardInstanceId keep   = engine.Revealed(engine.Rules.PendingChoice.Seat)[0];

            Assert.That(engine.ChooseKept(0, keep), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.ActionCount, Is.EqualTo(before + 1), "the answer is a seat action");
            Assert.That(engine.Rules.PendingChoice, Is.Null);
        }

        [Test]
        public void AnAnswerToAnEarlierRevealIsRefused()
        {
            Scenario scenario = Peeking();
            scenario.Seat(0).Hand("PebbleCollector");
            MatchEngine engine = scenario.Build();

            engine.Play(0, engine.SecretHand(0)[0]);
            int first = engine.ChoiceId;
            Assert.That(engine.Submit(0, new EffectChoiceIntent(first, new List<int> { 0 })), Is.EqualTo(MatchIntentResults.Success));

            // The second reveal holds different cards at the same indices; an answer to the first must not
            // be applied to it.
            engine.Play(0, engine.SecretHand(0)[0]);
            Assert.That(engine.ChoiceId, Is.Not.EqualTo(first), "every choice gets its own id");

            Assert.That(engine.Submit(0, new EffectChoiceIntent(first, new List<int> { 0 })), Is.EqualTo(MatchIntentResults.StaleChoice));
            Assert.That(engine.Rules.PendingChoice, Is.Not.Null, "the held choice is still waiting for its own answer");
            Assert.That(engine.Submit(0, new EffectChoiceIntent(engine.ChoiceId, new List<int> { 0 })), Is.EqualTo(MatchIntentResults.Success));
        }

        [Test]
        public void TheOtherSeatCannotAnswerAnotherSeatsChoice()
        {
            MatchEngine engine = Peeking().Build();
            engine.Play(0, engine.SecretHand(0)[0]);

            Assert.That(engine.ChooseKept(1, engine.Revealed(engine.Rules.PendingChoice.Seat)[0]), Is.EqualTo(MatchIntentResults.NotYourTurn));
        }

        [Test]
        public void KeepingMoreThanAllowedIsRefused()
        {
            MatchEngine engine = Peeking().Build();
            engine.Play(0, engine.SecretHand(0)[0]);

            PendingEffectChoice pending = engine.Rules.PendingChoice;
            Assert.That(engine.ChooseKept(0, engine.Revealed(pending.Seat)[0], engine.Revealed(pending.Seat)[1]), Is.EqualTo(MatchIntentResults.InvalidChoice));
        }

        [Test]
        public void KeepingSomethingNotRevealedCannotBeAskedFor()
        {
            // An answer names indices in the reveal, not cards, so "keep a card I was never shown" has no
            // spelling: there is no index that means it. What was once a refusal is now unrepresentable,
            // which is the stronger of the two — and it is why the answer needs no secret to validate.
            MatchEngine engine = Peeking().Build();
            engine.Play(0, engine.SecretHand(0)[0]);

            PendingEffectChoice pending = engine.Rules.PendingChoice;

            // The reachable illegal answers are an index outside the reveal, and the same one twice.
            Assert.That(engine.Submit(0, new EffectChoiceIntent(engine.ChoiceId, new List<int> { pending.RevealedCount })),
                Is.EqualTo(MatchIntentResults.InvalidChoice), "an index past the end of the reveal");

            Assert.That(engine.Submit(0, new EffectChoiceIntent(engine.ChoiceId, new List<int> { -1 })),
                Is.EqualTo(MatchIntentResults.InvalidChoice), "an index before the start of it");

            Assert.That(engine.Submit(0, new EffectChoiceIntent(engine.ChoiceId, new List<int> { 0, 0 })),
                Is.EqualTo(MatchIntentResults.InvalidChoice), "the same index twice");
        }

        [Test]
        public void KeptCardsReachTheHandAndTheRestGoToTheBottom()
        {
            MatchEngine engine = Peeking().Build();
            engine.Play(0, engine.SecretHand(0)[0]);

            List<CardInstanceId> revealed = new List<CardInstanceId>(engine.Revealed(engine.Rules.PendingChoice.Seat));
            int                  deckSize = engine.SecretDeck(0).Count;

            engine.ChooseKept(0, revealed[1]);

            SeatState state = engine.Rules.Seat(0);
            Assert.That(engine.SecretHand(0), Does.Contain(revealed[1]));
            Assert.That(engine.SecretDeck(0).Count, Is.EqualTo(deckSize - 1));
            Assert.That(engine.SecretDeck(0)[engine.SecretDeck(0).Count - 2], Is.EqualTo(revealed[0]), "the rest go to the bottom, in the order they were revealed");
            Assert.That(engine.SecretDeck(0)[engine.SecretDeck(0).Count - 1], Is.EqualTo(revealed[2]));
        }

        [Test]
        public void TheQueueResumesAfterTheChoice()
        {

            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector");
            scenario.Seat(0).DeckOf(6, "GreyOwl");
            scenario.Seat(0).Mana(9, 9);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, engine.SecretHand(0)[0], EffectTargetRef.None);
            engine.Clear();
            engine.ChooseKept(0, engine.Revealed(engine.Rules.PendingChoice.Seat)[0]);

            Assert.That(engine.HasEventOf<EffectChoiceResolvedEvent>(), Is.True);
            Assert.That(engine.Rules.EffectQueue.Count, Is.EqualTo(0), "the queue was held, not abandoned");
            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingIntent));
        }

        [Test]
        public void LapsedChoiceDeadlineKeepsTheHighestCostCard()
        {
            MatchEngine        engine    = Peeking().Build();

            engine.Play(0, engine.SecretHand(0)[0]);
            engine.ExpireDeadline(MatchDeadlineKind.EffectChoice, null);

            Assert.That(engine.Rules.PendingChoice, Is.Null);
            Assert.That(engine.LastEventOf<EffectChoiceResolvedEvent>().WasDefaulted, Is.True);

            List<CardId> hand = new List<CardId>();
            foreach (CardInstanceId id in engine.SecretHand(0))
                hand.Add(CardLookup.CardId(engine.Model, id));

            Assert.That(hand, Does.Contain(CardId.FromString("MooseWanderer")), "keep the highest-cost card");
        }

        [Test]
        public void DefaultChoiceBreaksTiesOnCanonicalCardOrder()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector");
            // Three cards of the same cost: the tie-break is canonical card order, which is the same tie-break
            // every deterministic choice in the game uses.
            scenario.Seat(0).Deck("WiseTortoise"); // cost 3
            scenario.Seat(0).Deck("PondFrog");     // cost 3
            scenario.Seat(0).Deck("FlameDancer");  // cost 3
            scenario.Seat(0).DeckOf(4, "GreyOwl");
            scenario.Seat(0).Mana(9, 9);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, engine.SecretHand(0)[0]);
            engine.ExpireDeadline(MatchDeadlineKind.EffectChoice, null);

            List<CardId> hand = new List<CardId>();
            foreach (CardInstanceId id in engine.SecretHand(0))
                hand.Add(CardLookup.CardId(engine.Model, id));

            Assert.That(hand, Does.Contain(CardId.FromString("FlameDancer")), "ordinal card order: FlameDancer before PondFrog before WiseTortoise");
        }

        [Test]
        public void PlayOutRemainderResolvesAHeldChoice()
        {
            MatchEngine engine = Peeking().Build();
            engine.Play(0, engine.SecretHand(0)[0]);
            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingEffectChoice));

            engine.PlayOutRemainder(TestBots.Strongest);

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete), "the pause must never block a play-out");
        }

        [Test]
        public void RevealedCardsAreOnTheOwnersPrivateViewOnly()
        {
            MatchEngine engine = Peeking().Build();
            engine.Play(0, engine.SecretHand(0)[0]);

            PendingChoiceView owner = engine.BuildPeekView(0);
            Assert.That(owner, Is.Not.Null);
            Assert.That(owner.Revealed.Count, Is.EqualTo(3));

            Assert.That(engine.BuildPeekView(1), Is.Null, "the other seat learns only that a resolution is held");
            Assert.That((engine.Rules.PendingChoice != null ? engine.Rules.PendingChoice.Seat : MatchSeats.None), Is.EqualTo(0));
        }

        [Test]
        public void ChoiceIntentWithNothingPendingIsRefused()
        {
            MatchEngine engine = Peeking().Build();

            Assert.That(engine.ChooseKept(0), Is.EqualTo(MatchIntentResults.NoChoicePending));
        }

        [Test]
        public void APeekDoesNotHandTheSeatAFreshTurnDeadline()
        {
            // The pause is the seat's own thinking time. Replacing the turn deadline and re-arming a fresh one
            // afterwards would hand out a whole second turn — repeatable by bouncing the peeker and casting
            // it again.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector");
            scenario.Seat(0).Mana(9, 9).DeckOf(8, "GreyOwl");
            scenario.Seat(1).DeckOf(8);
            MatchEngine engine = scenario.OnTurn(0).Build(MatchTimings.Default);

            engine.PassTurn();
            engine.PassTurn();

            MetaTime? turnDeadline = engine.Pending.DeadlineAt;
            Assert.That(turnDeadline, Is.Not.Null);

            engine.Play(0, engine.SecretHand(0)[0]);
            Assert.That(engine.Pending.DeadlineKind, Is.EqualTo(MatchDeadlineKind.EffectChoice));

            engine.ChooseKept(0, engine.Revealed(engine.Rules.PendingChoice.Seat)[0]);

            Assert.That(engine.Pending.DeadlineKind, Is.EqualTo(MatchDeadlineKind.Turn));
            Assert.That(engine.Pending.DeadlineAt, Is.EqualTo(turnDeadline), "the very stamp the pause displaced");
            Assert.That(engine.ReserveRemaining(0), Is.EqualTo(MatchTimings.Default.TurnReserveBank), "and no reserve was spent");
        }

        [Test]
        public void AZeroChoiceDeadlineInheritsTheTurnDeadline()
        {
            // A host that arms turn deadlines but configured no choice deadline must not end up holding a
            // resolution nothing can ever expire.
            MatchTimings noChoiceDeadline = new MatchTimings(
                mulliganDeadline:     MetaDuration.FromSeconds(30),
                turnDeadline:         MetaDuration.FromSeconds(60),
                turnReserveBank:      MetaDuration.Zero,
                turnReserveExtension: MetaDuration.Zero,
                effectChoiceDeadline: MetaDuration.Zero);

            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector");
            scenario.Seat(0).Mana(9, 9).DeckOf(8, "GreyOwl");
            scenario.Seat(1).DeckOf(8);
            MatchEngine engine = scenario.OnTurn(0).Build(noChoiceDeadline);

            engine.PassTurn();
            engine.PassTurn();
            MetaTime? turnDeadline = engine.Pending.DeadlineAt;

            engine.Play(0, engine.SecretHand(0)[0]);

            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingEffectChoice));
            Assert.That(engine.Pending.DeadlineAt, Is.EqualTo(turnDeadline), "it inherited the deadline already in force");
            Assert.That(engine.Pending.DeadlineKind, Is.EqualTo(MatchDeadlineKind.EffectChoice));

            // …and that inherited deadline really does unstick the table.
            Assert.That(engine.ExpireDeadline(MatchDeadlineKind.EffectChoice, null),
                Is.EqualTo(MatchDeadlineOutcome.Applied));
            Assert.That(engine.Rules.PendingChoice, Is.Null);
        }

        [Test]
        public void AllZeroTimingsStillHoldNoDeadlineAcrossThePause()
        {
            // The self-play and unit configuration: no deadlines anywhere, and nothing to inherit. There is no
            // host timer to stall.
            MatchEngine engine = Peeking().Build();
            engine.Play(0, engine.SecretHand(0)[0]);

            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingEffectChoice));
            Assert.That(engine.Pending.DeadlineAt, Is.Null);
        }

        [Test]
        public void PeekOnAnEmptyDeckIsANoOpRatherThanAPause()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector");
            scenario.Seat(0).Mana(9, 9);   // and no deck at all
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, engine.SecretHand(0)[0]);

            Assert.That(engine.Rules.PendingChoice, Is.Null);
            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingIntent));
        }
    }
}
