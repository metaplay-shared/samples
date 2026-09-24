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
using Metaplay.Core.Client;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System.Threading.Tasks;

using Metaplay.Server;
using Metaplay.Server.MultiplayerEntity;

namespace Game.Server.SdkPreview
{
    public abstract partial class MultiplayerEntityActorBase<TModel, TAction>
    {
        MetaDictionary<ClientSlot, AssociatedEntityRefBase> _associatedEntities = new();

        /// <summary>
        /// Adds an association with an entity to all sessions. The associated entity will
        /// be subscribed-to by the session and the state and updates will be delivered to the client.
        /// If there is already a previous association on the <see cref="ClientSlot"/> set by this
        /// entity, the previous association is removed first. It is an error to replace an association
        /// on a slot set by some other Entity.
        /// <para>
        /// The expected usage is:
        /// <list type="bullet">
        /// <item> Use AddEntityAssociation at the end of Entity.Initialize() to setup the pre-existing associations, for example if the Guild is supposed to be associated with a League Division. </item>
        /// <item> Whenever a new association is added, call AddEntityAssociation. </item>
        /// <item> Whenever an association is removed, call RemoveEntityAssociation. </item>
        /// </list>
        /// </para>
        /// </summary>
        protected void AddEntityAssociation(AssociatedEntityRefBase association)
        {
            // Existing is overwritten.
            _associatedEntities.AddOrReplace(association.GetClientSlot(), association);
            SendToAllSessions(new InternalSessionEntityAssociationUpdate(association.GetClientSlot(), association));
        }

        /// <summary>
        /// Removes association on the given slot from the current sessions. The entity will no longer visible to
        /// clients. It is an error to remove an association set by some other entity.
        /// </summary>
        protected void RemoveEntityAssociation(ClientSlot slot)
        {
            if (!_associatedEntities.Remove(slot))
                _log.Warning("Attempted to remove associated entity from slot {Slot}, but there were no entity associated", slot);

            SendToAllSessions(new InternalSessionEntityAssociationUpdate(slot, null));
        }

        [EntityAskHandler]
        async Task<EntityAskOk> HandleInternalEntityAssociatedEntityRefusedRequest(EntityId sessionId, InternalEntityAssociatedEntityRefusedRequest request)
        {
            _log.Debug("Handling InternalEntityAssociatedEntityRefusedRequest from {Entity} with {ReasonType}.", request.AssociationRef.AssociatedEntity, request.Refusal.GetType().ToGenericTypeString());

            EntityId playerId = SessionIdUtil.ToPlayerId(sessionId);

            // Filter out stale requests already
            foreach (AssociatedEntityRefBase currentAssociation in _associatedEntities.Values)
            {
                if (currentAssociation.AssociatedEntity != request.AssociationRef.AssociatedEntity)
                    continue;

                // \todo: check MemberInstanceId. If request is less, request was stale. If more, desynced and need to remove. For equal need to remove.

                bool handled = await OnAssociatedEntityRefusalAsync(playerId, request.AssociationRef, request.Refusal);
                if (!handled)
                {
                    _log.Warning("Unhandled association refusal from {Entity}. Actor.OnAssociatedEntityRefusalAsync", request.AssociationRef.AssociatedEntity);

                    // If we cannot remove the association, we refuse the call. This prevents futile retries.
                    throw new InvalidEntityAsk($"Entity association refusal was not handled. Entity {_entityId} OnAssociatedEntityRefusalAsync must handle refusal \"{request.Refusal.Message}\" from {request.AssociationRef.AssociatedEntity}.");
                }

                await PersistSnapshot();
                return EntityAskOk.Instance;
            }

            _log.Warning("Ignoring stale InternalEntityAssociatedEntityRefusedRequest from {Entity}. There was no such entity associated.", request.AssociationRef.AssociatedEntity);
            return EntityAskOk.Instance;
        }

        /// <summary>
        /// Called when a declared associated entity refused the claimed association reference. Implementation should correct the situation such that
        /// the association data is no longer incorrect. Implementation may for example clear the association to the entity (leave the associated entity)
        /// or re-establish the association (re-join the associated entity).
        /// </summary>
        /// <returns>true if refusal was handled</returns>
        protected virtual Task<bool> OnAssociatedEntityRefusalAsync(EntityId playerId, AssociatedEntityRefBase association, InternalEntitySubscribeRefusedBase refusal) => Task.FromResult<bool>(false);
    }
}
