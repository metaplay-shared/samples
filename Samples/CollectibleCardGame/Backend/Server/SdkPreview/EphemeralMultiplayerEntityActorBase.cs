// ---------------------------------------------------------------------------------------------------------
// VENDORED SDK SOURCE — NOT SAMPLE CODE.
//
// Copied from MetaplaySDK/Backend/Server/MultiplayerEntity/ at SDK 38.0.0 + SDK-263 (per-member timeline
// operations). Present only because SDK-263 ships in R39 and this sample targets released R38. Delete this
// folder on the R39 upgrade and point MatchActor back at the SDK's base. Differences from the original are
// marked `VENDORED CHANGE:`. See README.md in this folder.
// ---------------------------------------------------------------------------------------------------------

// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Threading.Tasks;

using Metaplay.Server;
using Metaplay.Server.MultiplayerEntity;

namespace Game.Server.SdkPreview
{
    /// <summary>
    /// Base class for actors for an ephemeral a Multiplayer Entity. Ephemeral entities are not stored into database and are always created anew.
    /// <para>
    /// Multiplayer Entity is an basic implementation for server-driven Entity. Multiple Clients may subscribe to an Multiplayer Entity and propose
    /// actions. Actions and Ticks executed by server are delivered back to client, allowing clients to see the changes in their copy of the Model.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The lifecycle of an entity is:
    /// <code>
    /// EphemeralMultiplayerEntityActorBase
    ///      |
    /// .---&gt;|
    /// |    | &lt;- (setup)
    /// |    |      Entity is woken up with InternalEntitySetupRequest message
    /// |    |
    /// |    |- Actor.Constructor()
    /// |    |       You should initialize readonly variables here
    /// |    |
    /// |    |- EntityActor.PreStart()
    /// |    |       Optional. Akka.Net Actor start hook. You should not do anything here.
    /// |    |       If overridden, You MUST call base.PreStart()
    /// |    |
    /// |    |- EntityActor.Initialize()
    /// |    |       Optional. Initialization before the first message is handled. You should not do anything here.
    /// |    |       If overridden, You MUST call base.Initialize()
    /// |    |
    /// |    |- MultiplayerEntityActorBase.OnSwitchedToModel()
    /// |    |       Optional. New model becomes active. You may attach Server-Listeners to the model.
    /// |    |
    /// |    |- MultiplayerEntityActorBase.SetUpModelAsync()
    /// |    |       Setups the initial Model state from InternalEntitySetupRequest parameters. A battle
    /// |    |       would set up its players.
    /// |    |
    /// |    |- MultiplayerEntityActorBase.OnEntityInitialized()
    /// |    |       The Main initialization method. The model has been set up.
    /// |    |
    /// |    |- [Entity is now running]
    /// |    |- [Message handlers]
    /// |    |
    /// |    |- EntityActor.OnShutdown()
    /// |    |       Optional. Cleanup method when just before actor is shut down.
    /// |    |
    /// |    |- EntityActor.PostStop()
    /// |    |       Optional. Akka.Net actor stop hook. You should not do anything here.
    /// |    |       If overridden, You MUST call base.PostStop()
    /// |    |
    /// |    |- Actor shuts down
    /// '----'
    /// </code>
    /// </remarks>
    public abstract partial class EphemeralMultiplayerEntityActorBase<TModel, TAction>
        // VENDORED CHANGE: IEphemeralEntityActor is SDK-internal; inherited via EphemeralEntityActor instead.
        : MultiplayerEntityActorBase<TModel, TAction>
        where TModel : class, IMultiplayerModel<TModel>, new()
        where TAction : ModelAction
    {
        /// <summary>
        /// <inheritdoc cref="EphemeralMultiplayerEntityActorBase{TModel, TAction}"/>
        /// </summary>
        protected EphemeralMultiplayerEntityActorBase() { }

        /// <summary>
        /// <inheritdoc cref="EphemeralMultiplayerEntityActorBase{TModel, TAction}"/>
        /// </summary>
        [Obsolete("Prefer using parameterless constructor.")]
        protected EphemeralMultiplayerEntityActorBase(EntityId entityId, string logChannelName = null) : base(entityId, logChannelName) { }

        public sealed override Task PersistSnapshot() => Task.CompletedTask;

        /// <summary>
        /// Called when the entity is set up. During and after this call, the <c>Model</c>
        /// exists and has been set up with the <see cref="SetUpModelAsync"/>.
        /// </summary>
        protected override Task OnEntityInitialized() => Task.CompletedTask;

        /// <summary>
        /// Called when the entity is being set up the first time with <see cref="InternalEntitySetupRequest"/>. Implementation should use the <paramref name="setupParams"/>
        /// to set up the <paramref name="model"/>. The given model is just initialized and does not need to be cleaned up before setup.
        /// </summary>
        protected abstract override Task SetUpModelAsync(TModel model, IMultiplayerEntitySetupParams setupParams);
    }
}
