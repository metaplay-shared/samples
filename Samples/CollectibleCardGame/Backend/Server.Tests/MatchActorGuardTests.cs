using Game.Logic;
using Game.Server.Match;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using Metaplay.Server.MultiplayerEntity;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Game.Server.Tests
{
    /// <summary> Guards on the actor's own shape that would otherwise fail only at runtime. </summary>
    [TestFixture]
    public class MatchActorGuardTests
    {
        [Test]
        public void TheEntityConfigIsEphemeralAndOnTheLogicNodes()
        {
            MatchEntityConfig config = new MatchEntityConfig();

            Assert.That(config.EntityKind, Is.EqualTo(EntityKindGame.Match));
            Assert.That(config.EntityActorType, Is.EqualTo(typeof(MatchActor)));
            Assert.That(config.NodeSetPlacement, Is.EqualTo(NodeSetPlacement.Logic));

            Assert.That(config, Is.InstanceOf<EphemeralEntityConfig>());

            Assert.That(config, Is.Not.InstanceOf<PersistedEntityConfig>(),
                "a match is not persisted: it lives exactly as long as its actor");
        }

        [Test]
        public void TheOneGameEntityKindIsInTheGamesOwnRange()
        {
            // [100, 300) is the game's. A value outside it collides with the SDK's own kinds, which is a
            // startup crash naming the number.
            Assert.That(EntityKindGame.Match.Value, Is.InRange(100, 299));
            Assert.That(EntityKindGame.Matchmaker.Value, Is.InRange(100, 299));
            Assert.That(EntityKindGame.Community.Value, Is.InRange(100, 299));

            // Allocated in order with no gaps, so the registry reads as its own documentation. An entity kind
            // is a startup-registered identity: once a deployed game has written rows under one, a later kind
            // wearing that number would be a different entity behind a value the cluster still remembers.
            Assert.That(
                new[] { EntityKindGame.Match.Value, EntityKindGame.Matchmaker.Value, EntityKindGame.Community.Value },
                Is.EqualTo(new[] { 100, 101, 102 }));
        }

        [Test]
        public void TheMatchClientSlotIsClearOfTheCoreRange()
        {
            // Core slots occupy the low range; a game slot that collided with one would deliver a match's
            // state to the wrong sub-client.
            Assert.That(ClientSlotGame.Match.Id, Is.GreaterThan(10));
        }

        [Test]
        public void TheInternalMatchMessagesAreServerInternalAndInTheirBlock()
        {
            // Namespace placement is enforced at startup: a client-facing message must live in the shared
            // assembly and a server-internal one must not. These are the server-internal half, and their
            // codes are nevertheless allocated in the one registry the game has.
            AssertServerInternal(typeof(InternalMatchDeliverResultRequest), MessageCodes.InternalMatchDeliverResult);
            AssertServerInternal(typeof(InternalMatchDeliverResultResponse), MessageCodes.InternalMatchDeliverResultOk);
            AssertServerInternal(typeof(InternalMatchProbeRequest), MessageCodes.InternalMatchProbe);
            AssertServerInternal(typeof(InternalMatchProbeResponse), MessageCodes.InternalMatchProbeOk);
            AssertServerInternal(typeof(InternalMatchAbandonRequest), MessageCodes.InternalMatchAbandon);
            AssertServerInternal(typeof(InternalMatchAbandonResponse), MessageCodes.InternalMatchAbandonOk);
        }

        [Test]
        public void TheDeliveryAskUsesTheGenericRequestForm()
        {
            // The non-generic EntityAskRequest is deprecated in the SDK, and the generic form is what gets the
            // response type checked at the call site.
            Assert.That(typeof(EntityAskRequest<InternalMatchDeliverResultResponse>).IsAssignableFrom(typeof(InternalMatchDeliverResultRequest)), Is.True);
            Assert.That(typeof(EntityAskResponse).IsAssignableFrom(typeof(InternalMatchDeliverResultResponse)), Is.True);
        }

        [Test]
        public void TheSetupParamsCarryTheFrozenSnapshotAndNothingLive()
        {
            // The decks and ranks are a snapshot taken at formation: a setup that carried a reference to an
            // account would let a rank that moved mid-search move the deal.
            foreach (PropertyInfo property in typeof(MatchSeatSetup).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(PlayerModel)), $"{property.Name} would carry a live account");
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(PlayerDeck)), $"{property.Name} would carry a live deck");
            }

            Assert.That(typeof(IMultiplayerEntitySetupParams).IsAssignableFrom(typeof(MatchSetupParams)), Is.True);
        }

        // ---------------------------------------------------------------- the model's own guards

        [Test]
        public void TheActorTicksTheModelsClock()
        {
            // Every deadline is stamped from the model's clock and ticks are what advance it, so an override
            // that turned ticking off would freeze every stamp at the deal. The SDK base ticks by default.
            PropertyInfo isTicking = typeof(MatchActor).GetProperty("IsTicking",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

            Assert.That(isTicking, Is.Null, "MatchActor overrides IsTicking; the model's clock must advance");
        }

        [Test]
        public void TheActorHoldsNoSecondCopyOfTheGame()
        {
            // The model on the timeline is the only state; a field holding another copy would need a publisher
            // to keep the two in step.
            foreach (FieldInfo field in typeof(MatchActor).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.That(field.FieldType.Name, Does.Not.Contain("Publisher"), $"{field.Name} is a publisher");
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(MatchRulesState)), $"{field.Name} holds a second copy of the rules state");
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(MatchModel)), $"{field.Name} holds a second model");
            }
        }

        static void AssertServerInternal(Type type, int expectedCode)
        {
            MetaMessageSpec spec = MetaMessageRepository.Instance.GetFromType(type);

            Assert.That(spec.MessageDirection, Is.EqualTo(MessageDirection.ServerInternal), $"{type.Name}");
            Assert.That(spec.TypeCode, Is.EqualTo(expectedCode), $"{type.Name}");
            Assert.That(spec.TypeCode, Is.InRange(30300, 30399), $"{type.Name} is outside the server-internal match block");
        }
    }
}
