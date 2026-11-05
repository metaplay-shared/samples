using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The deal: what each seat starts with, and the fixed order the stream is consumed in. Every test names
    /// the seed it depends on.
    /// </summary>
    [TestFixture]
    public class DealTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        MatchEngine Deal(ulong seed)
        {
            MatchSetup setup = new MatchSetup(seed, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            return MatchEngine.Create(setup);
        }

        /// <summary>
        /// The whole registry at the deal is the two authored decks, none of it public: anything else would be
        /// a card the mulligan is asked about and can never put back, which is why the Acorn is granted when the
        /// mulligan resolves rather than dealt. The first seat holds three, the second four, and each deck holds
        /// the rest.
        /// </summary>
        [Test]
        public void TheDealShape()
        {
            MatchEngine engine = Deal(seed: 12345);
            GlobalConfig global = Config.Global;
            CardId       acorn  = global.SecondPlayerBonusCard.Ref.CardId;

            Assert.That(engine.Rules.Instances.Count, Is.EqualTo(global.DeckSize * MatchSeats.Count));
            foreach (CardInstance instance in engine.Rules.Instances)
            {
                Assert.That(instance.FromStartingDeck, Is.True, $"instance {instance.Id.Value}");
                Assert.That(instance.IsPublic, Is.False, $"instance {instance.Id.Value}");
            }

            int first = engine.Rules.FirstSeat;
            foreach ((int seat, int hand) in new[] { (first, global.OpeningHandFirstPlayer), (MatchSeats.Other(first), global.OpeningHandSecondPlayer) })
            {
                int owned = 0;
                foreach (CardInstance instance in engine.Rules.Instances)
                {
                    if (instance.Owner == seat)
                        owned++;
                }

                Assert.That(owned, Is.EqualTo(global.DeckSize), $"seat {seat} owns one deck");
                Assert.That(engine.SecretHand(seat).Count, Is.EqualTo(hand), $"seat {seat}'s opening hand");
                Assert.That(engine.SecretDeck(seat).Count, Is.EqualTo(global.DeckSize - hand), $"seat {seat}'s deck");
                Assert.That(HandCards(engine, seat), Does.Not.Contain(acorn), $"seat {seat} was dealt the Acorn");
            }
        }

        [Test]
        public void WeatherIsPublicBeforeMulliganPhaseOpens()
        {
            MatchEngine        engine    = Deal(seed: 12345);

            MatchDealtEvent dealt = engine.LastEventOf<MatchDealtEvent>();
            Assert.That(dealt, Is.Not.Null);
            Assert.That(dealt.Weather, Is.EqualTo(engine.Weather.WeatherId));
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Mulligan));
            // And the Weather is on the public rules state, which is what a follower holds: the reference
            // resolves, so a client can name it without being told.
            Assert.That(engine.Rules.Weather.Ref.WeatherId, Is.EqualTo(engine.Weather.WeatherId));
        }

        [Test]
        public void DealConsumesTheStreamInTheFixedOrder()
        {
            // Weather, seat 0's shuffle, seat 1's shuffle, the first seat — and nothing else. Replaying that
            // order by hand must land on the same generator position the deal left behind.
            const ulong Seed = 4242;

            MatchEngine engine = Deal(Seed);

            RandomPCG           replay  = RandomPCG.CreateFromSeed(Seed);
            List<WeatherInfo>   pool    = new List<WeatherInfo>(Config.Weathers.Values);
            pool.Sort((a, b) => string.CompareOrdinal(a.WeatherId.Value, b.WeatherId.Value));
            WeatherInfo         weather = pool[replay.NextInt(pool.Count)];

            int[] deck0 = Identity(Config.Global.DeckSize);
            int[] deck1 = Identity(Config.Global.DeckSize);
            replay.ShuffleInPlace(deck0);
            replay.ShuffleInPlace(deck1);
            int firstSeat = replay.NextInt(2);

            Assert.That(engine.Weather.WeatherId, Is.EqualTo(weather.WeatherId));
            Assert.That(engine.Rules.FirstSeat, Is.EqualTo(firstSeat));
            Assert.That(engine.Model.Secret.Rng, Is.EqualTo(replay), "the deal consumed exactly four draws in the stated order");
        }

        [Test]
        public void InstanceIdsAreMintedInAuthoredOrderNotDrawOrder()
        {
            List<MatchDeckCard> deck0  = TestDecks.Standard(Config);
            MatchSetup          setup  = new MatchSetup(999, Config, MatchTimings.Instant, deck0, TestDecks.Alternate(Config));
            MatchEngine         engine = MatchEngine.Create(setup);

            for (int ndx = 0; ndx < deck0.Count; ndx++)
            {
                CardInstance instance = engine.Rules.Instance(new CardInstanceId(ndx));
                Assert.That(instance.Owner, Is.EqualTo(0));
                Assert.That(CardLookup.CardId(engine.Model, instance.Id), Is.EqualTo(deck0[ndx].Card), $"identity {ndx} must name the {ndx}th authored card");
            }
        }

        [Test]
        public void InstanceIdsMintedInDrawOrder_IsDetected()
        {
            // The negative control for the test above: if identities had been minted as cards were dealt, the
            // opening hand would hold the lowest ones. The real deal must not look like that.
            MatchEngine engine = Deal(seed: 999);
            int         first  = engine.Rules.FirstSeat;

            bool handIsAPrefix = true;
            List<CardInstanceId> hand = engine.SecretHand(first);
            for (int ndx = 0; ndx < hand.Count; ndx++)
            {
                if (hand[ndx].Value != first * Config.Global.DeckSize + ndx)
                    handIsAPrefix = false;
            }

            Assert.That(handIsAPrefix, Is.False, "the dealt hand must not be the lowest identities of the deck");
        }

        [Test]
        public void UnseenPoolStartsAsTheFullDeck()
        {
            List<MatchDeckCard> deck0  = TestDecks.Standard(Config);
            MatchSetup          setup  = new MatchSetup(31337, Config, MatchTimings.Instant, deck0, TestDecks.Alternate(Config));
            MatchEngine         engine = MatchEngine.Create(setup);

            List<CardId> expected = new List<CardId>();
            foreach (MatchDeckCard entry in deck0)
                expected.Add(entry.Card);
            ZoneOps.SortPool(expected);

            Assert.That(engine.Rules.Seat(0).UnseenPool, Is.EqualTo(expected));
            Assert.That(engine.Rules.Seat(0).UnseenPool, Is.EqualTo(engine.DeriveUnseenPool(0)));
        }

        [Test]
        public void ADealAtRankFiveMintsInstancesWithTheirTrackApplied()
        {
            // The whole deal path at a rank the collection would really hold, rather than the rank-1 default
            // every other test uses: a track that never reached MintDeck would look identical at rank 1.
            List<MatchDeckCard> ranked = TestDecks.Standard(Config, rank: 5);
            MatchSetup          setup  = new MatchSetup(2468, Config, MatchTimings.Instant, ranked, TestDecks.Alternate(Config));
            MatchEngine         engine = MatchEngine.Create(setup);

            bool sawGrowth = false;
            foreach (CardInstance instance in engine.Rules.Instances)
            {
                if (instance.Owner != 0 || !instance.FromStartingDeck)
                    continue;

                Assert.That(CardLookup.Rank(engine.Model, instance.Id), Is.EqualTo(5));

                CardInfo card = CardLookup.Info(engine.Model, instance.Id);
                if (!card.HasRankGrowth(5))
                    continue;

                sawGrowth = true;
                CardStats grown = SecretDerivations.Stats(engine.Model, instance.Id).Value;
                bool      moved = grown.Attack != card.Attack || grown.Health != card.Health || grown.Cost != card.Cost || grown.EffectAmountDelta != 0;
                Assert.That(moved, Is.True, $"{card.CardId} was minted without its rank track applied");
            }

            Assert.That(sawGrowth, Is.True, "the deck has at least one card whose numbers move at rank 5");

            // And the seat's ranks are its own: the opponent came at rank 1.
            foreach (CardInstance instance in engine.Rules.Instances)
            {
                if (instance.Owner == 1 && instance.FromStartingDeck)
                    Assert.That(CardLookup.Rank(engine.Model, instance.Id), Is.EqualTo(1));
            }
        }

        static List<CardId> HandCards(MatchEngine engine, int seat)
        {
            List<CardId> cards = new List<CardId>();
            foreach (CardInstanceId id in engine.SecretHand(seat))
                cards.Add(CardLookup.CardId(engine.Model, id));
            return cards;
        }

        static int[] Identity(int count)
        {
            int[] values = new int[count];
            for (int ndx = 0; ndx < count; ndx++)
                values[ndx] = ndx;
            return values;
        }
    }
}
