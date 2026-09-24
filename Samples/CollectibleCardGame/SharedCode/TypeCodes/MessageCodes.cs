namespace Game.Logic
{
    /// <summary>
    /// The game's <c>MetaMessage</c> type-code registry. The SDK has no reserved range for games and its own
    /// codes run up to roughly 19 800, so this game allocates from 30 000 upward in fixed-width per-subsystem
    /// blocks. A duplicate is a startup crash naming the code.
    /// <para>
    /// Rules: one block per subsystem, allocated in order inside the block, and a code is never recycled once
    /// a deployed client has spoken it. Client-facing messages live in this shared assembly and server-internal
    /// ones must not; namespace placement is enforced at startup, which is why the 30300 block's constants are
    /// here while its message types are in <c>Backend/Server/Match/</c>.
    /// </para>
    /// <para>
    /// This registry is for <c>[MetaMessage]</c> codes only. <c>ModelAction</c> codes are a different registry
    /// (<see cref="ActionCodes"/>), and the engine's event and intent hierarchies have their own
    /// (<see cref="MatchEventCodes"/>, <see cref="MatchIntentCodes"/>), so the same number in two of them is
    /// not a collision.
    /// </para>
    /// </summary>
    public static class MessageCodes
    {
        // 30000-30099  Match intents (client -> server, on the entity channel)
        public const int MatchIntent                    = 30000;
        public const int MatchSeatIntent                = 30001;
        // 30002 retired (MatchClockProbeRequest)

        // 30100-30199  Match notifications (server -> client, directed at one seat)
        // 30100 retired (MatchHandCorrection)
        public const int MatchIntentRefused             = 30101;
        // 30102 retired (MatchClockProbeResponse)

        // 30200-30299  Matchmaking, client-facing (server -> the searching player's own client). These two
        //              ride PlayerActorBase.SendToClient on the player's own session rather than an entity
        //              channel: there is no entity a client subscribes to for "my queue status".
        public const int MatchmakingStatusUpdate        = 30200;
        public const int MatchmakingEnded               = 30201;

        // 30300-30399  Server-internal match traffic. NOT in this assembly; see Backend/Server/Match/.
        public const int InternalMatchDeliverResult     = 30300;
        public const int InternalMatchDeliverResultOk   = 30301;
        public const int InternalMatchProbe             = 30302;
        public const int InternalMatchProbeOk           = 30303;
        public const int InternalMatchAbandon           = 30304;
        public const int InternalMatchAbandonOk         = 30305;
        public const int InternalPlayerSeatInMatchRequest  = 30306;
        public const int InternalPlayerSeatInMatchResponse = 30307;

        // The matchmaker's own traffic. Also server-internal, and in Backend/Server/Matchmaking/ — the
        // queue never crosses the wire to a client, so none of it belongs in this assembly.
        public const int InternalMatchmakingEnqueue           = 30308;
        public const int InternalMatchmakingCancel            = 30309;
        public const int InternalMatchmakingReservationReleased = 30310;
        public const int InternalMatchmakingSeatGone          = 30311;
        public const int InternalMatchmakingFormed            = 30312;

        // 30500-30599 Community: public read requests and authoritative server updates.
        public const int CommunityRequest = 30500;
        public const int CommunityResponse = 30501;
        public const int InternalCommunityRead = 30502;
        public const int InternalCommunityReadResponse = 30503;
        public const int InternalLeaderboardUpdate = 30504;
        public const int InternalMatchPopulation = 30505;
        public const int InternalQueuePopulationRequest = 30506;
        public const int InternalQueuePopulationResponse = 30507;
    }
}
