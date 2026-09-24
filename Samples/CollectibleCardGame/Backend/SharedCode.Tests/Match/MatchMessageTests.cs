using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The directed channel: what a refusal says, what a hand correction carries, and what the private-state
    /// payload does when it lands.
    /// </summary>
    [TestFixture]
    public class MatchMessageTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        #region Refusal mapping

        [Test]
        public void EveryEngineRefusalHasExactlyOneClientVisibleCode()
        {
            // A rules refusal added to the engine with no arm in the mapping must fail this test rather than
            // arriving at a board as a default. Success is the one member with nothing to map.
            foreach (MatchIntentResult result in MatchIntentResults.All)
            {
                if (result.IsSuccess)
                    continue;

                Assert.DoesNotThrow(() => MatchRefusals.FromEngine(result), $"{result} has no client-visible refusal code");
            }
        }

        [Test]
        public void AnUnmappedRefusalThrowsRatherThanDefaulting()
        {
            MatchIntentResult invented = new MatchIntentResult("SomeRuleNobodyMappedYet");
            Assert.Throws<MatchEngineException>(() => MatchRefusals.FromEngine(invented));
        }

        [Test]
        public void OnlyAnIllegalMoveIsABugSignal()
        {
            // Illegal means the client had the same legality function and the same inputs and offered the move
            // anyway. Everything else means something the server did first, which is often good news.
            Assert.That(MatchRefusals.IsBugSignal(MatchRefusalCode.Illegal), Is.True);
            Assert.That(MatchRefusals.IsBugSignal(MatchRefusalCode.Stale), Is.False);
            Assert.That(MatchRefusals.IsBugSignal(MatchRefusalCode.NotYourTurn), Is.False);
            Assert.That(MatchRefusals.IsBugSignal(MatchRefusalCode.OutOfDate), Is.False);
        }

        [Test]
        public void AnAnswerToAPassedPeekIsRefusedAsStaleRatherThanAsIllegal()
        {
            // The board puts the card down and says nothing, because nothing went wrong.
            Assert.That(MatchRefusals.FromEngine(MatchIntentResults.StaleChoice), Is.EqualTo(MatchRefusalCode.Stale));
            Assert.That(MatchRefusals.FromEngine(MatchIntentResults.ResolutionHeld), Is.EqualTo(MatchRefusalCode.Stale));
        }

        [Test]
        public void ARefusalNamesNoCard()
        {
            // A refusal that echoed the instance would be a channel for probing state, so it carries the code
            // and the request it answers and nothing else.
            MatchIntentRefused refusal = new MatchIntentRefused(7, MatchRefusalCode.Illegal);

            foreach (MetaSerializableMember member in MembersOf(typeof(MatchIntentRefused)))
            {
                Assert.That(member.Type, Is.Not.EqualTo(typeof(CardId)), $"{member.Name} would name a card");
                Assert.That(member.Type, Is.Not.EqualTo(typeof(CardInstanceId)), $"{member.Name} would name an instance");
                Assert.That(member.Type, Is.Not.EqualTo(typeof(EffectTargetRef)), $"{member.Name} would name a target");
            }

            Assert.That(System.Linq.Enumerable.Count(MembersOf(typeof(MatchIntentRefused))), Is.EqualTo(2), "the request id and the code");
            Assert.That(refusal.Reason, Is.EqualTo(MatchRefusalCode.Illegal));
        }

        #endregion

        #region Hand delivery

        [Test]
        public void APrivateStatePayloadReplacesTheLocalHandWholesale()
        {
            // Merging is not defined once a hand can hold two instances of the same catalogue card, so a
            // delivered hand replaces rather than merges.
            MatchModel model = new MatchModel
            {
                GameConfig = Config,
                OwnHand    = new List<HandCard>(),
            };

            List<HandCard> delivered = new List<HandCard> { new HandCard(new CardInstanceId(4), CardId.FromString("EmberKit"), 1) };

            new MatchMemberPrivateState(EntityId.Create(EntityKindCore.Player, 5), delivered, null).ApplyToModel(model);

            Assert.That(model.OwnHand, Is.SameAs(delivered));
            Assert.That(model.OwnHand.Count, Is.EqualTo(1));
        }

        [Test]
        public void APrivateStatePayloadRoundTripsToItsMember()
        {
            // The member id is the base class's; the payload carries the baseline for that member alone.
            EntityId member = EntityId.Create(EntityKindCore.Player, 5);
            MatchMemberPrivateState payload = new MatchMemberPrivateState(member, new List<HandCard>(), null);

            MatchMemberPrivateState roundTripped = (MatchMemberPrivateState)MetaSerialization.CloneTagged<MultiplayerMemberPrivateStateBase>(
                payload, MetaSerializationFlags.SendOverNetwork, logicVersion: null, resolver: Config);

            Assert.That(roundTripped.MemberId, Is.EqualTo(member));
            Assert.That(roundTripped.Hand, Is.Not.Null, "the baseline travels with the member it is for");
        }

        [Test]
        public void AddressedOperationsReplayOntoTheDeliveredBaseline()
        {
            // The baseline arrives once as private state; everything after it is one addressed operation per
            // card. Applying them in the order they were queued is what keeps a hand a list rather than a set.
            MatchModel     model = new MatchModel { GameConfig = Config, OwnHand = new List<HandCard>() };
            CardInstanceId acorn = new CardInstanceId(4);
            CardInstanceId ember = new CardInstanceId(7);

            new MatchOwnCardGained(new HandCard(acorn, CardId.FromString("EmberKit"), 1)).InvokeExecute(model, commit: true);
            new MatchOwnCardGained(new HandCard(ember, CardId.FromString("EmberKit"), 2)).InvokeExecute(model, commit: true);
            new MatchOwnCardLost(acorn).InvokeExecute(model, commit: true);

            Assert.That(model.OwnHand.Count, Is.EqualTo(1));
            Assert.That(model.OwnHand[0].Instance, Is.EqualTo(ember));
            Assert.That(model.OwnHand[0].Rank, Is.EqualTo(2));
        }

        [Test]
        public void AnAddressedOperationSurvivesTheWireAndNamesItsSeat()
        {
            // They ride the timeline as actions rather than as messages, so what matters is that the payload
            // round-trips under the network mask carrying what happened — and nothing else. It names no seat:
            // the SDK delivers it to one member, so a client holding it is the addressee by construction.
            MatchOwnCardGained gained = (MatchOwnCardGained)MetaSerialization.CloneTagged<ModelAction>(
                new MatchOwnCardGained(new HandCard(new CardInstanceId(9), CardId.FromString("EmberKit"), 3)),
                MetaSerializationFlags.SendOverNetwork, logicVersion: null, resolver: Config);

            Assert.That(gained.Instance, Is.EqualTo(new CardInstanceId(9)));
            Assert.That(gained.Card, Is.EqualTo(CardId.FromString("EmberKit")));
            Assert.That(gained.Rank, Is.EqualTo(3));

            // A departure needs only the instance: the hand it is leaving already knows what it is.
            MatchOwnCardLost lost = (MatchOwnCardLost)MetaSerialization.CloneTagged<ModelAction>(
                new MatchOwnCardLost(new CardInstanceId(2)),
                MetaSerializationFlags.SendOverNetwork, logicVersion: null, resolver: Config);

            Assert.That(lost.Instance, Is.EqualTo(new CardInstanceId(2)));
        }

        [Test]
        public void AnAddressedOperationCarriesWhatHappenedAndNothingElse()
        {
            // No seat, no position, no stamp. The SDK hands an addressed operation to one member and everyone
            // else a no-op, so the payload does not have to say who it is for — and a payload that named a
            // seat would be inviting somebody to check it, which is a check nothing can fail.
            List<string> gained = new List<string>();
            foreach (PropertyInfo property in typeof(MatchOwnCardGained).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                gained.Add(property.Name);

            gained.Sort();
            Assert.That(gained, Is.EqualTo(new List<string> { "Card", "Instance", "Rank" }));

            List<string> lost = new List<string>();
            foreach (PropertyInfo property in typeof(MatchOwnCardLost).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                lost.Add(property.Name);

            Assert.That(lost, Is.EqualTo(new List<string> { "Instance" }),
                "a departure needs only the instance: the hand it leaves already knows what it is");
        }

        [Test]
        public void TheClientsOwnViewIsNotOnTheModelsWire()
        {
            // OwnHand and OwnPeek are client-only state and carry no MetaMember at all: nothing reads them off
            // a wire, nothing persists them, and nothing in the multiplayer-entity follower path copies a model.
            List<string> serialized = new List<string>();
            foreach (MetaSerializableMember member in MembersOf(typeof(MatchModel)))
                serialized.Add(member.Name);

            Assert.That(serialized, Does.Not.Contain(nameof(MatchModel.OwnHand)));
            Assert.That(serialized, Does.Not.Contain(nameof(MatchModel.OwnPeek)));
            Assert.That(serialized, Does.Not.Contain(nameof(MatchModel.Outbox)));
        }

        #endregion

        #region Codes

        [Test]
        public void TheMatchMessageCodesAreAllocatedInTheirBlocks()
        {
            // The SDK has no reserved range for games and its own codes run up to roughly 19 800, so the whole
            // game allocates from 30 000 upward. A duplicate is a startup crash naming the code; a code in the
            // wrong block is a registry that no longer says where anything lives.
            AssertCode(typeof(MatchIntentMessage), 30000, 30099);
            AssertCode(typeof(MatchSeatIntentMessage), 30000, 30099);
            AssertCode(typeof(MatchIntentRefused), 30100, 30199);
        }

        [Test]
        public void TheClientFacingMessagesAreDirectedTheWayTheChannelNeeds()
        {
            AssertDirection(typeof(MatchIntentMessage), MessageDirection.ClientToServer);
            AssertDirection(typeof(MatchSeatIntentMessage), MessageDirection.ClientToServer);
            AssertDirection(typeof(MatchIntentRefused), MessageDirection.ServerToClient);
        }

        [Test]
        public void EveryClientToServerMatchMessageRidesTheEntityChannel()
        {
            // Without the routing rule the session has nowhere to send it: the table is not the player's own
            // entity, and a message with no rule is refused rather than routed.
            AssertRoutedToEntityChannel(typeof(MatchIntentMessage));
            AssertRoutedToEntityChannel(typeof(MatchSeatIntentMessage));
        }

        static void AssertCode(Type type, int low, int high)
        {
            MetaMessageSpec spec = MetaMessageRepository.Instance.GetFromType(type);
            Assert.That(spec.TypeCode, Is.InRange(low, high), $"{type.Name} has code {spec.TypeCode}, outside [{low}, {high}]");
        }

        static void AssertDirection(Type type, MessageDirection direction)
            => Assert.That(MetaMessageRepository.Instance.GetFromType(type).MessageDirection, Is.EqualTo(direction), $"{type.Name}");

        static void AssertRoutedToEntityChannel(Type type)
            => Assert.That(MetaMessageRepository.Instance.GetFromType(type).RoutingRule, Is.TypeOf<MessageRoutingRuleEntityChannel>(), $"{type.Name}");

        static IEnumerable<MetaSerializableMember> MembersOf(Type type)
        {
            Assert.That(MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType spec), Is.True, $"{type.Name} is not serializable");
            return spec.Members;
        }

        #endregion
    }
}
