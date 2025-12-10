// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.TypeCodes;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Client requests to join the idle leagues.
    /// Server responds with <see cref="PlayerJoinIdleLeagueResponse"/>
    /// </summary>
    [MetaMessage(MessageCodes.PlayerJoinIdleLeagueRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerJoinIdleLeagueRequest : MetaMessage
    {
        public static PlayerJoinIdleLeagueRequest Instance { get; } = new PlayerJoinIdleLeagueRequest();

        PlayerJoinIdleLeagueRequest() { }
    }

    /// <summary>
    /// Server's response to <see cref="PlayerJoinIdleLeagueRequest"/>.
    /// If the join was successful <see cref="JoinedDivision"/> is set.
    /// Otherwise <see cref="FailureReason"/> will be set.
    /// </summary>
    [MetaMessage(MessageCodes.PlayerJoinIdleLeagueResponse, MessageDirection.ServerToClient)]
    public class PlayerJoinIdleLeagueResponse : MetaMessage
    {
        public bool                   Success        { get; private set; }
        public LeagueJoinRefuseReason FailureReason  { get; private set; }
        public DivisionIndex          JoinedDivision { get; private set; }

        PlayerJoinIdleLeagueResponse() { }

        public PlayerJoinIdleLeagueResponse(bool success, LeagueJoinRefuseReason failureReason, DivisionIndex joinedDivision)
        {
            Success        = success;
            FailureReason  = failureReason;
            JoinedDivision = joinedDivision;
        }

        public static PlayerJoinIdleLeagueResponse ForSuccess(DivisionIndex joinedDivision)
            => new PlayerJoinIdleLeagueResponse(true, default, joinedDivision);

        public static PlayerJoinIdleLeagueResponse ForFailure(LeagueJoinRefuseReason failureReason)
            => new PlayerJoinIdleLeagueResponse(false, failureReason, default);
    }

    [MetaMessage(MessageCodes.PlayerPartyError, MessageDirection.ServerToClient)]
    public class PlayerPartyError : MetaMessage
    {
        public string Error { get; }

        [MetaDeserializationConstructor]
        public PlayerPartyError(string error)
        {
            Error = error;
        }
    }

    /// <summary>
    /// Debug message: client tells the PlayerActor to crash.
    /// </summary>
    [MetaMessage(MessageCodes.PlayerCrashActor, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerDebugCrashActor : MetaMessage
    {
        public string Reason { get; }

        [MetaDeserializationConstructor]
        public PlayerDebugCrashActor(string reason)
        {
            Reason = reason;
        }
    }
}
