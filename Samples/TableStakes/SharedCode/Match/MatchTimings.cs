using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// The durations a match uses, supplied by the host because the engine reads no configuration itself. The server
    /// host fills this from runtime options and the browser host from constants, so the same engine runs in the
    /// browser, which has no runtime options (<c>docs/match.md</c>, "Timings come from the host"). The engine uses
    /// only <see cref="MoveDeadline"/> and <see cref="ResolvePause"/>. The other values are here so the match host and
    /// the bots read all match timings from one value. Settings that are not per match, such as the matchmaking fill
    /// wait, belong to the matchmaker's and the server's own configuration.
    /// </summary>
    [MetaSerializable]
    public struct MatchTimings
    {
        /// <summary>How long the table waits for every human seat to subscribe before play begins anyway.</summary>
        [MetaMember(1)] public MetaDuration JoinWindow;

        /// <summary>How long a connected seat on turn has to play before the host auto-plays its card.</summary>
        [MetaMember(2)] public MetaDuration MoveDeadline;

        /// <summary>The pause after the last card of a trick, during which no seat is on turn.</summary>
        [MetaMember(3)] public MetaDuration ResolvePause;

        /// <summary>Lower bound of a bot's normal think delay.</summary>
        [MetaMember(4)] public MetaDuration BotThinkDelayMin;

        /// <summary>Upper bound of a bot's normal think delay, and lower bound of its long think delay.</summary>
        [MetaMember(5)] public MetaDuration BotThinkDelayMax;

        /// <summary>Upper bound of a bot's long think delay (see <see cref="BotProfile.LongThinkChancePercent"/>).</summary>
        [MetaMember(6)] public MetaDuration BotThinkDelayOccasionalMax;

        /// <summary>
        /// Think delay of a covering bot whose owner is still connected. It gives the owner time to reclaim the seat
        /// by playing a card.
        /// </summary>
        [MetaMember(7)] public MetaDuration CoveredSeatReclaimDelay;

        /// <summary>How long a disconnected seat waits before a bot covers it.</summary>
        [MetaMember(8)] public MetaDuration DisconnectGrace;

        /// <summary>The longer grace after the table is restored from the database, when every client reconnects at once.</summary>
        [MetaMember(9)] public MetaDuration RestartReconnectGrace;

        /// <summary>How many move deadlines in a row a seat may miss before a bot covers it.</summary>
        [MetaMember(11)] public int StrikesBeforeCover;

        /// <summary>
        /// The shipped values (<c>docs/match.md</c>, "Runtime options"). A host may pass other values, and tests
        /// usually do.
        /// </summary>
        public static MatchTimings Default => new MatchTimings
        {
            JoinWindow                     = MetaDuration.FromSeconds(10),
            MoveDeadline                   = MetaDuration.FromSeconds(20),
            ResolvePause                   = MetaDuration.FromMilliseconds(1500),
            BotThinkDelayMin               = MetaDuration.FromMilliseconds(600),
            BotThinkDelayMax               = MetaDuration.FromMilliseconds(1800),
            BotThinkDelayOccasionalMax     = MetaDuration.FromSeconds(3),
            CoveredSeatReclaimDelay        = MetaDuration.FromSeconds(3),
            DisconnectGrace                = MetaDuration.FromSeconds(20),
            RestartReconnectGrace          = MetaDuration.FromSeconds(90),
            StrikesBeforeCover             = 2,
        };

        /// <summary>
        /// Every duration zero: no join window, deadlines, think delays, resolve pauses or grace. Used by tests
        /// that need a game to run without waiting. A table played out by bots uses
        /// <see cref="MoveTimings.Instant"/> instead.
        /// </summary>
        public static MatchTimings Instant => new MatchTimings
        {
            JoinWindow                     = MetaDuration.Zero,
            MoveDeadline                   = MetaDuration.Zero,
            ResolvePause                   = MetaDuration.Zero,
            BotThinkDelayMin               = MetaDuration.Zero,
            BotThinkDelayMax               = MetaDuration.Zero,
            BotThinkDelayOccasionalMax     = MetaDuration.Zero,
            CoveredSeatReclaimDelay        = MetaDuration.Zero,
            DisconnectGrace                = MetaDuration.Zero,
            RestartReconnectGrace          = MetaDuration.Zero,
            StrikesBeforeCover             = 2,
        };
    }
}
