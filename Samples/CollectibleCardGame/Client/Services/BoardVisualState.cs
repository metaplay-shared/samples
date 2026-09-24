using Game.Logic;
using System.Collections.Generic;

namespace Game.Client.Services;

/// <summary>
/// The public board as presentation has reached it. It deliberately mirrors only values the board draws and
/// never contains the identity of a hidden card.
/// </summary>
public sealed class BoardVisualState
{
    public int Turn { get; set; }
    /// <summary> The rules' action count this state was taken at: which step the board has reached. </summary>
    public int ActionCount { get; set; }
    public MatchPhase Phase { get; set; }
    public int SeatOnTurn { get; set; }
    public WeatherId? Weather { get; set; }
    public BoardVisualSeat[] Seats { get; }
    public Dictionary<CardInstanceId, BoardVisualCard> KnownCards { get; }

    public BoardVisualState(
        int turn,
        int actionCount,
        MatchPhase phase,
        int seatOnTurn,
        BoardVisualSeat seat0,
        BoardVisualSeat seat1,
        Dictionary<CardInstanceId, BoardVisualCard>? knownCards = null,
        WeatherId? weather = null)
    {
        Turn        = turn;
        ActionCount = actionCount;
        Phase      = phase;
        SeatOnTurn = seatOnTurn;
        Weather    = weather;
        Seats      = new[] { seat0, seat1 };
        KnownCards = knownCards ?? new Dictionary<CardInstanceId, BoardVisualCard>();
    }

    public BoardVisualSeat Seat(int seat) => Seats[seat];

    public BoardVisualCritter? FindCritter(CardInstanceId instance)
    {
        for (int seat = 0; seat < Seats.Length; seat++)
        {
            BoardVisualCritter? critter = Seats[seat].FindCritter(instance);
            if (critter != null)
                return critter;
        }

        return null;
    }

    public static BoardVisualState FromRules(MatchRulesState rules)
    {
        Dictionary<CardInstanceId, BoardVisualCard> knownCards = new Dictionary<CardInstanceId, BoardVisualCard>();
        foreach (CardInstance instance in rules.Instances)
        {
            if (instance.IsKnown)
                knownCards[instance.Id] = new BoardVisualCard(instance.CardId, instance.Rank, instance.Owner, instance.Place);
        }

        BoardVisualSeat[] seats = new BoardVisualSeat[MatchSeats.Count];
        for (int seat = 0; seat < MatchSeats.Count; seat++)
        {
            SeatState source = rules.Seat(seat);
            BoardVisualSeat destination = new BoardVisualSeat(
                source.DenHp,
                source.Mana,
                source.MaxMana,
                source.HandCount,
                source.DeckCount,
                source.UnseenPool.Count,
                source.TuckeredOutTicks);

            foreach (BoardCritter sourceCritter in source.Board)
            {
                CardInstance instance = rules.Instance(sourceCritter.Id);
                destination.Board.Add(new BoardVisualCritter(
                    sourceCritter.Id,
                    instance.IsKnown ? instance.CardId : null,
                    instance.Rank,
                    seat,
                    sourceCritter.Attack,
                    sourceCritter.MaxHealth,
                    sourceCritter.Damage,
                    sourceCritter.Keywords,
                    sourceCritter.IsSleepy,
                    sourceCritter.HasAttackedThisTurn,
                    sourceCritter.BubbleIntact));
            }

            foreach (CardInstanceId graveInstance in source.Graveyard)
                destination.Graveyard.Add(graveInstance);

            seats[seat] = destination;
        }

        return new BoardVisualState(
            rules.Turn,
            rules.ActionCount,
            rules.Phase,
            rules.SeatOnTurn,
            seats[0],
            seats[1],
            knownCards,
            rules.Weather?.Ref.WeatherId);
    }

    public BoardVisualState Clone()
    {
        Dictionary<CardInstanceId, BoardVisualCard> knownCards = new Dictionary<CardInstanceId, BoardVisualCard>();
        foreach ((CardInstanceId instance, BoardVisualCard card) in KnownCards)
            knownCards[instance] = card;

        return new BoardVisualState(Turn, ActionCount, Phase, SeatOnTurn, Seats[0].Clone(), Seats[1].Clone(), knownCards, Weather);
    }
}

public readonly struct BoardVisualCard
{
    public readonly CardId Card;
    public readonly int Rank;
    public readonly int Owner;
    public readonly CardPlace Place;

    public BoardVisualCard(CardId card, int rank, int owner, CardPlace place)
    {
        Card  = card;
        Rank  = rank;
        Owner = owner;
        Place = place;
    }

    public BoardVisualCard At(CardPlace place) => new BoardVisualCard(Card, Rank, Owner, place);
}

public sealed class BoardVisualSeat
{
    public int DenHp { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public int HandCount { get; set; }
    public int DeckCount { get; set; }
    public int UnseenCount { get; set; }
    public int TuckeredOutTicks { get; set; }
    public List<BoardVisualCritter> Board { get; }
    public List<CardInstanceId> Graveyard { get; }

    public BoardVisualSeat(
        int denHp,
        int mana,
        int maxMana,
        int handCount,
        int deckCount,
        int unseenCount,
        int tuckeredOutTicks = 0)
    {
        DenHp       = denHp;
        Mana        = mana;
        MaxMana     = maxMana;
        HandCount   = handCount;
        DeckCount   = deckCount;
        UnseenCount = unseenCount;
        TuckeredOutTicks = tuckeredOutTicks;
        Board       = new List<BoardVisualCritter>();
        Graveyard   = new List<CardInstanceId>();
    }

    public BoardVisualCritter? FindCritter(CardInstanceId instance)
    {
        for (int ndx = 0; ndx < Board.Count; ndx++)
        {
            if (Board[ndx].Instance == instance)
                return Board[ndx];
        }

        return null;
    }

    public BoardVisualSeat Clone()
    {
        BoardVisualSeat clone = new BoardVisualSeat(
            DenHp,
            Mana,
            MaxMana,
            HandCount,
            DeckCount,
            UnseenCount,
            TuckeredOutTicks);
        foreach (BoardVisualCritter critter in Board)
            clone.Board.Add(critter.Clone());
        clone.Graveyard.AddRange(Graveyard);
        return clone;
    }
}

public sealed class BoardVisualCritter
{
    public CardInstanceId Instance { get; }
    public CardId? Card { get; }
    public int Rank { get; }
    public int Owner { get; }
    public int Attack { get; set; }
    public int MaxHealth { get; set; }
    public int Damage { get; set; }
    public KeywordFlags Keywords { get; set; }
    public bool IsSleepy { get; set; }
    public bool HasAttackedThisTurn { get; set; }
    public bool BubbleIntact { get; set; }
    public int CurrentHealth => MaxHealth - Damage;

    public BoardVisualCritter(
        CardInstanceId instance,
        CardId? card,
        int rank,
        int owner,
        int attack,
        int maxHealth,
        int damage,
        KeywordFlags keywords,
        bool isSleepy,
        bool hasAttackedThisTurn,
        bool bubbleIntact)
    {
        Instance            = instance;
        Card                = card;
        Rank                = rank;
        Owner               = owner;
        Attack              = attack;
        MaxHealth           = maxHealth;
        Damage              = damage;
        Keywords            = keywords;
        IsSleepy            = isSleepy;
        HasAttackedThisTurn = hasAttackedThisTurn;
        BubbleIntact        = bubbleIntact;
    }

    public BoardVisualCritter Clone()
        => new BoardVisualCritter(
            Instance,
            Card,
            Rank,
            Owner,
            Attack,
            MaxHealth,
            Damage,
            Keywords,
            IsSleepy,
            HasAttackedThisTurn,
            BubbleIntact);
}

/// <summary> Applies one public event to a detached presentation state. </summary>
public static class BoardVisualReducer
{
    public static BoardVisualState Apply(BoardVisualState current, MatchEvent ev)
    {
        BoardVisualState next = current.Clone();

        switch (ev)
        {
            case MatchDealtEvent dealt:
                next.Weather = dealt.Weather;
                next.Seat(0).HandCount = dealt.Seat0HandCount;
                next.Seat(0).DeckCount = dealt.Seat0DeckCount;
                next.Seat(1).HandCount = dealt.Seat1HandCount;
                next.Seat(1).DeckCount = dealt.Seat1DeckCount;
                break;

            case TuckeredOutEvent tuckeredOut:
                next.Seat(tuckeredOut.Seat).TuckeredOutTicks = tuckeredOut.TickIndex;
                break;

            case MulliganResolvedEvent mulligan:
                next.Seat(mulligan.Seat).HandCount = mulligan.HandCount;
                next.Seat(mulligan.Seat).DeckCount = mulligan.DeckCount;
                break;

            case TurnStartedEvent turnStarted:
                next.SeatOnTurn = turnStarted.Seat;
                break;

            case ManaChangedEvent mana:
                next.Seat(mana.Seat).Mana = mana.Mana;
                next.Seat(mana.Seat).MaxMana = mana.MaxMana;
                break;

            case CardDrawnEvent drawn:
                next.Seat(drawn.Seat).HandCount = drawn.HandCount;
                next.Seat(drawn.Seat).DeckCount = drawn.DeckCount;
                break;

            case DrawOverflowedEvent overflowed:
                next.Seat(overflowed.Seat).HandCount = overflowed.HandCount;
                next.Seat(overflowed.Seat).DeckCount = overflowed.DeckCount;
                break;

            case CrittersWokeEvent woke:
                foreach (CardInstanceId instance in woke.Instances)
                {
                    BoardVisualCritter? critter = next.FindCritter(instance);
                    if (critter != null)
                        critter.IsSleepy = false;
                }
                break;

            case CardPlayedEvent played:
                BoardVisualSeat playingSeat = next.Seat(played.Seat);
                playingSeat.HandCount = System.Math.Max(0, playingSeat.HandCount - 1);
                next.KnownCards[played.Instance] = new BoardVisualCard(played.Card, played.Rank, played.Seat, CardPlace.Limbo);
                break;

            case CritterEnteredPlayEvent entered:
                int enteredRank = next.KnownCards.TryGetValue(entered.Instance, out BoardVisualCard enteredCard)
                    ? enteredCard.Rank
                    : 0;
                next.Seat(entered.Seat).Board.Add(new BoardVisualCritter(
                    entered.Instance,
                    entered.Card,
                    enteredRank,
                    entered.Seat,
                    entered.Attack,
                    entered.MaxHealth,
                    0,
                    entered.Keywords,
                    entered.IsSleepy,
                    false,
                    (entered.Keywords & KeywordFlags.Bubble) != 0));
                next.KnownCards[entered.Instance] = new BoardVisualCard(entered.Card, enteredRank, entered.Seat, CardPlace.Board);
                break;

            case AttackDeclaredEvent attack:
                BoardVisualCritter? attacker = next.FindCritter(attack.Attacker);
                if (attacker != null)
                    attacker.HasAttackedThisTurn = true;
                break;

            case DamageDealtEvent damage when damage.Target.IsCritter && !damage.AbsorbedByBubble:
                BoardVisualCritter? damaged = next.FindCritter(damage.Target.Critter);
                if (damaged != null)
                    damaged.Damage += damage.Amount;
                break;

            case BubblePoppedEvent bubble:
                BoardVisualCritter? bubbled = next.FindCritter(bubble.Instance);
                if (bubbled != null)
                    bubbled.BubbleIntact = false;
                break;

            case SneakyRevealedEvent sneaky:
                BoardVisualCritter? revealed = next.FindCritter(sneaky.Instance);
                if (revealed != null)
                    revealed.Keywords &= ~KeywordFlags.Sneaky;
                break;

            case HealedEvent healed when healed.Target.IsDen:
                next.Seat(healed.Target.DenSeat).DenHp = healed.NewValue;
                break;

            case HealedEvent healed when healed.Target.IsCritter:
                BoardVisualCritter? healedCritter = next.FindCritter(healed.Target.Critter);
                if (healedCritter != null)
                    healedCritter.Damage = System.Math.Max(0, healedCritter.MaxHealth - healed.NewValue);
                break;

            case DenDamagedEvent den:
                next.Seat(den.Seat).DenHp = den.NewHp;
                break;

            case CritterDiedEvent died:
                BoardVisualSeat deadSeat = next.Seat(died.Seat);
                deadSeat.Board.RemoveAll(critter => critter.Instance == died.Instance);
                deadSeat.Graveyard.Add(died.Instance);
                if (next.KnownCards.TryGetValue(died.Instance, out BoardVisualCard deadCard))
                    next.KnownCards[died.Instance] = deadCard.At(CardPlace.Graveyard);
                break;

            case StatsChangedEvent stats:
                BoardVisualCritter? changedStats = next.FindCritter(stats.Instance);
                if (changedStats != null)
                {
                    changedStats.Attack = stats.Attack;
                    changedStats.MaxHealth = stats.MaxHealth;
                    changedStats.Damage = stats.Damage;
                }
                break;

            case KeywordsChangedEvent keywords:
                BoardVisualCritter? changedKeywords = next.FindCritter(keywords.Instance);
                if (changedKeywords != null)
                    changedKeywords.Keywords = keywords.Keywords;
                break;

            case CardAddedToHandEvent added:
                next.Seat(added.Seat).HandCount = added.HandCount;
                if (added.Instance.IsValid && added.Card != null)
                {
                    int rank = next.KnownCards.TryGetValue(added.Instance, out BoardVisualCard addedCard) ? addedCard.Rank : 0;
                    next.KnownCards[added.Instance] = new BoardVisualCard(added.Card, rank, added.Seat, CardPlace.Hand);
                }
                break;

            case CardBouncedEvent bounced:
                BoardVisualSeat bouncedSeat = next.Seat(bounced.Seat);
                bouncedSeat.Board.RemoveAll(critter => critter.Instance == bounced.Instance);
                bouncedSeat.HandCount = bounced.HandCount;
                if (next.KnownCards.TryGetValue(bounced.Instance, out BoardVisualCard bouncedCard))
                    next.KnownCards[bounced.Instance] = bouncedCard.At(CardPlace.Hand);
                break;

            case UnseenPoolChangedEvent unseen:
                BoardVisualSeat unseenSeat = next.Seat(unseen.Seat);
                unseenSeat.UnseenCount = System.Math.Max(0, unseenSeat.UnseenCount - unseen.Removed.Count);
                break;

            case MatchEndedEvent:
                next.Phase = MatchPhase.Complete;
                break;
        }

        return next;
    }
}
