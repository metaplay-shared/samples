// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.Matchmaking;
using Game.Server.Matchmaking;
using Game.Server.Player;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Server;
using Metaplay.Server.Matchmaking;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Server
{
    [EntityConfig]
    public class SessionConfig : SessionConfigBase
    {
        public override Type EntityActorType => typeof(SessionActor);
    }

    public class SessionActor : SessionActorBase
    {
        /// <inheritdoc />
        protected override async Task<bool> GameTryHandleIncomingPayloadMessage(MessageRoutingRule routingRule, MetaMessage message)
        {
            if (routingRule is MessageRoutingRuleSession)
            {
                switch (message)
                {
                    case IdleMatchingRequest matchingRequest:
                        await HandleMatchingRequest(matchingRequest);
                        return true;
                }
            }
            return false;
        }

        async Task HandleMatchingRequest(IdleMatchingRequest _)
        {
            InternalPlayerGetBattleAttackParamsResponse attackParams =
                await EntityAskAsync(
                    PlayerId, InternalPlayerGetBattleAttackParamsRequest.Instance);

            IdlerMatchmakerQuery matchmakerQuery = new IdlerMatchmakerQuery(PlayerId, attackParams.AttackMmr,
                 attackParams.HighestLevelProducer);

            EntityId matchmakerId = IdlerAsyncMatchmakerActor.Entities.GetQueryableMatchmakersRandom().First();

            AsyncMatchmakingResponse response = await EntityAskAsync(
                matchmakerId,
                new AsyncMatchmakingRequest(0, matchmakerQuery, null));

            if (response.ResponseType == MatchmakingResponseType.Success)
            {
                // Simulate an auto-battle. Rather than simulating any combat, we simply compare (simulated) MMRs to resolve the winner.
                int defenseMmr = response.GetDeserializedModel<IdlerMatchmakerPlayerModel>().Value.DefenseMmr;
                int attackMmr = attackParams.AttackMmr;

                if (attackMmr > defenseMmr)
                {
                    CastMessage(PlayerId, InternalWinIdlerPvPBattleMessage.Instance);
                    
                    SendOutgoingPayloadMessage(
                        new IdleMatchingResponse(isSuccess: true, didWinBattle: true));
                }
                else
                {
                    SendOutgoingPayloadMessage(
                        new IdleMatchingResponse(isSuccess: true, didWinBattle: false));
                }
            }
            else
            {
                SendOutgoingPayloadMessage(
                    new IdleMatchingResponse(isSuccess: false, didWinBattle: false));
            }
        }
    }
}
