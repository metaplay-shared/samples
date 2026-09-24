using Game.Logic;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Threading.Tasks;

namespace Game.Server.Match
{
    /// <summary>
    /// Timing settings for a server-hosted table (see the timing budget in <c>docs/match.md</c>).
    /// <para>
    /// The match engine reads no configuration. The server host builds its <see cref="MatchTimings"/> from these
    /// options with <see cref="ToMatchTimings"/>, and the browser host passes constants (<c>docs/match.md</c>,
    /// "Timings come from the host"). The defaults come from <see cref="MatchTimings.Default"/>, so both hosts
    /// agree. <see cref="ActorLingerAfterLastSubscriber"/> controls only the actor's lifetime, so
    /// <see cref="MatchTimings"/> has no matching member.
    /// </para>
    /// </summary>
    [RuntimeOptions("Match", isStatic: false, "Timings for one table of Table Stakes.")]
    public class MatchOptions : RuntimeOptionsBase
    {
        [MetaDescription("How long the table waits for every human seat to subscribe before play begins anyway. A human seat that never arrives is covered by a bot from that moment.")]
        public TimeSpan JoinWindow { get; private set; } = MatchTimings.Default.JoinWindow.ToTimeSpan();

        [MetaDescription("How long a CONNECTED seat on turn has to play a card before its card is auto-played by the strongest bot profile. A disconnected seat is governed by DisconnectGrace instead and is never given a deadline. Zero means no deadline is in force at all.")]
        public TimeSpan MoveDeadline { get; private set; } = MatchTimings.Default.MoveDeadline.ToTimeSpan();

        [MetaDescription("The beat after the fourth card of a trick lands, during which no seat is on turn.")]
        public TimeSpan ResolvePause { get; private set; } = MatchTimings.Default.ResolvePause.ToTimeSpan();

        [MetaDescription("Lower bound of a bot's think delay before it plays.")]
        public TimeSpan BotThinkDelayMin { get; private set; } = MatchTimings.Default.BotThinkDelayMin.ToTimeSpan();

        [MetaDescription("Upper bound of a bot's ordinary think delay.")]
        public TimeSpan BotThinkDelayMax { get; private set; } = MatchTimings.Default.BotThinkDelayMax.ToTimeSpan();

        [MetaDescription("Upper bound of a bot's occasional longer think delay.")]
        public TimeSpan BotThinkDelayOccasionalMax { get; private set; } = MatchTimings.Default.BotThinkDelayOccasionalMax.ToTimeSpan();

        [MetaDescription("Think delay of a covering bot whose owner is still connected, long enough for the owner to take the turn back by playing a card. A covered seat whose owner is gone plays at ordinary speed.")]
        public TimeSpan CoveredSeatReclaimDelay { get; private set; } = MatchTimings.Default.CoveredSeatReclaimDelay.ToTimeSpan();

        [MetaDescription("How long a disconnected seat is held before a bot covers it. A deliberate Leave skips this entirely.")]
        public TimeSpan DisconnectGrace { get; private set; } = MatchTimings.Default.DisconnectGrace.ToTimeSpan();

        [MetaDescription("The longer grace held after a server restart, when every client is reconnecting from cold at once. A table woken from the database holds every seat on this before it may conclude that nobody is coming back.")]
        public TimeSpan RestartReconnectGrace { get; private set; } = MatchTimings.Default.RestartReconnectGrace.ToTimeSpan();

        [MetaDescription("How long a table's actor stays alive after its last subscriber leaves. Must exceed DisconnectGrace and JoinWindow, or a table shuts down before those timers end. The server refuses to start otherwise.")]
        public TimeSpan ActorLingerAfterLastSubscriber { get; private set; } = TimeSpan.FromSeconds(30);

        [MetaDescription("Consecutive move-deadline lapses before a seat is handed to a covering bot. Any move from the seat's owner resets the count and takes the seat back.")]
        public int StrikesBeforeCover { get; private set; } = MatchTimings.Default.StrikesBeforeCover;

        /// <summary>
        /// Checks that the actor outlives the timers it runs after its last subscriber leaves, and throws if it does
        /// not, which stops the server from starting or refuses the reload. An actor that shut down first would
        /// leave the table in the database mid-game until something woke it again.
        /// </summary>
        public override Task OnLoadedAsync()
        {
            const string Consequence = "The table's actor would shut down before the timer ends, and the table would wait in the database until something woke it.";

            OptionsOrder.ThrowIfNotShorter("Match", DisconnectGrace, nameof(DisconnectGrace), ActorLingerAfterLastSubscriber, nameof(ActorLingerAfterLastSubscriber), Consequence);
            OptionsOrder.ThrowIfNotShorter("Match", JoinWindow, nameof(JoinWindow), ActorLingerAfterLastSubscriber, nameof(ActorLingerAfterLastSubscriber), Consequence);

            return Task.CompletedTask;
        }

        /// <summary>Converts these options to the <see cref="MatchTimings"/> that the match engine takes.</summary>
        public MatchTimings ToMatchTimings() => new MatchTimings
        {
            JoinWindow                     = MetaDuration.FromTimeSpan(JoinWindow),
            MoveDeadline                   = MetaDuration.FromTimeSpan(MoveDeadline),
            ResolvePause                   = MetaDuration.FromTimeSpan(ResolvePause),
            BotThinkDelayMin               = MetaDuration.FromTimeSpan(BotThinkDelayMin),
            BotThinkDelayMax               = MetaDuration.FromTimeSpan(BotThinkDelayMax),
            BotThinkDelayOccasionalMax     = MetaDuration.FromTimeSpan(BotThinkDelayOccasionalMax),
            CoveredSeatReclaimDelay        = MetaDuration.FromTimeSpan(CoveredSeatReclaimDelay),
            DisconnectGrace                = MetaDuration.FromTimeSpan(DisconnectGrace),
            RestartReconnectGrace          = MetaDuration.FromTimeSpan(RestartReconnectGrace),
            StrikesBeforeCover             = StrikesBeforeCover,
        };
    }
}
