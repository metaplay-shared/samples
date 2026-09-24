using Game.Logic;
using Microsoft.AspNetCore.Components;
using Game.Client.Services;
using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Client.Components.Board;

/// <summary>
/// Everything <see cref="BoardView"/> draws, and every press it can raise, in one object.
/// <para>
/// It is the board's seam. <c>MatchBoardPage</c> fills it from <c>MatchService</c> — the live board — and
/// <c>DevBoardPreview</c> fills it from a scene built offline, so the same components render both and a
/// screenshot of a scene is a screenshot of the real board rather than of a lookalike. Every member is a
/// value the board already had; nothing here decides anything.
/// </para>
/// <para>
/// Read-only from the view's side: the view never writes a member and never holds one across a render. The
/// page rebuilds the whole object each render, which is why plain properties are enough and why there is no
/// change notification to keep in step.
/// </para>
/// </summary>
public sealed class BoardViewState
{
    public Dictionary<CardInstanceId, TargetDelta> CritterDeltas { get; set; } = new Dictionary<CardInstanceId, TargetDelta>();
    public Dictionary<int, TargetDelta> DenDeltas { get; set; } = new Dictionary<int, TargetDelta>();
    public TargetDelta? DenDelta(int seat) => DenDeltas.TryGetValue(seat, out TargetDelta? delta) ? delta : null;

    // ---- the game ----

    /// <summary> The public rules state: the game itself. </summary>
    public MatchRulesState Rules { get; set; } = default!;

    /// <summary> The public board as the animation timeline has reached it. </summary>
    public BoardVisualState Visual { get; set; } = default!;

    /// <summary> The event currently animating, or null while the board is settled. </summary>
    public BoardBeat? Beat { get; set; }

    public SharedGameConfig? Config { get; set; }

    /// <summary> The Weather in force, or null before it is drawn. </summary>
    public WeatherInfo? Weather { get; set; }

    public MatchStakes Stakes { get; set; } = default!;

    public int LocalSeat { get; set; }
    public int OpponentSeat { get; set; }

    /// <summary>
    /// Which attachment this state belongs to. Only the browser motion layer reads it, and only to drop the
    /// bookkeeping it cannot otherwise know is stale.
    /// </summary>
    public int AttachmentEpoch { get; set; }

    public MatchSeat LocalRoster { get; set; } = default!;
    public MatchSeat OpponentRoster { get; set; } = default!;

    /// <summary> History reached by presentation; the feed reads its tail. </summary>
    public IReadOnlyList<MatchEvent> History { get; set; } = Array.Empty<MatchEvent>();

    /// <summary> A local scene preview has no live match connection. </summary>
    public bool IsPreview { get; set; }

    /// <summary> This seat's own hand, as the private channel delivered it. </summary>
    public List<HandCard> HandCards { get; set; } = new List<HandCard>();

    // ---- what the legality walk says is live ----

    public HashSet<CardInstanceId> PlayableCards { get; set; } = new HashSet<CardInstanceId>();
    public HashSet<CardInstanceId> Attackers { get; set; } = new HashSet<CardInstanceId>();
    public HashSet<CardInstanceId> TargetableCritters { get; set; } = new HashSet<CardInstanceId>();
    public List<int> TargetableDens { get; set; } = new List<int>();

    /// <summary> The cards the mulligan may put back, which is what the fan offers while it is open. </summary>
    public HashSet<CardInstanceId> ReplaceableCards { get; set; } = new HashSet<CardInstanceId>();

    /// <summary>
    /// What aiming at each critter would do, keyed by instance: a consequence on an enemy, a heal amount on
    /// a friend. Computed from public state plus the card in hand, which is all either side needs.
    /// </summary>
    public Dictionary<CardInstanceId, string> CritterTags { get; set; } = new Dictionary<CardInstanceId, string>();

    /// <summary> The same for a Den, keyed by seat. </summary>
    public Dictionary<int, string> DenTags { get; set; } = new Dictionary<int, string>();

    /// <summary>
    /// The number inside a heal tag, keyed the same way. The tag itself is copy — "+2 → 9" — and a fixture
    /// that had to parse it would be asserting the wording; these carry the amount as a value instead, and
    /// they come from the same shared query the words do.
    /// </summary>
    public Dictionary<CardInstanceId, int> CritterTagAmounts { get; set; } = new Dictionary<CardInstanceId, int>();

    public Dictionary<int, int> DenTagAmounts { get; set; } = new Dictionary<int, int>();

    // ---- local feedback: none of it game state ----

    public CardInstanceId? SelectedCard { get; set; }
    public CardInstanceId? SelectedAttacker { get; set; }
    public HashSet<CardInstanceId> MulliganMarks { get; set; } = new HashSet<CardInstanceId>();
    public HashSet<CardInstanceId> PeekKeeps { get; set; } = new HashSet<CardInstanceId>();

    /// <summary> The refusal on screen, as its reason code, or null. </summary>
    public string? RefusalReason { get; set; }
    public string RefusalText { get; set; } = "";

    /// <summary>
    /// Whether the transient "your turn ran out" notice is up. The page raises it on a strike it watched land
    /// and it expires on its own; the count behind it is never drawn as a running total.
    /// </summary>
    public bool ShowsStrikeNotice { get; set; }

    /// <summary>
    /// Whether a bot is playing the seat this client is looking through. Derived from the roster rather than
    /// carried separately, so this notice and the plaque's own computer mark cannot disagree.
    /// </summary>
    public bool IsLocalSeatCovered => LocalRoster?.Occupancy == SeatOccupancy.HumanCoveredByBot;

    /// <summary> Whether this covered seat's owner has asked for it back and takes it at the next turn boundary. </summary>
    public bool IsLocalSeatReclaimPending => IsLocalSeatCovered && LocalRoster?.ReclaimPending == true;

    /// <summary>
    /// Whether the game is on this seat's turn, on the <b>authoritative</b> rules state rather than the
    /// presented one — the covered-seat notice reads it to say whether "play anything" has anything to press
    /// right now, which is a question about what the table would accept and not about what is on screen.
    /// </summary>
    public bool IsLocalSeatOnTurn
        => Rules != null && Rules.Phase == MatchPhase.Playing && Rules.SeatOnTurn == LocalSeat;

    public TargetingLayer.TargetingKind TargetingState { get; set; } = TargetingLayer.TargetingKind.None;

    // ---- the clocks and the turn ----

    /// <summary> The table's clock as this client estimates it. Never the device's. </summary>
    public MetaTime Now { get; set; }

    /// <summary> The deadline the board may draw a ring for, or null for none in force. </summary>
    public MetaTime? DeadlineAt { get; set; }

    /// <summary> False while the board trails, which withholds the ring and the readout. </summary>
    public bool RingAllowed { get; set; } = true;

    /// <summary> Whose turn the screen says it is, or that it is withholding the answer. </summary>
    public string TurnIndicatorState { get; set; } = "";
    public bool IsTrailing { get; set; }
    public int QueueLength { get; set; }

    /// <summary> How many of this seat's own turns have gone by, counting from one. </summary>
    public int OwnTurnNumber { get; set; }

    public bool CanEndTurn { get; set; }
    public bool HasSomethingLeft { get; set; }

    /// <summary> How many things the turn still has in it, which is what the nudge counts. </summary>
    public int ActionsLeft { get; set; }

    // ---- the overlays ----

    /// <summary>
    /// Whether the <em>table</em> is still in the mulligan, on the presented clock. This is what draws the
    /// scrim: the board behind it is not playable yet, however this seat answered.
    /// </summary>
    public bool IsMulliganOpen { get; set; }

    /// <summary>
    /// Whether this seat may still answer the mulligan, on the authoritative clock — the same predicate the
    /// confirm handler checks. Everything that takes input reads this and nothing reads the other one: the
    /// two are different facts, and answering "is the mulligan open" from both is what left a lit, dead hand
    /// under a live-looking confirm.
    /// </summary>
    public bool CanAnswerMulligan { get; set; }

    public bool IsBusy { get; set; }

    public PendingChoiceView? PendingChoice { get; set; }

    /// <summary>
    /// Whether the pre-match reveal is still on screen. It is a panel over the mulligan rather than a gate in
    /// front of it, because the mulligan's own deadline is already running while the client boots — so it
    /// takes no pointer events and leaves on the first mulligan interaction or on its own short timeout.
    /// </summary>
    public bool ShowsPreMatchReveal { get; set; }

    public bool ShowsResultPanel { get; set; }
    public MatchOutcomeRecord? Result { get; set; }
    public bool AllSeatsAcked { get; set; }

    /// <summary>
    /// The Heist screen's own state, or null when this match ends on the plain result panel instead. Which of
    /// the two a finished match lands on is <c>HeistPresentation.ShowsHeistScreen</c>'s answer and nothing
    /// else's — one fact off the outcome record, so the two clients cannot disagree about whether a phase
    /// happened. Both branches sit behind <see cref="ShowsResultPanel"/>, which is what makes the Heist screen
    /// inherit the terminal-phase rule for free.
    /// </summary>
    public Heist.HeistViewState? Heist { get; set; }

    /// <summary> Whether the feed is showing more than its last three lines. </summary>
    public bool IsFeedExpanded { get; set; }

    public bool IsPileOpen { get; set; }
    public string PileTitle { get; set; } = "";
    public List<CardId> PileCards { get; set; } = new List<CardId>();

    // ---- the presses ----

    public EventCallback<CardInstanceId> OnHandCardDragStart { get; set; }
    public EventCallback<CardInstanceId> OnHandCardClick { get; set; }
    public EventCallback<CardInstanceId> OnCritterClick { get; set; }
    public EventCallback<int> OnDenClick { get; set; }
    public EventCallback OnEndTurn { get; set; }
    public EventCallback OnMulliganConfirm { get; set; }
    public EventCallback<CardInstanceId> OnPeekToggle { get; set; }
    public EventCallback OnPeekConfirm { get; set; }
    public EventCallback OnLeave { get; set; }
    public EventCallback OnPlayAgain { get; set; }
    public EventCallback<(int Seat, ZoneInspector.PileKind Kind)> OnOpenPile { get; set; }
    public EventCallback OnClosePile { get; set; }
    public EventCallback OnClearSelection { get; set; }
    public EventCallback OnToggleFeed { get; set; }

    /// <summary> A Den's full health, so its value reads as a fraction rather than a bare number. </summary>
    public int MaxDenHp { get; set; }

    /// <summary>
    /// Whether the acorn the maximum just gained is being shown arriving. A permanent ramp is the one mana
    /// change with nothing else on the board to show it — the acorn it adds arrives spent — so the row pops
    /// it while that beat runs.
    /// </summary>
    public bool PopNewManaAcorn { get; set; }

    /// <summary> What aiming at one Den would do, or null. </summary>
    public string? DenTag(int seat) => DenTags.TryGetValue(seat, out string? tag) ? tag : null;

    /// <summary> The amount inside that Den's tag, or null when it carries no number. </summary>
    public int? DenTagAmount(int seat) => DenTagAmounts.TryGetValue(seat, out int amount) ? amount : null;

    /// <summary> One seat's public zones and counters. </summary>
    public BoardVisualSeat Seat(int seat) => Visual.Seat(seat);

    public MatchSeat Roster(int seat) => seat == LocalSeat ? LocalRoster : OpponentRoster;
}
