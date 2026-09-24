using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary> The one simultaneous step: both seats act at the same position, once each. </summary>
    [TestFixture]
    public class MulliganTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        MatchEngine Deal(ulong seed)
        {
            MatchSetup setup = new MatchSetup(seed, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            return MatchEngine.Create(setup);
        }

        [Test]
        public void EachSeatMayMulliganOnce()
        {
            MatchEngine engine = Deal(seed: 5);

            Assert.That(engine.Mulligan(0), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Mulligan(1), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing));
        }

        [Test]
        public void SecondMulliganIsRefusedAsAlreadyMulliganed()
        {
            MatchEngine engine = Deal(seed: 5);

            Assert.That(engine.Mulligan(0), Is.EqualTo(MatchIntentResults.Success));
            // Named as a duplicate: the seat's own flag is what refuses it.
            Assert.That(engine.Mulligan(0), Is.EqualTo(MatchIntentResults.AlreadyMulliganed));
        }

        [Test]
        public void ReplacesAnySubsetIncludingEmptyAndWholeHand()
        {
            MatchEngine engine = Deal(seed: 11);
            int         first  = engine.Rules.FirstSeat;

            List<CardInstanceId> whole = new List<CardInstanceId>(engine.SecretHand(first));
            Assert.That(engine.Mulligan(first, whole.ToArray()), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Mulligan(MatchSeats.Other(first)), Is.EqualTo(MatchIntentResults.Success));
        }

        [Test]
        public void ReplacedCardsReturnAndTheSameNumberIsRedrawn()
        {
            MatchEngine        engine    = Deal(seed: 11);
            int                first     = engine.Rules.FirstSeat;

            int handBefore = engine.SecretHand(first).Count;
            int deckBefore = engine.SecretDeck(first).Count;

            List<CardInstanceId> replace = new List<CardInstanceId> { engine.SecretHand(first)[0], engine.SecretHand(first)[1] };

            MulliganResolvedEvent resolved = null;
            engine.Clear();
            engine.Mulligan(first, replace.ToArray());
            engine.Mulligan(MatchSeats.Other(first));

            foreach (MulliganResolvedEvent ev in engine.EventsOf<MulliganResolvedEvent>())
            {
                if (ev.Seat == first)
                    resolved = ev;
            }

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.ReplacedCount, Is.EqualTo(2));
            Assert.That(resolved.HandCount, Is.EqualTo(handBefore));
            Assert.That(resolved.DeckCount, Is.EqualTo(deckBefore));
        }

        [Test]
        public void ARedrawnCardMayBeOneJustReturned()
        {
            // The deck is reshuffled before the redraw, so a returned card is back in the pool it is drawn
            // from. Over a spread of seeds this must actually happen.
            bool sawAReturnedCard = false;

            for (ulong seed = 1; seed <= 40 && !sawAReturnedCard; seed++)
            {
                MatchEngine engine = Deal(seed);
                int         first  = engine.Rules.FirstSeat;

                List<CardInstanceId> whole = new List<CardInstanceId>(engine.SecretHand(first));
                engine.Mulligan(first, whole.ToArray());
                engine.Mulligan(MatchSeats.Other(first));

                foreach (CardInstanceId id in engine.SecretHand(first))
                {
                    if (whole.Contains(id))
                        sawAReturnedCard = true;
                }
            }

            Assert.That(sawAReturnedCard, Is.True, "no seed in 1..40 redrew a returned card, which a reshuffle makes very unlikely");
        }

        [Test]
        public void BothSeatsMayMulliganInEitherOrder()
        {
            MatchEngine engine = Deal(seed: 5);

            Assert.That(engine.Submit(1, new MulliganIntent(new List<CardInstanceId>())), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Submit(0, new MulliganIntent(new List<CardInstanceId>())), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing));
        }

        [Test]
        public void EachSeatsMulliganResolvesWhenSubmitted()
        {
            // The swap happens in the submitting action, so the new hand reaches its owner without waiting for
            // the other seat. The phase stays open until both have answered.
            MatchEngine    engine   = Deal(seed: 2024);
            CardInstanceId replaced = engine.SecretHand(1)[0];

            engine.Clear();
            Assert.That(engine.Mulligan(1, replaced), Is.EqualTo(MatchIntentResults.Success));

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Mulligan), "the other seat has not answered");
            Assert.That(engine.SecretHand(1), Does.Not.Contain(replaced));
            Assert.That(engine.HandChangedInLastCall(1), Is.True, "the owner is told at once, by addressed operations");
            Assert.That(engine.HandChangedInLastCall(0), Is.False);

            List<MulliganResolvedEvent> resolved = engine.EventsOf<MulliganResolvedEvent>();
            Assert.That(resolved.Count, Is.EqualTo(1));
            Assert.That(resolved[0].Seat, Is.EqualTo(1));
            Assert.That(resolved[0].ReplacedCount, Is.EqualTo(1));
        }

        [Test]
        public void TheOrderTheSeatsAnswerInIsReplayedExactly()
        {
            // Arrival order decides which seat's swap consumes the stream first. The seed is secret, so that
            // is no lever, and the same order on the timeline reproduces the same game.
            const ulong Seed = 2024;

            MatchEngine original = Deal(Seed);
            original.Mulligan(1, original.SecretHand(1)[0]);
            original.Mulligan(0, original.SecretHand(0)[0]);

            MatchEngine replay = Deal(Seed);
            replay.Mulligan(1, replay.SecretHand(1)[0]);
            replay.Mulligan(0, replay.SecretHand(0)[0]);

            Assert.That(replay.ComputeRulesHash(), Is.EqualTo(original.ComputeRulesHash()));
            Assert.That(replay.SecretHand(0), Is.EqualTo(original.SecretHand(0)));
            Assert.That(replay.SecretHand(1), Is.EqualTo(original.SecretHand(1)));
        }

        [Test]
        public void LapsedMulliganDeadlineKeepsTheDealtHand()
        {
            MatchEngine          engine = Deal(seed: 77);
            int                  first  = engine.Rules.FirstSeat;
            List<CardInstanceId> dealt  = new List<CardInstanceId>(engine.SecretHand(first));

            engine.ExpireDeadline(MatchDeadlineKind.Mulligan, null);

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing));

            // The seat on turn has since drawn one card; everything else it holds is what it was dealt.
            foreach (CardInstanceId id in dealt)
                Assert.That(engine.SecretHand(first), Does.Contain(id));
        }

        [Test]
        public void OneSeatSubmittingAndOneLapsingResolvesBoth()
        {
            MatchEngine        engine    = Deal(seed: 909);

            List<CardInstanceId> keptByLapser = new List<CardInstanceId>(engine.SecretHand(1));
            CardInstanceId       replaced     = engine.SecretHand(0)[0];

            engine.Clear();
            engine.Mulligan(0, replaced);
            engine.ExpireDeadline(MatchDeadlineKind.Mulligan, null);

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing));
            Assert.That(engine.SecretHand(0), Does.Not.Contain(replaced), "the seat that asked had its card replaced");

            // The seat that never submitted keeps exactly what it was dealt, apart from anything its own turn
            // start drew.
            foreach (CardInstanceId id in keptByLapser)
                Assert.That(engine.SecretHand(1), Does.Contain(id));

            List<MulliganResolvedEvent> resolved = engine.EventsOf<MulliganResolvedEvent>();
            Assert.That(resolved.Count, Is.EqualTo(2), "each seat reports once: the submitter at its submit, the other at the deadline");
            Assert.That(resolved[0].Seat, Is.EqualTo(0));
            Assert.That(resolved[0].ReplacedCount, Is.EqualTo(1));
            Assert.That(resolved[1].Seat, Is.EqualTo(1));
            Assert.That(resolved[1].ReplacedCount, Is.EqualTo(0));
        }

        [Test]
        public void TheAcornIsGrantedWhenTheMulliganResolves()
        {
            MatchEngine engine = Deal(seed: 5);
            int         first  = engine.Rules.FirstSeat;
            int         second = MatchSeats.Other(first);

            // Not while the mulligan is open: the step that could put a card back is asked only about cards
            // that could go back.
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Mulligan));
            Assert.That(AcornIn(engine, first), Is.EqualTo(CardInstanceId.None));
            Assert.That(AcornIn(engine, second), Is.EqualTo(CardInstanceId.None));

            int handBefore = engine.SecretHand(second).Count;

            engine.Mulligan(0);
            engine.Mulligan(1);

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing));
            Assert.That(AcornIn(engine, second), Is.Not.EqualTo(CardInstanceId.None), "the second seat is compensated");
            Assert.That(AcornIn(engine, first), Is.EqualTo(CardInstanceId.None), "the first seat is not");

            // The second seat has not drawn yet — the first seat opens — so its hand grew by exactly the grant.
            Assert.That(engine.SecretHand(second).Count, Is.EqualTo(handBefore + 1));
            Assert.That(engine.Rules.Seat(second).HandCount, Is.EqualTo(handBefore + 1), "and the public count says so too");
        }

        [Test]
        public void TheAcornIsGrantedOnTheLapsedDeadlinePathToo()
        {
            // The resolution is one rule with two callers — the second submission and the lapsed deadline —
            // and a grant that only rode the submission would leave a seat uncompensated for going quiet.
            MatchEngine engine = Deal(seed: 77);
            int         second = MatchSeats.Other(engine.Rules.FirstSeat);

            engine.ExpireDeadline(MatchDeadlineKind.Mulligan, null);

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing));
            Assert.That(AcornIn(engine, second), Is.Not.EqualTo(CardInstanceId.None));
        }

        [Test]
        public void TheAcornIsPublicWhenGrantedAndNotInTheUnseenPool()
        {
            MatchEngine engine = Deal(seed: 12345);
            int         second = MatchSeats.Other(engine.Rules.FirstSeat);
            CardId      acorn  = Config.Global.SecondPlayerBonusCard.Ref.CardId;

            engine.Mulligan(0);
            engine.Mulligan(1);

            CardInstance instance = engine.Rules.Instance(AcornIn(engine, second));

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.IsPublic, Is.True);
            Assert.That(instance.FromStartingDeck, Is.False);
            Assert.That(engine.Rules.Seat(second).UnseenPool, Does.Not.Contain(acorn));
            Assert.That(engine.Rules.Seat(second).UnseenPool, Is.EqualTo(engine.DeriveUnseenPool(second)));
        }

        [Test]
        public void NamingACardNotInTheHandIsRefused()
        {
            MatchEngine engine = Deal(seed: 5);

            Assert.That(engine.Mulligan(0, engine.SecretDeck(0)[0]), Is.EqualTo(MatchIntentResults.NotInYourHand), "a card in its own deck");
            Assert.That(engine.Mulligan(0, engine.SecretHand(1)[0]), Is.EqualTo(MatchIntentResults.NotInYourHand), "the other seat's card");
            Assert.That(engine.Mulligan(0, new CardInstanceId(9999)), Is.EqualTo(MatchIntentResults.NotInYourHand), "no such instance");

            CardInstanceId held = engine.SecretHand(0)[0];
            Assert.That(engine.Mulligan(0, held, held), Is.EqualTo(MatchIntentResults.IllegalTarget), "the same card twice");

            Assert.That(engine.Rules.Seat(0).HasMulliganed, Is.False, "a refusal answers nothing");
        }

        /// <summary> The Acorn instance in a seat's hand, or none. </summary>
        static CardInstanceId AcornIn(MatchEngine engine, int seat)
        {
            CardId acorn = engine.Model.Content.Global.SecondPlayerBonusCard.Ref.CardId;

            foreach (CardInstanceId id in engine.SecretHand(seat))
            {
                if (CardLookup.CardId(engine.Model, id) == acorn)
                    return id;
            }

            return CardInstanceId.None;
        }

        /// <summary>
        /// Every seat a bot plays names only cards the table would accept. A refused mulligan is the silent
        /// kind of wrong, since the seat then never mulligans at all and the shared deadline keeps its dealt
        /// hand.
        /// </summary>
        [Test]
        public void ABotNamesOnlyCardsTheTableWouldAccept()
        {
            int named = 0;

            for (ulong seed = 1; seed <= 20; seed++)
            {
                MatchEngine engine = Deal(seed);

                for (int seat = 0; seat < MatchSeats.Count; seat++)
                {
                    foreach (IActionSource source in new IActionSource[] { TestBots.Strongest, new RandomLegalActionSource(Config, seed) })
                    {
                        MatchIntent chosen = source.ChooseAction(engine.BuildSeatView(seat), seat);
                        Assert.That(chosen, Is.TypeOf<MulliganIntent>(), "the mulligan phase offers one intent");

                        List<CardInstanceId> replace = ((MulliganIntent)chosen).Replace;
                        Assert.That(MulliganRules.CheckSubmit(engine.Model, seat, replace).IsSuccess, Is.True,
                            $"seed {seed}, seat {seat}: {source.GetType().Name} named a card the table refuses");

                        named += replace.Count;
                    }
                }
            }

            // A run in which no bot named anything would pass every assertion above without asking the
            // question, so the net has to see something actually put back.
            Assert.That(named, Is.GreaterThan(0), "no bot put anything back across 20 deals");
        }

        /// <summary>
        /// While the mulligan is open the hand holds only the opening deal, so the table accepts any single card
        /// of it — which is what lets the board offer the whole fan and the bots consider every card.
        /// </summary>
        [Test]
        public void AnyCardInTheOpeningHandMayBeNamed()
        {
            MatchEngine engine = Deal(seed: 5);

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                foreach (CardInstanceId id in engine.SecretHand(seat))
                {
                    Assert.That(engine.Rules.Instance(id).FromStartingDeck, Is.True);
                    Assert.That(MulliganRules.CheckSubmit(engine.Model, seat, new List<CardInstanceId> { id }).IsSuccess,
                        Is.True, $"seat {seat} may not name instance {id.Value}");
                }
            }
        }

        [Test]
        public void MulliganDoesNotChangeTheUnseenPool()
        {
            MatchEngine  engine = Deal(seed: 5);
            List<CardId> before = new List<CardId>(engine.Rules.Seat(0).UnseenPool);

            engine.Mulligan(0, engine.SecretHand(0).ToArray());
            engine.Mulligan(1, engine.SecretHand(1)[0]);

            // The seat on turn drew at the start of its turn, which also does not touch the pool.
            Assert.That(engine.Rules.Seat(0).UnseenPool, Is.EqualTo(before));
            Assert.That(engine.Rules.Seat(0).UnseenPool, Is.EqualTo(engine.DeriveUnseenPool(0)));
        }

        [Test]
        public void MulliganResolvedEventCarriesOnlyACount()
        {
            MatchEngine        engine    = Deal(seed: 5);

            engine.Clear();
            engine.Mulligan(0, engine.SecretHand(0)[0]);
            engine.Mulligan(1);

            List<MulliganResolvedEvent> resolved = engine.EventsOf<MulliganResolvedEvent>();
            Assert.That(resolved.Count, Is.EqualTo(2));

            // Reflection rather than eyeballing: the event type itself must have no way to name a card.
            foreach (System.Reflection.PropertyInfo property in typeof(MulliganResolvedEvent).GetProperties())
            {
                Assert.That(property.PropertyType, Is.EqualTo(typeof(int)),
                    $"{property.Name} could carry an identity the mulligan must not publish");
            }
        }
    }
}
