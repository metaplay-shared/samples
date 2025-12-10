// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Game.Logic.TypeCodes
{
    /// <summary>
    /// Message code registry for game-specific messages.
    /// </summary>
    public static class MessageCodes
    {
        // Matchmaking (client->server)
        public const int IdleMatchingRequest = 18100;

        // Matchmaking (server->client)
        public const int IdleMatchingResponse = 18101;

        // Matchmaking (server internal)
        public const int InternalPlayerGetBattleAttackParamsRequest = 18102;
        public const int InternalPlayerGetBattleAttackParamsResponse = 18103;
        public const int InternalWinIdlerPvPBattleMessage = 18104;

        // Player Leagues (client->server)
        public const int PlayerJoinIdleLeagueRequest = 18200;

        // Player Leagues (server->client)
        public const int PlayerJoinIdleLeagueResponse = 18201;

        // Party system (server->server)
        public const int PlayerJoinOrUpdatePartyRequest = 18300;

        // Party system (server->client)
        public const int PlayerPartyError = 18301;

        // Miscellaneous debug messages
        // Crash PlayerActor (client->server)
        public const int PlayerCrashActor = 30000;

        // Add your message codes here
        // Example:
        //  public const int InternalMessageFoo = 15400;
    }
}
