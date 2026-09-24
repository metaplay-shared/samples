using Game.Logic;
using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Client.Services;

public enum BoardBeatKind
{
    Other,
    Deal,
    Mulligan,
    TurnStart,
    Resource,
    Draw,
    Wake,
    CardPlay,
    CritterEnter,
    Attack,
    Damage,
    Bubble,
    Reveal,
    Heal,
    DenDamage,
    Death,
    Stats,
    Keywords,
    CardMove,
    Effect,
    Fizzle,
    TurnEnd,
    MatchEnd,
    Choice,
}

/// <summary> Browser-facing description of the event currently being presented. </summary>
public sealed class BoardBeat
{
    public int Sequence { get; }
    public BoardBeatKind Kind { get; }
    public MatchEvent Event { get; }
    /// <summary> The rules' action count the step this beat belongs to reached. </summary>
    public int ActionCount { get; }
    public MetaDuration Duration { get; }
    public float Progress { get; }
    public MetaDuration RemainingDuration
        => MetaDuration.FromMilliseconds((long)Math.Ceiling(Duration.Milliseconds * (1d - Progress)));
    public int Seat { get; }
    public int Amount { get; }
    public CardInstanceId Source { get; }
    public EffectTargetRef Target { get; }
    public IReadOnlyList<CardInstanceId> AffectedInstances { get; }
    public bool SuppressImpact { get; }
    public IReadOnlyList<MatchEvent> Events { get; }
    public BoardVisualCritter? EnteringCritter { get; }

    BoardBeat(
        int sequence,
        BoardBeatKind kind,
        MatchEvent ev,
        int actionCount,
        MetaDuration duration,
        float progress,
        int seat,
        int amount,
        CardInstanceId source,
        EffectTargetRef target,
        IReadOnlyList<CardInstanceId> affectedInstances,
        bool suppressImpact,
        IReadOnlyList<MatchEvent>? events,
        BoardVisualCritter? enteringCritter)
    {
        Sequence          = sequence;
        Kind              = kind;
        Event             = ev;
        ActionCount       = actionCount;
        Duration          = duration;
        Progress          = progress;
        Seat              = seat;
        Amount            = amount;
        Source            = source;
        Target            = target;
        AffectedInstances = affectedInstances;
        SuppressImpact    = suppressImpact;
        Events            = events ?? new[] { ev };
        EnteringCritter   = enteringCritter;
    }

    internal static BoardBeat FromEvent(
        MatchEvent ev,
        int actionCount,
        MetaDuration duration,
        float progress,
        int sequence,
        bool suppressImpact = false,
        IReadOnlyList<MatchEvent>? events = null,
        BoardVisualCritter? enteringCritter = null)
    {
        BoardBeatKind kind = KindOf(ev);
        int seat = MatchSeats.None;
        int amount = 0;
        CardInstanceId source = CardInstanceId.None;
        EffectTargetRef target = EffectTargetRef.None;
        IReadOnlyList<CardInstanceId> affected = Array.Empty<CardInstanceId>();

        switch (ev)
        {
            case MulliganResolvedEvent value: seat = value.Seat; break;
            case TurnStartedEvent value: seat = value.Seat; break;
            case ManaChangedEvent value: seat = value.Seat; break;
            case CardDrawnEvent value: seat = value.Seat; break;
            case DrawOverflowedEvent value: seat = value.Seat; break;
            case TuckeredOutEvent value: seat = value.Seat; amount = value.Damage; break;
            case CrittersWokeEvent value: seat = value.Seat; affected = value.Instances; break;
            case CardPlayedEvent value:
                seat = value.Seat;
                source = value.Instance;
                target = value.Target;
                amount = value.ManaSpent;
                affected = One(value.Instance);
                break;
            case CritterEnteredPlayEvent value: seat = value.Seat; source = value.Instance; affected = One(value.Instance); break;
            case AttackDeclaredEvent value: source = value.Attacker; target = value.Target; affected = One(value.Attacker); break;
            case DamageDealtEvent value: source = value.Source; target = value.Target; amount = value.Amount; affected = TargetInstance(value.Target); break;
            case BubblePoppedEvent value: affected = One(value.Instance); break;
            case SneakyRevealedEvent value: affected = One(value.Instance); break;
            case HealedEvent value: target = value.Target; amount = value.Amount; affected = TargetInstance(value.Target); break;
            case DenDamagedEvent value: seat = value.Seat; amount = value.Amount; target = EffectTargetRef.Den(value.Seat); break;
            case CritterDiedEvent value: seat = value.Seat; affected = One(value.Instance); break;
            case StatsChangedEvent value: affected = One(value.Instance); break;
            case KeywordsChangedEvent value: affected = One(value.Instance); break;
            case CardAddedToHandEvent value: seat = value.Seat; affected = value.Instance.IsValid ? One(value.Instance) : Array.Empty<CardInstanceId>(); break;
            case CardBouncedEvent value: seat = value.Seat; affected = One(value.Instance); break;
            case EffectResolvedEvent value: seat = value.ResolvingSeat; source = value.Source; break;
            case SummonFizzledEvent value: seat = value.Seat; amount = value.Count; break;
            case UnseenPoolChangedEvent value: seat = value.Seat; break;
            case TurnEndedEvent value: seat = value.Seat; break;
            case EffectChoiceRequestedEvent value: seat = value.Seat; amount = value.RevealedCount; break;
            case EffectChoiceResolvedEvent value: seat = value.Seat; amount = value.KeptCount; break;
        }

        return new BoardBeat(
            sequence,
            kind,
            ev,
            actionCount,
            duration,
            progress,
            seat,
            amount,
            source,
            target,
            affected,
            suppressImpact,
            events,
            enteringCritter);
    }

    static IReadOnlyList<CardInstanceId> One(CardInstanceId instance) => new[] { instance };

    static IReadOnlyList<CardInstanceId> TargetInstance(EffectTargetRef target)
        => target.IsCritter ? One(target.Critter) : Array.Empty<CardInstanceId>();

    static BoardBeatKind KindOf(MatchEvent ev)
        => ev switch
        {
            MatchDealtEvent            => BoardBeatKind.Deal,
            MulliganResolvedEvent      => BoardBeatKind.Mulligan,
            TurnStartedEvent           => BoardBeatKind.TurnStart,
            ManaChangedEvent           => BoardBeatKind.Resource,
            CardDrawnEvent             => BoardBeatKind.Draw,
            DrawOverflowedEvent        => BoardBeatKind.Draw,
            CrittersWokeEvent          => BoardBeatKind.Wake,
            CardPlayedEvent            => BoardBeatKind.CardPlay,
            CritterEnteredPlayEvent    => BoardBeatKind.CritterEnter,
            AttackDeclaredEvent        => BoardBeatKind.Attack,
            DamageDealtEvent           => BoardBeatKind.Damage,
            BubblePoppedEvent          => BoardBeatKind.Bubble,
            SneakyRevealedEvent        => BoardBeatKind.Reveal,
            HealedEvent                => BoardBeatKind.Heal,
            DenDamagedEvent            => BoardBeatKind.DenDamage,
            CritterDiedEvent           => BoardBeatKind.Death,
            StatsChangedEvent          => BoardBeatKind.Stats,
            KeywordsChangedEvent       => BoardBeatKind.Keywords,
            CardAddedToHandEvent       => BoardBeatKind.CardMove,
            CardBouncedEvent           => BoardBeatKind.CardMove,
            EffectResolvedEvent        => BoardBeatKind.Effect,
            SummonFizzledEvent         => BoardBeatKind.Fizzle,
            TurnEndedEvent             => BoardBeatKind.TurnEnd,
            MatchEndedEvent            => BoardBeatKind.MatchEnd,
            EffectChoiceRequestedEvent => BoardBeatKind.Choice,
            EffectChoiceResolvedEvent  => BoardBeatKind.Choice,
            _                          => BoardBeatKind.Other,
        };
}
