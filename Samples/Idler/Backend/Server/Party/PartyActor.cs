using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Logic;
using Game.Logic.TypeCodes;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server;
using Metaplay.Server.MultiplayerEntity;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using Serilog;

namespace Game.Server.Party
{
    [EntityConfig]
    public class PartyConfig : EphemeralEntityConfig
    {
        public override EntityKind EntityKind => EntityKindGame.Party;
        public override Type EntityActorType => typeof(PartyActor);
        public override NodeSetPlacement NodeSetPlacement => NodeSetPlacement.Logic;
        public override IShardingStrategy ShardingStrategy => ShardingStrategies.CreateStaticSharded();
        public override TimeSpan ShardShutdownTimeout => TimeSpan.FromSeconds(5);
    }

    [MetaSerializableDerived(1)]
    public class PartySetupParams : IMultiplayerEntitySetupParams
    {
        public EntityId Owner { get; }
        public string OwnerName { get; }

        [MetaDeserializationConstructor]
        public PartySetupParams(EntityId owner, string ownerName)
        {
            Owner = owner;
            OwnerName = ownerName;
        }
    }

    [MetaMessage(MessageCodes.PlayerJoinOrUpdatePartyRequest, MessageDirection.ServerInternal)]
    public class PartyJoinOrUpdateRequest : EntityAskRequest<EntityAskOk>
    {
        public string PlayerName;
    }

    public class PartyActor : EphemeralMultiplayerEntityActorBase<PartyModel, PartyAction>
    {
        // We don't have any time-based logic in our party, so disable the ticking functionality here.
        protected override bool IsTicking => false;

        // Initialize the PartyModel from setup parameters by adding the creator as the single member of the party,
        // and remembering who the creator was.
        protected override Task SetUpModelAsync(PartyModel model, IMultiplayerEntitySetupParams setupParams)
        {
            PartySetupParams partySetupParams = (PartySetupParams)setupParams;

            model.Creator = partySetupParams.Owner;
            model.Members[partySetupParams.Owner] = new PartyMember(partySetupParams.OwnerName, isOnline: false);
            return Task.CompletedTask;
        }

        protected override Task<InternalEntitySubscribeResponseBase> OnClientSessionStart(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase,
            List<AssociatedEntityRefBase> associatedEntities)
        {
            // Validate that the player is a member of this party
            if (!Model.Members.TryGetValue(playerId, out PartyMember memberInfo))
                throw new InternalEntitySubscribeRefusedBase.Builtins.NotAParticipant();

            // Set online status
            if (!memberInfo.IsOnline)
                ExecuteAction(new UpdatePartyMember(playerId, memberInfo.WithOnlineStatus(isOnline: true)));
            return base.OnClientSessionStart(sessionId, playerId, requestBase, associatedEntities);
        }

        protected override void OnParticipantSessionEnded(EntitySubscriber session)
        {
            EntityId playerId = SessionIdUtil.ToPlayerId(session.EntityId);

            // Validate that the player is a member of this party
            if (!Model.Members.TryGetValue(playerId, out PartyMember memberInfo))
            {
                _log.Warning("Participant session ended for a player not currently in party: {PlayerId}", playerId);
                return;
            }
            // Clear online status
            if (memberInfo.IsOnline)
                ExecuteAction(new UpdatePartyMember(playerId, memberInfo.WithOnlineStatus(isOnline: false)));
        }

        protected override bool ValidateClientOriginatingAction(ClientPeerState client, PartyAction action)
        {
            if (action is PartyClientAction clientAction)
                return clientAction.ValidateOnServer(client.PlayerId);
            return true;
        }

        [EntityAskHandler]
        public EntityAskOk HandlePlayerJoinOrUpdateRequest(EntityId player, PartyJoinOrUpdateRequest request)
        {
            // Refuse join if entity hasn't been setup
            if (Model == null)
                throw new InternalEntityAskNotSetUpRefusal();

            // Retain member online status. If member didn't exist this is a new join and therefore member
            // is initially offline.
            bool isOnline = Model.Members.TryGetValue(player, out PartyMember existingMember) &&
                existingMember.IsOnline;

            PartyMember memberData = new PartyMember(request.PlayerName, isOnline);
            ExecuteAction(new UpdatePartyMember(player, memberData));
            return EntityAskOk.Instance;
        }
    }

}