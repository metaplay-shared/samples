using Game.Server.Match;
using Game.Server.SdkPreview;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity.Messages;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Game.Server.Tests
{
    /// <summary>
    /// Guards the one vendored change that fails at <em>startup</em> rather than at compile time.
    /// <para>
    /// <see cref="MatchActor"/> derives from the copy of the SDK's actor base in
    /// <c>Backend/Server/SdkPreview</c>, which exists only until the R39 upgrade (see that folder's
    /// README). The SDK's own ephemeral base declares the marker interface <c>IEphemeralEntityActor</c>,
    /// which is <c>internal</c> to the SDK assembly and therefore unimplementable from here; the copy
    /// inherits it by deriving from the public <see cref="EphemeralEntityActor"/> instead.
    /// </para>
    /// <para>
    /// If that ever stops holding, <c>EntityConfigRegistry</c> throws while validating entity configs and
    /// the server does not start — a failure no unit test would otherwise see, because nothing else here
    /// boots an entity. These tests are deleted along with the vendored copy.
    /// </para>
    /// </summary>
    [TestFixture]
    public class VendoredSdkBaseTests
    {
        [Test]
        public void MatchActorIsRecognisedAsAnEphemeralEntityActor()
        {
            // The name, not the type: the interface is internal to the SDK assembly and cannot be named here.
            // This is exactly what EntityConfigRegistry checks for an EphemeralEntityConfig's actor type.
            bool implementsMarker = typeof(MatchActor).GetInterfaces().Any(i => i.Name == "IEphemeralEntityActor");

            Assert.That(implementsMarker, Is.True,
                "MatchActor no longer implements IEphemeralEntityActor, so EntityConfigRegistry will refuse "
                + "MatchEntityConfig at startup. The vendored base in Backend/Server/SdkPreview must keep "
                + "deriving from EphemeralEntityActor.");
        }

        [Test]
        public void MatchActorDerivesFromTheVendoredBaseWhileItExists()
        {
            // A reminder rather than a constraint: when this fails because MatchActor now derives from the
            // SDK's own base, the vendored copy and this fixture should both be gone.
            Type vendored = Type.GetType("Game.Server.SdkPreview.MultiplayerEntityActorBase`2, Server");

            Assert.That(vendored, Is.Not.Null,
                "The vendored SDK base is gone. If this is the R39 upgrade, delete Backend/Server/SdkPreview, "
                + "point MatchActor at Metaplay.Server.MultiplayerEntity.EphemeralMultiplayerEntityActorBase, "
                + "and delete this fixture.");
        }

        /// <summary>
        /// The substitution the whole hidden-information design now rests on: one member receives the real
        /// operation and every other subscriber receives what the server staged — a no-op — at the same index,
        /// under the same checksums.
        /// <para>
        /// This is vendored SDK code (see SdkPreview/README.md), and the SDK's own tests cover the SDK's copy
        /// rather than ours. While the sample carries it, the sample tests it: if it ever substituted for the
        /// wrong subscriber, one seat would see the other's cards and nothing else here would notice.
        /// </para>
        /// </summary>
        [Test]
        public void AnOverrideReplacesOnlyItsOwnOperationAndKeepsTheChecksums()
        {
            ModelAction shared    = new NoopAction();
            ModelAction addressed = new NoopAction();

            EntityTimelineUpdateMessage update = new EntityTimelineUpdateMessage(
                new List<ModelAction> { shared, shared, shared },
                finalChecksum: 12345u,
                debugChecksums: null);

            MultiplayerEntityTimelineOverrideBuffer buffer = new MultiplayerEntityTimelineOverrideBuffer();
            Assert.That(buffer.IsEmpty, Is.True, "a subscriber with no overrides is handed the shared update untouched");

            buffer.Set(pendingOpIndex: 1, addressed);
            Assert.That(buffer.IsEmpty, Is.False);

            EntityTimelineUpdateMessage substituted = buffer.Consume(update);

            Assert.That(substituted.Operations[0], Is.SameAs(shared), "an un-overridden operation is untouched");
            Assert.That(substituted.Operations[1], Is.SameAs(addressed), "the addressed member gets the real operation");
            Assert.That(substituted.Operations[2], Is.SameAs(shared));
            Assert.That(substituted.FinalChecksum, Is.EqualTo(12345u),
                "the checksum is over the model, not the operations, so substituting must not move it");

            Assert.That(update.Operations[1], Is.SameAs(shared), "substitution must not mutate the shared update");
            Assert.That(buffer.IsEmpty, Is.True, "an override inside the flushed range is consumed by it");
        }

        /// <summary>
        /// An override for an operation that has not been flushed yet survives, re-keyed to the next flush.
        /// This is what makes several addressed operations in one actor turn land on the right members: the
        /// sample produces one per card, so a mulligan alone can queue a dozen.
        /// <para>
        /// Checked end to end — through a second <c>Consume</c> — rather than by reading the buffer's own
        /// bookkeeping, because the rebase is only correct if the survivor lands at the right index next time.
        /// </para>
        /// </summary>
        [Test]
        public void AnOverrideBeyondTheFlushSurvivesItRebased()
        {
            ModelAction shared  = new NoopAction();
            ModelAction outside = new NoopAction();

            MultiplayerEntityTimelineOverrideBuffer buffer = new MultiplayerEntityTimelineOverrideBuffer();
            buffer.Set(pendingOpIndex: 4, outside);

            // A flush of three operations: index 4 is not in it, so nothing is substituted here.
            EntityTimelineUpdateMessage first = buffer.Consume(new EntityTimelineUpdateMessage(
                new List<ModelAction> { shared, shared, shared }, finalChecksum: 1u, debugChecksums: null));

            Assert.That(first.Operations, Is.All.SameAs(shared), "the override was for an operation this flush did not carry");
            Assert.That(buffer.IsEmpty, Is.False, "and so it must still be waiting");

            // The next flush starts where that one ended, so the override now belongs at index 1.
            EntityTimelineUpdateMessage second = buffer.Consume(new EntityTimelineUpdateMessage(
                new List<ModelAction> { shared, shared, shared }, finalChecksum: 2u, debugChecksums: null));

            Assert.That(second.Operations[1], Is.SameAs(outside), "index 4 minus the 3 operations already flushed");
            Assert.That(second.Operations[0], Is.SameAs(shared));
            Assert.That(second.Operations[2], Is.SameAs(shared));
            Assert.That(buffer.IsEmpty, Is.True);
        }

        /// <summary>
        /// The precise trigger for deleting all of this: the SDK itself has the API the copy exists to
        /// provide.
        /// <para>
        /// The build also refuses on SDK 39 (see Server.csproj), but that keys on a version number, which is a
        /// proxy — <c>develop</c> was already <c>39.0.0-pre</c> before the change merged. This asks the real
        /// question, so it fails on the day un-vendoring becomes both possible and necessary, and not before.
        /// </para>
        /// </summary>
        [Test]
        public void TheSdkDoesNotYetHaveTheApiThisCopyExistsToProvide()
        {
            // Fully qualified deliberately: the vendored copy shares the type's simple name, which is the
            // shadowing this test exists to end.
            bool sdkHasIt = typeof(Metaplay.Server.MultiplayerEntity.MultiplayerEntityActorBase<,>)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(method => method.Name == "ExecuteActionPerMember");

            Assert.That(sdkHasIt, Is.False,
                "Metaplay.Server.MultiplayerEntity.MultiplayerEntityActorBase now has ExecuteActionPerMember, so the "
                + "vendored copy in Backend/Server/SdkPreview is obsolete and is shadowing the real thing. Delete that "
                + "folder, SdkPreviewInternalsVisible.cs and this fixture, then change MatchActor's base back to the "
                + "SDK's EphemeralMultiplayerEntityActorBase<MatchModel, MatchAction>. See SdkPreview/README.md.");
        }

        [Test]
        public void TheMatchEntityIsRegisteredAsEphemeral()
        {
            // The premise of the test above: the marker only matters because the config is ephemeral.
            Assert.That(new MatchEntityConfig(), Is.InstanceOf<EphemeralEntityConfig>());
            Assert.That(new MatchEntityConfig().EntityActorType, Is.EqualTo(typeof(MatchActor)));
        }
    }
}
