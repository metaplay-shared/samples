using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// What the match model may and may not put on the wire.
    /// <para>
    /// The model <em>is</em> the game now, so the secret and the public sit in one object and the split is the
    /// only thing keeping a hand off the wire. Every claim here fails <b>silently</b> in production, which is
    /// the criterion for being in this fixture: the persistence claim (everything comes back under
    /// <c>IncludeAll</c>), the secrecy claim (nothing of the hidden half survives <c>SendOverNetwork</c>) and
    /// the shape claims (exactly three hidden members, and none anywhere beneath the public half).
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchModelTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        MatchModel BuildDealtModel(ulong dealSeed, ulong botSeed) => DealtModels.Build(Config, dealSeed, botSeed);

        MatchModel OnWire(MatchModel model) => DealtModels.AsFollower(model, Config);

        #region The persistence claim

        [Test]
        public void ThePersistedModelCarriesTheWholeGame()
        {
            MatchModel model = BuildDealtModel(dealSeed: 0x0123456789ABCDEFul, botSeed: 0xFEDCBA9876543210ul);

            MatchModel restored = MetaSerialization.CloneTagged(model, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: Config);
            restored.GameConfig = Config;

            // The hidden half is what makes a restart resumable, so all of it has to come back.
            Assert.That(restored.Secret, Is.Not.Null);
            Assert.That(restored.IsAuthority, Is.True);
            Assert.That(restored.Secret.BotSeed, Is.EqualTo(model.Secret.BotSeed));
            Assert.That(restored.SecretSeat(0).Hand, Is.EqualTo(model.SecretSeat(0).Hand));
            Assert.That(restored.SecretSeat(0).Deck, Is.EqualTo(model.SecretSeat(0).Deck));
            Assert.That(restored.SecretSeat(1).Deck, Is.EqualTo(model.SecretSeat(1).Deck));

            // The hidden identities come back too, which is what the hand views and the pool derivation read.
            Assert.That(restored.SecretSeat(0).Cards.Count, Is.EqualTo(model.SecretSeat(0).Cards.Count));

            // And a restored model replays identically, which is the whole point of persisting it.
            Assert.That(RulesHash(restored), Is.EqualTo(RulesHash(model)));
        }

        static uint RulesHash(MatchModel model)
            => MurmurHash.MurmurHash2(MetaSerialization.SerializeTagged(model.Rules, MetaSerializationFlags.IncludeAll, logicVersion: null));

        #endregion

        #region The secrecy claim

        [Test]
        public void TheWireFormCarriesNoneOfTheAuthoritativeHalf()
        {
            MatchModel model  = BuildDealtModel(dealSeed: 0x0123456789ABCDEFul, botSeed: 0xFEDCBA9876543210ul);
            MatchModel onWire = OnWire(model);

            Assert.That(onWire.Secret, Is.Null, "the hidden half must not ride the wire");
            Assert.That(onWire.IsAuthority, Is.False, "a follower is not the authority, and that is one null check");
            Assert.That(onWire.OwnHand, Is.Null, "a member whose value differs per viewer must not ride the timeline");
            Assert.That(onWire.SecretSeat(0), Is.Null, "the one accessor answers null on a follower");

            // The outbox is drained inside the actor turn that fills it, so nothing serializes it.
            List<string> serialized = MembersOf(typeof(MatchModel)).Select(member => member.Name).ToList();
            Assert.That(serialized, Does.Not.Contain(nameof(MatchModel.Outbox)));

            // The public half is still all there: the split is a line through the model, not a redaction of it.
            Assert.That(onWire.Rules, Is.Not.Null);
            Assert.That(onWire.Rules.Seat(0).UnseenPool.Count, Is.EqualTo(model.Rules.Seat(0).UnseenPool.Count));
            Assert.That(onWire.Rules.Seat(1).UnseenPool.Count, Is.EqualTo(model.Rules.Seat(1).UnseenPool.Count));
            Assert.That(onWire.Seats.Count, Is.EqualTo(MatchSeats.Count));
            Assert.That(onWire.Stakes, Is.Not.Null);
            Assert.That(onWire.Timings.TurnDeadline, Is.EqualTo(model.Timings.TurnDeadline), "the durations are public: a follower derives its own stamps from them");
        }

        [Test]
        public void NeitherSeedContributesAnythingToTheWireForm()
        {
            // Stronger than hunting for a seed's bytes in a payload, and free of any assumption about how the
            // serializer encodes a ulong: two models that differ ONLY in their seeds must produce
            // byte-identical wire forms. If either seed reached the wire in any encoding at all, these would
            // differ.
            MatchModel left  = BuildDealtModel(dealSeed: 1ul, botSeed: 0x0123456789ABCDEFul);
            MatchModel right = BuildDealtModel(dealSeed: 1ul, botSeed: 0xFEDCBA9876543210ul);

            Assert.That(DealtModels.Wire(right), Is.EqualTo(DealtModels.Wire(left)),
                "the bot seed must not reach the wire in any encoding");

            // The negative control for the comparison itself: the full form does carry both seeds, so the
            // same two models must differ there. A comparison that could not tell them apart would report the
            // wire form clean whatever the wire form said.
            Assert.That(DealtModels.Everything(right), Is.Not.EqualTo(DealtModels.Everything(left)),
                "the comparison must be able to see a seed that is there");
        }

        [Test]
        public void DeckOrderContributesNothingToTheWireForm()
        {
            // Deck ORDER is the secret; deck contents are not. So a model whose decks have been reordered —
            // the same cards, in the same zone, in a different order — must serialize to the same wire bytes,
            // because the public zone is the unseen pool and a pool is a multiset.
            MatchModel model = BuildDealtModel(dealSeed: 777ul, botSeed: 888ul);

            MatchModel reordered = MetaSerialization.CloneTagged(model, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: Config);
            reordered.GameConfig = Config;
            reordered.SecretSeat(0).Deck.Reverse();
            reordered.SecretSeat(1).Deck.Reverse();

            Assert.That(DealtModels.Wire(reordered), Is.EqualTo(DealtModels.Wire(model)),
                "reordering a deck must be invisible on the wire");

            Assert.That(DealtModels.Everything(reordered), Is.Not.EqualTo(DealtModels.Everything(model)),
                "the comparison must be able to see an order that is there");
        }

        [Test]
        public void AHandsContentsAreNotDerivableFromTheWireForm()
        {
            MatchModel model  = BuildDealtModel(dealSeed: 4242ul, botSeed: 2424ul);
            MatchModel onWire = OnWire(model);

            // The public zone is the unseen pool — the deck and the hand combined — which is exactly what
            // makes a hand undiffable. Every card that has not yet become public is in that pool, so a client
            // sees the multiset and cannot see the split.
            //
            // At the deal nothing is public yet — the compensation card arrives with the mulligan's
            // resolution — so the identity holds for both seats, which is the strongest form of it.
            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                SeatState state = onWire.Rules.Seat(seat);

                Assert.That(state.HandCount, Is.GreaterThan(0), $"seat {seat}");
                Assert.That(state.DeckCount, Is.GreaterThan(0), $"seat {seat}");
                Assert.That(state.UnseenPool.Count, Is.EqualTo(state.HandCount + state.DeckCount),
                    "the pool is the deck plus the hand: publishing either separately would leak draws by subtraction");

                foreach (CardInstanceId id in model.SecretSeat(seat).Hand)
                {
                    CardId card = CardLookup.CardId(model, id);
                    Assert.That(state.UnseenPool, Does.Contain(card), "a card in hand is in the pool, which is why the pool leaks nothing");
                }
            }
        }

        [Test]
        public void TheRegistrySaysNothingAboutWhichHiddenCardIsWhere()
        {
            // The model-shape half of the subtraction rule. A public entry that said "hidden, in a hand" versus
            // "hidden, in a deck" would let a client split the pool and read every draw — so the entry says
            // Unseen for both, and carries no card and no rank at all.
            MatchModel model  = BuildDealtModel(dealSeed: 31337ul, botSeed: 1ul);
            MatchModel onWire = OnWire(model);

            int hidden = 0;
            foreach (CardInstance instance in onWire.Rules.Instances)
            {
                if (instance.IsPublic)
                    continue;

                hidden++;
                Assert.That(instance.Place, Is.EqualTo(CardPlace.Unseen), $"{instance.Id} says which hidden zone it is in");
                Assert.That(instance.Card, Is.Null, $"{instance.Id} says which card it is");
                Assert.That(instance.Rank, Is.Zero, $"{instance.Id} says which rank it is at, which narrows the card");
                Assert.That(instance.IsKnown, Is.False);
            }

            Assert.That(hidden, Is.GreaterThan(0), "the comparison must have had hidden instances to look at");

            // And the server does know, which is what makes the assertions above about the mask rather than
            // about an empty registry.
            foreach (CardInstance instance in model.Rules.Instances)
            {
                if (instance.IsPublic)
                    continue;

                Assert.That(CardLookup.Info(model, instance.Id), Is.Not.Null, "the authority can still say what a hidden card is");
                Assert.That(CardLookup.Info(onWire, instance.Id), Is.Null, "a follower cannot, and gets null rather than a wrong answer");
            }
        }

        #endregion

        #region Shape guards

        [Test]
        public void ServerOnlyIsExactlyTheSecretRootAndTheOwnView()
        {
            // A ServerOnly member is off the wire and out of the checksum, so its value differs per viewer without
            // anything noticing. The model has three, the secret root and the client's own view, and nothing
            // reachable from the public members may have one.
            List<MetaSerializableMember> hidden = MembersOf(typeof(MatchModel)).Where(m => m.Flags.HasFlag(MetaMemberFlags.Hidden)).ToList();
            Assert.That(hidden.Select(m => m.Name), Is.EquivalentTo(new[] { nameof(MatchModel.Secret), nameof(MatchModel.OwnHand), nameof(MatchModel.OwnPeek) }));
            foreach (MetaSerializableMember member in hidden)
                Assert.That(member.Flags.HasFlag(MetaMemberFlags.ServerOnly), Is.True, $"{member.Name} must also be left out of the checksum");

            List<string>  offenders = new List<string>();
            HashSet<Type> seen      = new HashSet<Type> { typeof(MatchSecrets), typeof(SeatSecrets), typeof(HiddenCard), typeof(HandCard), typeof(PendingChoiceView) };

            foreach (MetaSerializableMember member in MembersOf(typeof(MatchModel)))
            {
                if (member.Flags.HasFlag(MetaMemberFlags.Hidden))
                    continue;

                foreach (Type reachable in Reachable(member.Type))
                    WalkForHidden(reachable, seen, offenders);
            }

            Assert.That(offenders, Is.Empty, "nothing reachable from the public half may be ServerOnly: " + string.Join(", ", offenders));

            // The walk must have reached the public state, or the check above passes vacuously.
            Assert.That(seen, Does.Contain(typeof(MatchRulesState)), "the walk did not reach the rules state");
            Assert.That(seen, Does.Contain(typeof(SeatState)), "the walk did not reach a seat");
            Assert.That(seen, Does.Contain(typeof(MatchEvent)), "the walk did not reach the event stream");
        }

        /// <summary>
        /// Walk every serializable type reachable from one root, reporting any <c>Hidden</c> member. The
        /// secret types themselves are pre-seeded into <paramref name="seen"/>, because they are legitimately
        /// full of secrets and are not reachable from the public half anyway.
        /// </summary>
        static void WalkForHidden(Type type, HashSet<Type> seen, List<string> offenders)
        {
            if (type == null || !seen.Add(type))
                return;

            if (!MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType spec))
                return;

            if (spec.Members != null)
            {
                foreach (MetaSerializableMember member in spec.Members)
                {
                    if (member.Flags.HasFlag(MetaMemberFlags.Hidden))
                        offenders.Add($"{type.Name}.{member.Name}");

                    foreach (Type reachable in Reachable(member.Type))
                        WalkForHidden(reachable, seen, offenders);
                }
            }

            if (spec.DerivedTypes != null)
            {
                foreach ((int _, Type derived) in spec.DerivedTypes)
                    WalkForHidden(derived, seen, offenders);
            }
        }

        /// <summary> A member's type, plus whatever it holds if it is a collection. </summary>
        static IEnumerable<Type> Reachable(Type type)
        {
            if (type == null)
                yield break;

            yield return type;

            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    foreach (Type nested in Reachable(argument))
                        yield return nested;
                }
            }
        }

        static IEnumerable<MetaSerializableMember> MembersOf(Type type)
        {
            Assert.That(MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType spec), Is.True, $"{type.Name} is not serializable");
            return spec.Members;
        }

        #endregion
    }
}
