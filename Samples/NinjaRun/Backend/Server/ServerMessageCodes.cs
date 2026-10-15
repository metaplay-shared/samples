namespace Game.Server
{
    /// <summary>
    /// Registry for the codes of the game's server-internal <c>MetaMessage</c>s. They share one code space with
    /// the shared messages in <see cref="Game.Logic.MessageCodes"/>, so a code is taken from the range that
    /// registry leaves to the server.
    /// </summary>
    public static class ServerMessageCodes
    {
        // Between the player actor and a course's ghost pool.
        public const int CourseGhostLineupRequest     = 20003;
        public const int CourseGhostLineupResponse    = 20004;
        public const int CourseGhostRunAccepted       = 20005;
        public const int CourseGhostRunAcceptedResult = 20006;

        // 20007 and 20008 are reserved and must not be reused.

        // Between the player actor and the map registry.
        public const int MapRegistryActiveSetRequest  = 20009;
        public const int MapRegistryActiveSetResponse = 20010;

        // 20011 and 20012 are reserved and must not be reused.
    }
}
