using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Threading.Tasks;

namespace Game.Server.Matchmaking
{
    /// <summary>
    /// Matchmaker timing settings. Matchmaking runs only on the server, so unlike the match engine's timings
    /// these are read directly as runtime options (<c>docs/match.md</c>, "Timings come from the host").
    /// <para>
    /// Each of the player's two timeouts must be longer than the step it is a fallback for. Otherwise it fires while
    /// that step is still running, and the matchmaker and the player's actor disagree about whether the player got
    /// a seat:
    /// </para>
    /// <list type="bullet">
    /// <item><c>FillWait + SeatReservationAskTimeout &lt; SearchTimeout</c>. A searching player's reservation ask
    /// arrives at most this long after they joined the queue.</item>
    /// <item><c>SeatReservationAskTimeout + MintAskTimeout &lt; SeatReservationTimeout</c>. A reserved player's
    /// table is created at most this long after the reservation.</item>
    /// </list>
    /// <para>
    /// The two timeouts never run at the same time, because a reservation replaces the search timeout, so they
    /// need no order between them.
    /// </para>
    /// </summary>
    [RuntimeOptions("Matchmaking", isStatic: false, "How long the queue waits before it fills a table with bots.")]
    public class MatchmakingOptions : RuntimeOptionsBase
    {
        [MetaDescription("How long after the oldest waiter joined an incomplete queue is formed into a table anyway, with bots in the empty seats. A full table forms at once and never waits for this.")]
        public TimeSpan FillWait { get; private set; } = TimeSpan.FromSeconds(5);

        [MetaDescription("How long the matchmaker waits for a player's actor to answer a seat reservation. The asks for one table go out together, so this bounds the reservation step as a whole rather than one player at a time.")]
        public TimeSpan SeatReservationAskTimeout { get; private set; } = TimeSpan.FromSeconds(5);

        [MetaDescription("How long the matchmaker waits for a new table entity to set itself up. A mint that exceeds it is treated as failed and the waiters are queued again.")]
        public TimeSpan MintAskTimeout { get; private set; } = TimeSpan.FromSeconds(10);

        [MetaDescription("How long a player's actor holds a seat it committed to before it gives up and puts the player back on the menu. It is the backstop for a matchmaker that died mid-formation, so it must stay longer than the worst-case formation above it.")]
        public TimeSpan SeatReservationTimeout { get; private set; } = TimeSpan.FromSeconds(30);

        [MetaDescription("How long a player's actor leaves them on the searching screen before it gives up and puts them back on the menu. It is the backstop for a queue entry that no longer exists — a matchmaker that restarted, or a lost queue message — which nothing else would ever answer.")]
        public TimeSpan SearchTimeout { get; private set; } = TimeSpan.FromSeconds(60);

        public MetaDuration FillWaitDuration => MetaDuration.FromTimeSpan(FillWait);

        /// <summary>
        /// Checks that the values are in the order documented on <see cref="MatchmakingOptions"/>, and throws if
        /// they are not, which stops the server from starting or refuses the reload.
        /// <para>
        /// A wrong order causes no error at run time, because each component behaves correctly for its own
        /// timeout. Only players see the result, for example a player who is told the search failed and is then
        /// seated anyway. The check runs when the options load, before the server serves players.
        /// </para>
        /// </summary>
        public override Task OnLoadedAsync()
        {
            const string Consequence =
                "The player's actor would give up while the matchmaker is still seating them, so the player is told the "
                + "search failed and is then seated anyway. See MatchmakingOptions.";

            // The reservation asks for one table are sent in parallel, so the longest a search waits for its ask is
            // the fill wait plus one ask timeout, and the longest a formation takes is one ask plus the table creation.
            OptionsOrder.ThrowIfNotShorter("Matchmaking", FillWait + SeatReservationAskTimeout, "FillWait + SeatReservationAskTimeout", SearchTimeout, nameof(SearchTimeout), Consequence);
            OptionsOrder.ThrowIfNotShorter("Matchmaking", SeatReservationAskTimeout + MintAskTimeout, "SeatReservationAskTimeout + MintAskTimeout", SeatReservationTimeout, nameof(SeatReservationTimeout), Consequence);

            return Task.CompletedTask;
        }
    }
}
