using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The controls that must fail. "Nothing leaked" is also what a check that compared nothing would report,
    /// so every positive result in <see cref="IndistinguishabilityTests"/> is worth exactly as much as these
    /// are — a negative control that passes is the one result that invalidates the whole harness
    /// (<c>Docs/bots.md</c>, "Negative controls").
    /// <para>
    /// Each control reuses the same comparison machinery the positive case uses and feeds it a deliberately
    /// broken input, one per leak class <c>Docs/hidden-information.md</c> names: the opponent's hand
    /// spliced into a field the type does carry, an identity minted in draw order, and the opponent's deck
    /// published as its own list — the literal subtraction problem, reintroduced.
    /// </para>
    /// </summary>
    [TestFixture]
    public class IndistinguishabilityNegativeControlTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        // ---------------------------------------------------------------- the positive baseline

        [Test]
        public void TheHonestProjectionPassesTheSameComparison()
        {
            // Without this, a control that "failed" would only prove the comparison fails on everything.
            Pair pair = Pair.Build(seatToObserve: 0);

            Assert.That(
                SelfPlayBytes.Equal(HiddenState.SeatPayload(pair.Left, pair.Seat), HiddenState.SeatPayload(pair.Right, pair.Seat)),
                Is.True,
                "the real projection is the same across the two deals");

            // The controls below build their bytes through a different assembler, and a second assembler is a
            // second thing that can drift. Fed the seat's real public view with nothing planted in it, it has
            // to produce the *same buffer* the positive case compares — not merely one that agrees with
            // itself. Without this, a control could be green because its assembler had quietly stopped
            // including the field the plant goes into.
            int enemy = MatchSeats.Other(pair.Seat);

            Assert.That(
                SelfPlayBytes.Equal(
                    HiddenState.SeatPayload(pair.Left, pair.Seat),
                    HiddenState.PayloadWithSeatState(pair.Left, pair.Seat, enemy, HiddenState.CopyOf(pair.Left.Rules.Seat(enemy)))),
                Is.True,
                "the control's assembler and the real one build the same payload from the same state");

            Assert.That(
                SelfPlayBytes.Equal(
                    LeakingPayload(pair.Left, pair.Seat, enemy, pair.Left.Rules.Seat(enemy).UnseenPool),
                    LeakingPayload(pair.Right, pair.Seat, enemy, pair.Right.Rules.Seat(enemy).UnseenPool)),
                Is.True,
                "and it agrees across the pair when nothing is planted in it");
        }

        // ---------------------------------------------------------------- control 1: the hand, included

        [Test]
        public void AProjectionCarryingTheOpponentHandIsCaught()
        {
            Pair pair = Pair.Build(seatToObserve: 0);
            int  enemy = MatchSeats.Other(pair.Seat);

            // Spliced into a field the seat view really does carry, so what is under test is whether the
            // comparison inspects content rather than merely type shape.
            byte[] left  = LeakingPayload(pair.Left, pair.Seat, enemy, Hand(pair.Left, enemy));
            byte[] right = LeakingPayload(pair.Right, pair.Seat, enemy, Hand(pair.Right, enemy));

            Assert.That(SelfPlayBytes.Equal(left, right), Is.False,
                "the harness has to catch a projection carrying the opponent's hand");
        }

        // ---------------------------------------------------------------- control 2: draw-order identities

        [Test]
        public void AnIdentityMintedInDrawOrderIsCaught()
        {
            // The control for this leak class deliberately does not go through the byte comparison the other
            // two use, and the reason is worth stating rather than working around.
            //
            // The comparison asks whether two members of one indistinguishability class produce the same
            // payload, and there are exactly two ways to feed a relabelling into it. Neither discriminates.
            //
            // Relabel BOTH members and nothing is caught. Every zone's count is fixed across the class, so a
            // relabelling that assigns identities by position gives every card the seat can see the same
            // identity in both members — the cards whose position actually moved are the opponent's, in a deck
            // and a hand the seat cannot see. The payloads still match, for a correct engine and a broken one
            // alike.
            //
            // Relabel ONE member and everything is caught, for the same reason and just as uninformatively:
            // renaming the cards on one side of a comparison of names makes the two sides differ whatever the
            // engine does. A control that passes no matter what is worse than no control at all.
            //
            // What is checkable is the property that makes draw-order minting a leak in the first place: an
            // identity must be a function of the authored deck list, not of the shuffle. Both directions are
            // asserted, because only the pair of them says the engine is on the right side of it.
            MatchEngine first  = Deal(seed: 1001);
            MatchEngine second = Deal(seed: 2002);

            Assert.That(first.SecretDeck(0), Is.Not.EqualTo(second.SecretDeck(0)),
                "the two deals have to differ in the shuffle, or neither direction means anything");

            Assert.That(StartingDeckIdentities(second), Is.EqualTo(StartingDeckIdentities(first)),
                "the engine's own mint is shuffle-independent");

            MatchModel firstDrawOrder = HiddenState.Clone(first.State, Config);
            HiddenState.RelabelInDrawOrder(firstDrawOrder);
            MatchModel secondDrawOrder = HiddenState.Clone(second.State, Config);
            HiddenState.RelabelInDrawOrder(secondDrawOrder);

            Assert.That(
                StartingDeckIdentities(HiddenState.Wrap(secondDrawOrder)),
                Is.Not.EqualTo(StartingDeckIdentities(HiddenState.Wrap(firstDrawOrder))),
                "a draw-order mint would make the identity read off the shuffle");
        }

        // ---------------------------------------------------------------- control 3: the subtraction problem

        [Test]
        public void PublishingTheOpponentDeckAsItsOwnListIsCaught()
        {
            Pair pair  = Pair.Build(seatToObserve: 0);
            int  enemy = MatchSeats.Other(pair.Seat);

            // The rule that was rejected, put back: the remaining deck as a list of its own rather than one
            // unseen pool. It leaks nothing an identity scan could see, and it differs between two deals that
            // are supposed to look the same — which is exactly the class byte equality exists for.
            byte[] left  = LeakingPayload(pair.Left, pair.Seat, enemy, Deck(pair.Left, enemy));
            byte[] right = LeakingPayload(pair.Right, pair.Seat, enemy, Deck(pair.Right, enemy));

            Assert.That(SelfPlayBytes.Equal(left, right), Is.False,
                "the harness has to catch a published deck list, which is the subtraction problem");
        }

        // ---------------------------------------------------------------- control 4: a count in the wrong place

        [Test]
        public void ACountComputedOverHiddenStateIsCaught()
        {
            // Not a card, not an identity, not an ordering: one integer, in a field that is supposed to hold
            // an integer, carrying what the opponent's hand is worth in mana. An identity scan sees nothing to
            // object to, and the byte comparison has to catch it anyway — this is the class
            // hidden-information.md means by "a count in the wrong place".
            //
            // The pair is chosen so the planted number really does differ; a pair where it happened to match
            // would make the control pass or fail on a coincidence.
            Pair pair  = Pair.Build(seatToObserve: 0, (left, right, enemySeat) => HandCost(left, enemySeat) != HandCost(right, enemySeat));
            int  enemy = MatchSeats.Other(pair.Seat);

            byte[] leftBytes  = CountPayload(pair.Left, pair.Seat, enemy, HandCost(pair.Left, enemy));
            byte[] rightBytes = CountPayload(pair.Right, pair.Seat, enemy, HandCost(pair.Right, enemy));

            Assert.That(SelfPlayBytes.Equal(leftBytes, rightBytes), Is.False,
                "the harness has to catch a count derived from what the seat may not see");
        }

        // ---------------------------------------------------------------- control 5: a leftover RNG position

        [Test]
        public void ALeftoverRngPositionIsCaught()
        {
            Pair pair  = Pair.Build(seatToObserve: 0);
            int  enemy = MatchSeats.Other(pair.Seat);

            // The stream replays forward in closed form, so a position plus a seed is the rest of the deal —
            // which is why hidden-information.md lists a leftover RNG position among the things byte equality
            // is for. The comparison can only see it if the two states disagree about where the stream stands,
            // which is why the re-deal advances it.
            byte[] left  = TicksPayload(pair.Left, pair.Seat, enemy, HiddenState.RngPositionProxy(pair.Left.Model));
            byte[] right = TicksPayload(pair.Right, pair.Seat, enemy, HiddenState.RngPositionProxy(pair.Right.Model));

            Assert.That(SelfPlayBytes.Equal(left, right), Is.False,
                "the harness has to catch a payload that moved with the seeded stream");
        }

        // ---------------------------------------------------------------- the other half of the mint rule

        [Test]
        public void ACardMintedDuringPlayTakesItsIdentityFromPublicOrder()
        {
            // hidden-information.md's mint rule has two halves. The deal's half — identities over the authored
            // deck list, before the shuffle — is what the control above is about. The other half is that
            // instances created *during* play take the next identity from a counter, "which is safe because
            // they are created by public events in a public order".
            //
            // The indistinguishability sweep structurally cannot check that: a token's identity is a position
            // in a counter, and both members of a class have the same counter. So it is checked here, against
            // the thing that would break it — two games whose only difference is the shuffle. If a mint during
            // play ever consumed the seeded stream, or fired in an order the deal decided, the same public
            // sequence of plays would hand out different identities.
            List<string> first  = MintedDuringPlay(seed: 4001);
            List<string> second = MintedDuringPlay(seed: 4002);

            Assert.That(first, Is.Not.Empty, "no token or copy was minted, so the check would say nothing");
            Assert.That(second, Is.EqualTo(first), "an in-play mint took its identity from something other than public order");
        }

        /// <summary>
        /// The identities handed to cards minted during play, in the order they were minted, for a game whose
        /// public sequence of plays is fixed and whose shuffle is not. The board is built rather than dealt so
        /// the plays are the same on both sides; the shuffle differs underneath them.
        /// </summary>
        static List<string> MintedDuringPlay(ulong seed)
        {
            Scenario       scenario  = new Scenario(Config);
            CardInstanceId shepherd  = scenario.Seat(0).Hand("SheepdogShepherd");  // Hello: summon two Lambs
            CardInstanceId seance    = scenario.Seat(0).Hand("MoonlightSeance");   // Hello: copy from a graveyard
            scenario.Seat(0).Graveyard("PondFrog");
            scenario.Seat(0).Mana(9).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.Seed(seed).OnTurn(0).Build();

            int before = engine.Rules.Instances.Count;

            Assert.That(engine.Play(0, shepherd), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Play(0, seance), Is.EqualTo(MatchIntentResults.Success));

            List<string> minted = new List<string>();
            for (int ndx = before; ndx < engine.Rules.Instances.Count; ndx++)
            {
                CardInstance instance = engine.Rules.Instances[ndx];
                minted.Add($"{instance.Id.Value}={CardLookup.CardId(engine.Model, instance.Id)}");
            }

            return minted;
        }

        [Test]
        public void TheSubtractionProblemIsInvisibleToAnIdentityScan()
        {
            // Worth pinning: the reason this control is not redundant with the leak walk is that the
            // leak walk would report the published deck as perfectly clean. Every card in it is one the seat
            // is already allowed to know about.
            Pair pair  = Pair.Build(seatToObserve: 0);
            int  enemy = MatchSeats.Other(pair.Seat);

            foreach (CardId card in Deck(pair.Left, enemy))
                Assert.That(pair.Left.Rules.Seat(enemy).UnseenPool, Does.Contain(card), "the deck holds nothing the pool does not");
        }

        // ---------------------------------------------------------------- fixtures

        static MatchEngine Deal(ulong seed)
        {
            MatchEngine engine = MatchEngine.Create(
                new MatchSetup(seed, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config)));

            engine.Mulligan(0);
            engine.Mulligan(1);
            return engine;
        }

        /// <summary> Card to identity, for the cards that came out of a starting deck, in canonical card order. </summary>
        static List<string> StartingDeckIdentities(MatchEngine engine)
        {
            List<string> pairs = new List<string>();
            foreach (CardInstance instance in engine.Rules.Instances)
            {
                if (instance.FromStartingDeck)
                    pairs.Add($"{instance.Owner}:{CardLookup.CardId(engine.Model, instance.Id)}={instance.Id.Value}");
            }

            pairs.Sort(string.CompareOrdinal);
            return pairs;
        }

        static byte[] LeakingPayload(MatchEngine engine, int observingSeat, int leakedSeat, List<CardId> planted)
            => HiddenState.PayloadWithSeatState(
                engine,
                observingSeat,
                leakedSeat,
                HiddenState.WithUnseenPool(engine.Rules.Seat(leakedSeat), planted));

        static byte[] CountPayload(MatchEngine engine, int observingSeat, int leakedSeat, int planted)
            => HiddenState.PayloadWithSeatState(
                engine,
                observingSeat,
                leakedSeat,
                HiddenState.WithHandCount(engine.Rules.Seat(leakedSeat), planted));

        static byte[] TicksPayload(MatchEngine engine, int observingSeat, int leakedSeat, int planted)
            => HiddenState.PayloadWithSeatState(
                engine,
                observingSeat,
                leakedSeat,
                HiddenState.WithTuckeredOutTicks(engine.Rules.Seat(leakedSeat), planted));

        static List<CardId> Hand(MatchEngine engine, int seat)
            => HiddenState.SortedCards(engine.Model, engine.SecretHand(seat));

        static List<CardId> Deck(MatchEngine engine, int seat)
            => HiddenState.SortedCards(engine.Model, engine.SecretDeck(seat));

        static int HandCost(MatchEngine engine, int seat)
            => HiddenState.TotalPrintedCost(engine.Model, engine.SecretHand(seat));

        /// <summary>
        /// A real position, reached by real play, and the two states derived from it that the observing seat
        /// is supposed to be unable to tell apart.
        /// </summary>
        sealed class Pair
        {
            public MatchEngine Left;
            public MatchEngine Right;
            public int         Seat;

            public static Pair Build(int seatToObserve, System.Func<MatchEngine, MatchEngine, int, bool> discriminates = null)
            {
                // A handful of turns in, so both boards, both graveyards and a real hand are in play rather
                // than a freshly dealt position where almost nothing is public yet.
                for (int index = 0; index < 100; index++)
                {
                    ulong       seed   = SelfPlayRun.GameSeed(SelfPlayStreams.NegativeControls, index);
                    MatchEngine engine = PlayAFewTurns(seed, seatToObserve);
                    if (engine == null)
                        continue;

                    if (!IndistinguishablePair.TryBuild(engine, seatToObserve, seed, out IndistinguishablePair pair))
                        continue;

                    // A plant that happens to be the same number on both sides proves nothing either way, so a
                    // control may insist on a pair where its own plant actually differs.
                    if (discriminates != null && !discriminates(pair.Left, pair.Right, MatchSeats.Other(seatToObserve)))
                        continue;

                    return new Pair { Left = pair.Left, Right = pair.Right, Seat = seatToObserve };
                }

                throw new AssertionException("no position with hidden state left to move was reachable");
            }

            static MatchEngine PlayAFewTurns(ulong seed, int seatToObserve)
            {
                MatchEngine engine = Deal(seed);
                BotPolicy   policy = new BotPolicy(Config, BotProfile.Strongest, seed);

                for (int step = 0; step < 400; step++)
                {
                    if (engine.Phase == MatchPhase.Complete)
                        return null;


                    if (engine.Pending.Kind == MatchPendingKind.AwaitingEffectChoice)
                    {
                        int holder = engine.Rules.PendingChoice.Seat;
                        engine.Submit(holder, policy.ChooseAction(engine.BuildSeatView(holder), holder));
                        continue;
                    }

                    int         seat   = engine.Rules.SeatOnTurn;
                    MatchIntent intent = policy.ChooseAction(engine.BuildSeatView(seat), seat);

                    // Deep enough that both sides have played, shallow enough that plenty is still unseen, and
                    // the observed seat's own turn with a card about to be named — the decision half of the
                    // control has nothing to say about a seat that is not being asked for anything.
                    if (seat == seatToObserve && engine.Turn >= 8 && engine.Rules.Seat(0).Graveyard.Count > 0 && NamesACard(intent))
                        return engine;

                    engine.Submit(seat, intent ?? new EndTurnIntent());
                }

                return null;
            }

            static bool NamesACard(MatchIntent intent) => intent is AttackIntent || intent is PlayCardIntent;
        }
    }
}
