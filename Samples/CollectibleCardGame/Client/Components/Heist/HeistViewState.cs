using Game.Client.Services;
using Game.Logic;
using Metaplay.Core;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;

namespace Game.Client.Components.Heist;

/// <summary>
/// Everything <see cref="HeistScreen"/> draws, in one object, filled by whoever is driving the board — the
/// live page from <c>MatchService</c> and <c>CollectionService</c>, the offline scene preview from a table it
/// played itself. The same seam <c>BoardViewState</c> is, and for the same reason: the screen decides nothing,
/// so a screenshot of a scene is a screenshot of the real screen.
/// <para>
/// <b>The transfer arrives as a question rather than as an answer.</b> Which rank this account holds a card at
/// changes as the player clicks around the lineup, so the one thing the screen cannot be handed as a value is
/// <see cref="MyMoveFor"/> — and it is a function of the reader's own live collection, which is exactly the
/// state a preview must read rather than be told.
/// </para>
/// </summary>
public sealed class HeistViewState
{
    public MatchOutcomeRecord Result { get; set; } = default!;
    public MatchStakes Stakes { get; set; } = default!;
    public SharedGameConfig? Config { get; set; }

    /// <summary> Whether this client holds the winning seat. Only the winner has anything to do. </summary>
    public bool IsWinner { get; set; }

    public HeistStage Stage { get; set; } = HeistStage.Done;

    /// <summary> How many picks the tier owes, derived from the frozen tier and which seat won. </summary>
    public int PicksOwed { get; set; }

    /// <summary> Every card the loser played, the locked ones included. </summary>
    public List<HeistLootRow> Lineup { get; set; } = new List<HeistLootRow>();

    /// <summary> What has been taken, in pick order. </summary>
    public List<CardId> Picks { get; set; } = new List<CardId>();

    /// <summary> Whether any pick was the deterministic default rather than a choice. Shown on both clients. </summary>
    public bool AnyAutoDefaulted { get; set; }

    /// <summary> The pick clock, or null when none is in force. The winner's own; the loser waits on it. </summary>
    public MetaTime? DeadlineAt { get; set; }

    /// <summary> The table's clock as this client estimates it. Never the device's. </summary>
    public MetaTime Now { get; set; }

    /// <summary> Whether both accounts have folded the outcome in, which is when Play again is honest. </summary>
    public bool AllSeatsAcked { get; set; }

    /// <summary> The refusal on screen, or empty. A refused pick is said out loud, never absorbed. </summary>
    public string RefusalText { get; set; } = "";

    /// <summary> What taking one card would do to <em>this</em> account's copy, read live. </summary>
    public Func<CardId, HeistRankMove> MyMoveFor { get; set; } = _ => default;

    /// <summary>
    /// What rank this account holds one card at, now. The winner's side of a pick that has already landed has
    /// no "before" recorded anywhere — the payload deliberately carries no ranks — so the settled screen states
    /// what they hold rather than a delta it would have to invent.
    /// </summary>
    public Func<CardId, int> MyRankFor { get; set; } = _ => 0;

    public EventCallback<CardId> OnPick { get; set; }
    public EventCallback OnPlayAgain { get; set; }
    public EventCallback OnLeave { get; set; }

    public int RankMax => Config?.Global.RankMax ?? 5;

    public GlobalConfig? Global => Config?.Global;
}
