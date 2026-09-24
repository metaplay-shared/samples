using Metaplay.Core;

namespace Game.Logic
{
    /// <summary>
    /// What the searching dialog shows while a table is being found: the headline, the detail line, whether the
    /// player can cancel, and the countdown seconds. The dialog does not announce the change from
    /// <see cref="MatchmakingStatus.Searching"/> to <see cref="MatchmakingStatus.SeatReserved"/>.
    /// <para>
    /// It is a pure shared class rather than code in the dialog so unit tests can check the text against the
    /// status and the countdown rounding (<c>docs/matchmaking.md</c>, "What the client sees").
    /// </para>
    /// </summary>
    public static class MatchmakingWaitText
    {
        /// <summary>Whether the searching dialog is shown for <paramref name="status"/>.</summary>
        public static bool IsWaiting(MatchmakingStatus status) =>
            status == MatchmakingStatus.Searching || status == MatchmakingStatus.SeatReserved;

        /// <summary>
        /// Whether Cancel is enabled. It is disabled once the player is seated, because the server refuses a
        /// cancel from then on (<c>docs/matchmaking.md</c>, "What the client sees"). This is the only visible
        /// change when the player is seated.
        /// </summary>
        public static bool CanCancel(MatchmakingStatus status) => status == MatchmakingStatus.Searching;

        /// <summary>
        /// The dialog title for the whole wait. Bots fill empty seats, so it promises a table, not an opponent.
        /// <para>
        /// It does not change when the player is seated. Seating follows within one message, so a changed title
        /// would flash too briefly to read (<c>docs/web-client.md</c>).
        /// </para>
        /// </summary>
        public const string Headline = "Finding you a table";

        /// <summary>
        /// The line under the headline.
        /// <para>
        /// When the countdown has ended, it says the table is about to arrive instead of going blank or restarting.
        /// The countdown time bounds only the queue wait, and forming the table takes a little longer.
        /// </para>
        /// <para>
        /// A seated player has no countdown time, so they see the same line as a player whose countdown ended.
        /// </para>
        /// </summary>
        public static string Detail(MetaDuration remaining) =>
            HasCountdown(remaining)
                ? "Players are joining. Computer players take any seat still empty."
                : "Any moment now…";

        /// <summary>
        /// Whether to show a countdown. There is none before the server's first answer, which arrives a round trip
        /// after the dialog opens, and none after the countdown has ended.
        /// </summary>
        public static bool HasCountdown(MetaDuration remaining) => remaining > MetaDuration.Zero;

        /// <summary>
        /// The largest number the countdown shows. It protects against overflow from a bad server time, such as
        /// a badly wrong clock or a corrupt message, and does not limit how long a wait may be. Larger values show
        /// as this value.
        /// </summary>
        public const int MaxSecondsShown = 3599;

        /// <summary>
        /// The countdown number: whole seconds rounded up, so any remaining time shows as at least one second
        /// and the countdown never shows zero while the wait is still running.
        /// <para>
        /// The value is clamped to <see cref="MaxSecondsShown"/> before it is narrowed to <c>int</c>, because
        /// <see cref="MetaDuration.Milliseconds"/> is a 64-bit count computed from a time the server sent.
        /// </para>
        /// </summary>
        public static int SecondsShown(MetaDuration remaining)
        {
            if (remaining <= MetaDuration.Zero)
                return 0;

            // Clamp before rounding up. Rounding adds to the millisecond count, which would overflow for a value
            // near the 64-bit maximum and wrap to a number that passes the clamp below.
            if (remaining.Milliseconds >= (long)MaxSecondsShown * 1000)
                return MaxSecondsShown;

            long seconds = (remaining.Milliseconds + 999) / 1000;
            return seconds >= MaxSecondsShown ? MaxSecondsShown : (int)seconds;
        }
    }
}
