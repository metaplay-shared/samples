namespace Game.Logic
{
    /// <summary>
    /// Registry for game-specific <c>MetaMessage</c> codes, the counterpart of <see cref="ActionCodes"/> for
    /// messages rather than actions. Codes must be unique across the whole game, which includes the SDK's own
    /// <c>MessageCodesCore</c> (those run up into the nineteen thousands, so the game's start at 20000) and the
    /// server-internal messages, whose codes are kept with the server in <c>ServerMessageCodes</c>.
    /// </summary>
    public static class MessageCodes
    {
        public const int TimeTrialSubmitRequest = 20001;

        // 20002 is reserved and must not be reused.
        // 20003 to 20012 are the server's (ServerMessageCodes) or reserved.

        public const int TimeTrialStartRequest  = 20013;
        public const int TimeTrialStartOnMapRequest = 20014;
    }
}
