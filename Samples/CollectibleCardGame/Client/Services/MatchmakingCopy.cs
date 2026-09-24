using Game.Logic;
using Metaplay.Core;

namespace Game.Client.Services;

/// <summary> What the searching dialog is showing, from the client's own point of view. </summary>
public enum MatchmakingDialogState
{
    /// <summary> No dialog. Either nothing was asked for, or a board is up. </summary>
    Hidden = 0,

    /// <summary>
    /// Waiting for the queue. Entered <em>optimistically</em>, on the tap, before the server has answered the
    /// entry action at all — a request lost on an already-dead connection would otherwise leave nothing on
    /// screen at all, and the alternative leaves the dialog up with a Cancel that goes exactly where the
    /// request went.
    /// </summary>
    Searching = 1,

    /// <summary>
    /// A cancel has been sent and the dialog is waiting for the answer rather than closing on the tap: a
    /// cancel that loses the race is refused and a board appears instead, and a dialog that had already
    /// dismissed itself would have to un-dismiss.
    /// </summary>
    Cancelling = 2,
}

/// <summary>
/// What the searching dialog says, as pure functions of the state and the one stamp the server sent. It is a
/// type of its own so a test can ask it away from the browser — the same reason the board's own trailing rule
/// and the clock's rejection policy are types (<c>Docs/matchmaking.md</c>: "What the dialog says for a
/// given status, and how the countdown rounds, is a pure function and is unit-tested as one").
/// </summary>
public static class MatchmakingCopy
{
    /// <summary> Which of the two waits is running. </summary>
    public static string Headline(MatchmakingDialogState state) => state switch
    {
        MatchmakingDialogState.Cancelling => "Leaving the queue…",
        MatchmakingDialogState.Searching  => "Finding you an opponent",
        _                                 => "",
    };

    /// <summary>
    /// The countdown, against the server's own bound on the wait rather than against anything the client
    /// invented. The stamp is an <b>upper bound</b>, not a prediction — a compatible human can arrive at any
    /// moment — so this finishes early gracefully and says "any moment now" rather than restarting the number
    /// or going negative when it runs out.
    /// <para>
    /// Rounded up, so a wait of forty-five seconds reads as forty-five for its first instant and the number
    /// never reads zero while there is still time on it.
    /// </para>
    /// </summary>
    public static string Countdown(MetaTime? byInstant, MetaTime now)
    {
        if (!byInstant.HasValue)
            return "";

        long remainingMs = (byInstant.Value - now).Milliseconds;
        if (remainingMs <= 0)
            return AnyMomentNow;

        long seconds = (remainingMs + 999) / 1000;

        return seconds == 1 ? "About 1 second left" : $"About {seconds} seconds left";
    }

    /// <summary> The copy the countdown lands on rather than a negative number or a restart. </summary>
    public const string AnyMomentNow = "Any moment now…";

    /// <summary>
    /// The line the dialog leaves behind when a search ends with no board. Every reason gets a sentence,
    /// because a dialog that vanished with nothing said is what a player reads as the game being broken.
    /// </summary>
    public static string EndedNotice(MatchmakingEndReason reason) => reason switch
    {
        MatchmakingEndReason.Cancelled  => "",
        MatchmakingEndReason.Refused    => "You are already in a game or already in the queue.",
        MatchmakingEndReason.SeatLost   => "That match got away. Tap Ranked to look again.",
        _                               => "The queue stopped answering. Tap Ranked to look again.",
    };

    /// <summary>
    /// Whether Cancel is still on offer. It is dropped the moment the client has asked to leave, and the true
    /// guarantee is on the server anyway — a late cancel is a harmless no-op there whatever this button did.
    /// </summary>
    public static bool ShowsCancel(MatchmakingDialogState state) => state == MatchmakingDialogState.Searching;
}
