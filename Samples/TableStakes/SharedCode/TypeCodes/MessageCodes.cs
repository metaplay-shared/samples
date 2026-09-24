namespace Game.Logic
{
    /// <summary>
    /// The registry of this game's <c>[MetaMessage]</c> type codes. The SDK reserves no message code range for
    /// games and fails server startup on a duplicate code, so this game allocates blocks of 100 from 30000 up,
    /// above the SDK's own codes, one block per subsystem. Every allocated code is listed here, even when its type
    /// is declared elsewhere, so this file alone shows whether a code is free.
    /// <para>
    /// The SDK checks at startup that client-facing messages are declared in shared code and that server-internal
    /// messages are not referenced from public code. Client-facing types therefore live in <c>SharedCode/</c> and
    /// server-internal types in the server assembly.
    /// </para>
    /// </summary>
    public static class MessageCodes
    {
        // ---- 30000-30099: Match, client-facing. Types live in SharedCode/Match/MatchMessages.cs. ----

        /// <summary>Client to server: play a named card at a named play index.</summary>
        public const int MatchPlayCardRequest = 30000;

        /// <summary>Server to client: the submitter's move was not played, and why.</summary>
        public const int MatchMoveRefused = 30001;

        /// <summary>Server to client: this seat's hand, as it stands at a named play index.</summary>
        public const int MatchHandDelivered = 30002;

        /// <summary>Client to server: request the host's current clock. The client measures the round trip itself.</summary>
        public const int MatchClockSyncRequest = 30003;

        /// <summary>Server to client: the host's clock at the time it answered.</summary>
        public const int MatchClockSyncResponse = 30004;

        /// <summary>Client to server: leave the table on purpose. Skips the reconnect grace. The game is still recorded.</summary>
        public const int MatchLeaveRequest = 30005;

        // 30006-30099: free.

        // ---- 30100-30199: Matchmaker, client-facing. Types live in SharedCode/Matchmaking/MatchmakingMessages.cs. ----

        /// <summary>Client to server: join the matchmaking queue.</summary>
        public const int MatchmakingEnterRequest = 30100;

        /// <summary>Client to server: leave the matchmaking queue.</summary>
        public const int MatchmakingCancelRequest = 30101;

        /// <summary>Server to client: the player's matchmaking status.</summary>
        public const int MatchmakingStatusUpdate = 30102;

        // 30103-30199: free.

        // ---- 30200-30299: Player profile and the loop, client-facing. Types live in SharedCode/Player/PlayerMessages.cs. ----

        /// <summary>Client to server: change the player's display name.</summary>
        public const int PlayerRenameRequest = 30200;

        /// <summary>Server to client: the player's current name, and the reason if the rename was rejected.</summary>
        public const int PlayerRenameResponse = 30201;

        /// <summary>Client to server: claim today's daily reward. Has no fields; the server decides the reward.</summary>
        public const int PlayerDailyRewardClaimRequest = 30202;

        /// <summary>Server to client: whether the claim was accepted, and the reason if it was refused.</summary>
        public const int PlayerDailyRewardClaimResponse = 30203;

        /// <summary>Client to server: spin the wheel. Carries only the expected spin ordinal.</summary>
        public const int PlayerWheelSpinRequest = 30204;

        /// <summary>Server to client: whether the spin was accepted, and the reason if it was refused.</summary>
        public const int PlayerWheelSpinResponse = 30205;

        // 30206-30299: free.

        // ---- 30300-30399: free. ----

        // ---- 30400-30499: Seasonal tournament, client-facing. Types live in SharedCode/Tournament/TournamentMessages.cs. ----

        /// <summary>Client to server: join the current tournament season.</summary>
        public const int TournamentJoinRequest = 30400;

        /// <summary>Server to client: whether the player joined a group, and the reason if not.</summary>
        public const int TournamentJoinResponse = 30401;

        /// <summary>Client to server: claim a participation milestone or a concluded season's placement reward.</summary>
        public const int TournamentClaimRequest = 30402;

        /// <summary>Server to client: the result of the claim, or the reason it was refused.</summary>
        public const int TournamentClaimResponse = 30403;

        // 30404-30499: free.

        // ---- 31000-31099: Matchmaker, server-internal. Types live in Backend/Server/Matchmaking/MatchmakerMessages.cs. ----

        /// <summary>Player actor to matchmaker: this player is waiting for a table.</summary>
        public const int MatchmakerEnterQueueMessage = 31000;

        /// <summary>Player actor to matchmaker: this player is no longer waiting.</summary>
        public const int MatchmakerLeaveQueueMessage = 31001;

        /// <summary>Matchmaker to player actor: request to reserve a seat at a table being formed.</summary>
        public const int MatchmakerReserveSeatRequest = 31002;

        /// <summary>Player actor to matchmaker: the answer to a seat reservation.</summary>
        public const int MatchmakerReserveSeatResponse = 31003;

        /// <summary>Matchmaker to player actor: you are seated at this table.</summary>
        public const int MatchmakerSeatAssignedMessage = 31004;

        /// <summary>Matchmaker to player actor: the table the seat was reserved for was not formed. The player stays queued.</summary>
        public const int MatchmakerSeatReleasedMessage = 31005;

        // 31006-31099: free.

        // ---- 31100-31199: Match, server-internal. Types live in Backend/Server/Match/MatchServerMessages.cs. ----

        /// <summary>Any entity to a table: check that the table is live. Used to detect a stale match reference.</summary>
        public const int MatchProbeRequest = 31100;

        /// <summary>A table's answer to a probe.</summary>
        public const int MatchProbeResponse = 31101;

        // 31102: free.

        /// <summary>Test only: expire every deadline the table is waiting on now, and report how many were expired.</summary>
        public const int MatchForceExpireRequest = 31103;

        /// <summary>The number of deadlines a force-expire request expired.</summary>
        public const int MatchForceExpireResponse = 31104;

        /// <summary>Table to a seated player's actor: the finished game's result. Retried until acknowledged, and recorded once.</summary>
        public const int MatchRecordResultRequest = 31105;

        /// <summary>Player actor to table: the result is recorded. The table stops resending it.</summary>
        public const int MatchRecordResultResponse = 31106;

        /// <summary>Table to a seated player's actor: the player no longer controls their seat, and why.</summary>
        public const int MatchSeatLostNotification = 31107;

        /// <summary>Table to the actor of a player who left on purpose: a bot covers the seat, so clear the match reference.</summary>
        public const int MatchReleaseNotification = 31108;

        // 31109-31199: free.

        // ---- 31200-31299: Weekly-event seeding, server-internal. Types live in
        //      Backend/Server/WeeklyEvent/WeeklyEventSeederMessages.cs. ----

        /// <summary>Test only: report the seeding passes so far, and optionally run the same pass the server runs.</summary>
        public const int WeeklyEventSeedPassRequest = 31200;

        /// <summary>What a seeding pass created, what it left unchanged, and how far ahead the timeline is now filled.</summary>
        public const int WeeklyEventSeedPassResponse = 31201;

        // 31202-31299: free.

        // ---- Serializable type codes ----
        //
        // [MetaSerializableDerived] codes must never change after the first live deployment, like message codes.
        // They are numbered per base class, so a code can only conflict with another derived type of the same
        // base, including the SDK's own derived types. The codes are declared on each type's attribute.
    }
}
