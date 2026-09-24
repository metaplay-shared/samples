using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// What the match model may and may not put on the wire.
    /// <para>
    /// The model <em>is</em> the game now, so the secret and the public sit in one object and the split is the
    /// only thing keeping a hand off the wire. Every claim here fails <b>silently</b> in production, which is
    /// the criterion for being in this fixture: the persistence claim (everything comes back under
    /// <c>IncludeAll</c>), the secrecy claim (nothing of the hidden half survives <c>SendOverNetwork</c>) and
    /// the shape claims (exactly two hidden members, and none anywhere beneath the public half).
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
            // The model-shape half of the subtraction rule, and the reason CardZone became CardPlace. A public
            // entry that said "hidden, in a hand" versus "hidden, in a deck" would let a client split the pool
            // and read every draw — so the entry says Unseen for both, and carries no card and no rank at all.
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

        #region Config references on the wire

        /// <summary>
        /// A held resolution travels, and every config reference on it resolves against the client's config.
        /// <para>
        /// The effect queue is a <b>public</b> model member and every item on it holds a
        /// <c>MetaRef&lt;EffectStepInfo&gt;</c>. The queue is drained inside the action that filled it, so the
        /// one state a subscriber can receive it in is a peek waiting for its owner — and a reference the
        /// follower cannot resolve throws on the client at the moment the pause arrives, in the one state no
        /// rules test visits and every other suite serializes an empty list for.
        /// </para>
        /// <para>
        /// With today's content the loop below is empty: the only card that pauses has a single-step Hello, so
        /// nothing is left on the queue behind the pause. That is worth pinning rather than assuming — a
        /// two-step Hello whose first step is the peek is one CSV row away, and this is the test that would
        /// then be carrying the weight. <see cref="AnEffectStepReferenceResolvesOnAFollower"/> proves the
        /// mechanism itself so that this one is not the only thing standing between a new row and a client
        /// crash.
        /// </para>
        /// </summary>
        [Test]
        public void AHeldResolutionTravelsWithItsConfigReferencesIntact()
        {
            MatchEngine engine = Peeking().Build();
            engine.Play(0, engine.SecretHand(0)[0], EffectTargetRef.None);

            Assert.That(engine.Rules.PendingChoice, Is.Not.Null, "the fixture has to reach the pause for this to be a test");

            MatchModel follower = OnWire(engine.Model);

            Assert.That(follower.Rules.PendingChoice, Is.Not.Null, "the pause is public: both clients show a resolution held");
            Assert.That(follower.Rules.EffectQueue.Count, Is.EqualTo(engine.Rules.EffectQueue.Count));

            for (int ndx = 0; ndx < follower.Rules.EffectQueue.Count; ndx++)
            {
                EffectQueueItem item = follower.Rules.EffectQueue[ndx];
                Assert.That(item.Step, Is.Not.Null, $"queue item {ndx} lost its step reference on the wire");
                Assert.That(item.Step.Ref, Is.Not.Null, $"queue item {ndx}'s step reference does not resolve against the client's config");
                Assert.That(item.Step.Ref.StepId, Is.EqualTo(engine.Rules.EffectQueue[ndx].Step.Ref.StepId));
            }
        }

        /// <summary>
        /// The mechanism, independent of content: a queue item's step reference survives the network mask and
        /// resolves against the config a follower deserializes with. Written against a hand-built item rather
        /// than a played card because the queue empties inside the action, so no reachable game state holds
        /// one at a serialization point today.
        /// </summary>
        [Test]
        public void AnEffectStepReferenceResolvesOnAFollower()
        {
            EffectStepInfo step = null;
            foreach (EffectStepInfo candidate in Config.EffectSteps.Values)
            {
                step = candidate;
                break;
            }

            Assert.That(step, Is.Not.Null, "the config has to carry at least one effect step for this to be a test");

            EffectQueueItem item = new EffectQueueItem(
                MetaRef<EffectStepInfo>.FromItem(step),
                resolvingSeat: 1,
                source: default,
                chosenTarget: EffectTargetRef.None,
                amountBonus: 2,
                excludesSourceFromPlayedCount: true);

            EffectQueueItem travelled = MetaSerialization.CloneTagged(
                item, MetaSerializationFlags.SendOverNetwork, logicVersion: null, resolver: Config);

            Assert.That(travelled.Step, Is.Not.Null);
            Assert.That(travelled.Step.Ref, Is.Not.Null, "a step reference that does not resolve is a throw on the client");
            Assert.That(travelled.Step.Ref.StepId, Is.EqualTo(step.StepId));
            Assert.That(travelled.AmountBonus, Is.EqualTo(2), "the rest of the item travels too");
        }

        /// <summary> Seat 0 holding Pebble Collector over a deck whose top three are named, as EffectChoiceTests builds it. </summary>
        Scenario Peeking()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector");   // Hello: look at 3, keep 1
            scenario.Seat(0).Deck("MeadowMouse");
            scenario.Seat(0).Deck("MooseWanderer");
            scenario.Seat(0).Deck("BusyBeaver");
            scenario.Seat(0).DeckOf(4, "GreyOwl");
            scenario.Seat(0).Mana(9, 9);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            return scenario.OnTurn(0);
        }

        #endregion

        #region Shape guards

        [Test]
        public void NoModelMemberUsesTheSdksReservedTagBand()
        {
            // MultiplayerModelBase reserves [200, 300) for itself and uses 200-203. A member that strayed into
            // the band would be a startup crash at best and a silent overwrite at worst.
            foreach (MetaSerializableMember member in MembersOf(typeof(MatchModel)))
            {
                // The base class owns 200-203 and is welcome to them; what must stay out of the band is
                // everything this model declares.
                if (member.MemberInfo.DeclaringType != typeof(MatchModel))
                    continue;

                Assert.That(member.TagId is >= 200 and < 300, Is.False, $"{member.Name} uses reserved tag {member.TagId}");
            }
        }

        [Test]
        public void TheAuthoritativeMembersAreServerOnly()
        {
            // One member, and it is the one the server holds and no client ever does. The client's own view is
            // the mirror image — held by a client and never by the server — and it is not serialized, so it is
            // neither ServerOnly nor anything else. See TheClientsOwnViewIsNotOnTheModelsWire.
            AssertServerOnly(nameof(MatchModel.Secret));
        }

        [Test]
        public void ServerOnlyIsExactlyTheSecretRoot()
        {
            // The one secret root, by reflection — and NOTHING hidden anywhere in the tree beneath the public
            // half. A ServerOnly member down there would be excluded from the wire AND from the checksum, so
            // its value would differ per viewer and nothing would complain until the day it fed a public write.
            //
            // There is exactly one, and that is the whole of the hidden-on-the-wire story. The client's own
            // view — OwnHand, OwnPeek — is not here because it is not serialized at all: it never travels on
            // this model's wire, so it has nothing to be hidden from. TheClientsOwnViewIsNotOnTheModelsWire
            // asserts that directly.
            //
            // This is what the built projection's "a field nobody has written yet cannot leak" discipline
            // became. It is a test rather than a type, and that is a real trade.
            List<string> hidden = new List<string>();
            foreach (MetaSerializableMember member in MembersOf(typeof(MatchModel)))
            {
                if (member.Flags.HasFlag(MetaMemberFlags.Hidden))
                    hidden.Add(member.Name);
            }

            Assert.That(hidden, Is.EquivalentTo(new[] { nameof(MatchModel.Secret) }),
                "one secret root; a second hidden member is a second place to look");

            // The walk starts at the model itself rather than at a hand-kept list of its members' types. A
            // list has to be maintained, and the failure mode of a stale one is silence: a new public member
            // of a new type would simply not be walked. Starting from MatchModel and skipping the hidden root
            // makes the coverage structural — a new member is walked because it is there.
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

            // And the walk has to have gone somewhere: a Reachable() that stopped resolving types would make
            // the assertion above vacuously true.
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

        [Test]
        public void ServerOnlyIsBothMasksAtOnce()
        {
            // Worth pinning rather than reasoning about: ServerOnly is Hidden|NoChecksum, so it excludes a
            // member from the network mask AND the checksum mask. Hidden alone would still be checksummed, so
            // both clients would have to agree about a value neither may see — and there is deliberately no
            // standalone [Hidden] attribute to reach for by mistake.
            Assert.That(MetaMemberFlags.ServerOnly.HasFlag(MetaMemberFlags.Hidden), Is.True);
            Assert.That(MetaMemberFlags.ServerOnly.HasFlag(MetaMemberFlags.NoChecksum), Is.True);
        }

        [Test]
        public void TheModelsClockMovesOneSecondPerTick()
        {
            // Every deadline is stamped from the model's clock, and the rate is the lowest that serves: whole
            // seconds, the grain every shipped duration is expressed in.
            MatchModel model = new MatchModel();
            Assert.That(model.TicksPerSecond, Is.EqualTo(1));

            MetaTime before = model.CurrentTime;
            model.Tick(checksumCtx: null);
            Assert.That(model.CurrentTime - before, Is.EqualTo(MetaDuration.FromSeconds(1)));
        }

        [Test]
        public void TheTimingsRoundTripAsAModelMember()
        {
            // MatchTimings is a readonly struct with a deserialization constructor, sitting directly on a
            // MultiplayerModelBase subclass. That is a shape nothing in the sample used before, and every
            // pacing stamp a follower derives depends on it arriving intact.
            MatchModel model = BuildDealtModel(dealSeed: 5ul, botSeed: 6ul);
            model.Timings = MatchTimings.Default;

            MatchModel onWire = OnWire(model);

            Assert.That(onWire.Timings.TurnDeadline, Is.EqualTo(MatchTimings.Default.TurnDeadline));
            Assert.That(onWire.Timings.MulliganDeadline, Is.EqualTo(MatchTimings.Default.MulliganDeadline));
            Assert.That(onWire.Timings.TurnReserveBank, Is.EqualTo(MatchTimings.Default.TurnReserveBank));
            Assert.That(onWire.Timings.EffectChoiceDeadline, Is.EqualTo(MatchTimings.Default.EffectChoiceDeadline));
        }

        void AssertServerOnly(string memberName)
        {
            foreach (MetaSerializableMember member in MembersOf(typeof(MatchModel)))
            {
                if (member.Name != memberName)
                    continue;

                Assert.That(member.Flags.HasFlag(MetaMemberFlags.Hidden), Is.True, $"{memberName} must not be sent");
                Assert.That(member.Flags.HasFlag(MetaMemberFlags.NoChecksum), Is.True, $"{memberName} must not be checksummed");
                return;
            }

            Assert.Fail($"{memberName} is not a serialized member of {nameof(MatchModel)}");
        }

        static IEnumerable<MetaSerializableMember> MembersOf(Type type)
        {
            Assert.That(MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType spec), Is.True, $"{type.Name} is not serializable");
            return spec.Members;
        }

        #endregion
    }
}
