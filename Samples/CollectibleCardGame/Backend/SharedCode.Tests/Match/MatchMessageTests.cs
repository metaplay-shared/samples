using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Only an illegal move is a bug signal: the client had the same legality function and the same inputs
        /// and offered the move anyway. Everything else means something the server did first. An answer to a
        /// passed peek is stale rather than illegal — the board puts the card down and says nothing.
        /// </summary>
        [TestCase(nameof(MatchIntentResults.StaleChoice), MatchRefusalCode.Stale, false)]
        [TestCase(nameof(MatchIntentResults.ResolutionHeld), MatchRefusalCode.Stale, false)]
        [TestCase(nameof(MatchIntentResults.NotYourTurn), MatchRefusalCode.NotYourTurn, false)]
        [TestCase(nameof(MatchIntentResults.UnknownInstance), MatchRefusalCode.OutOfDate, false)]
        [TestCase(nameof(MatchIntentResults.NotEnoughMana), MatchRefusalCode.Illegal, true)]
        public void ARefusalMapsToItsCodeAndOnlyAnIllegalMoveIsABugSignal(string engineResult, MatchRefusalCode expected, bool isBugSignal)
        {
            MatchIntentResult result = (MatchIntentResult)typeof(MatchIntentResults).GetField(engineResult)!.GetValue(null);

            Assert.That(MatchRefusals.FromEngine(result), Is.EqualTo(expected));
            Assert.That(MatchRefusals.IsBugSignal(expected), Is.EqualTo(isBugSignal));
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

        static IEnumerable<MetaSerializableMember> MembersOf(Type type)
        {
            Assert.That(MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType spec), Is.True, $"{type.Name} is not serializable");
            return spec.Members;
        }

        #endregion
    }
}
