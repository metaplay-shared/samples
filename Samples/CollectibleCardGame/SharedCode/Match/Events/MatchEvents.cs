using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Type codes for the match event hierarchy. Its own registry, so it cannot collide with intent codes or
    /// with the player action codes in <c>SharedCode/Player/PlayerActions.cs</c>.
    /// </summary>
    public static class MatchEventCodes
    {
        public const int MatchDealt            = 1;
        public const int MulliganResolved      = 2;
        public const int TurnStarted           = 3;
        public const int ManaChanged           = 4;
        public const int CardDrawn             = 5;
        public const int DrawOverflowed        = 6;
        public const int TuckeredOut           = 7;
        public const int CrittersWoke          = 8;
        public const int CardPlayed            = 9;
        public const int CritterEnteredPlay    = 10;
        public const int AttackDeclared        = 11;
        public const int DamageDealt           = 12;
        public const int BubblePopped          = 13;
        public const int SneakyRevealed        = 14;
        public const int Healed                = 15;
        public const int DenDamaged            = 16;
        public const int CritterDied           = 17;
        public const int StatsChanged          = 18;
        public const int KeywordsChanged       = 19;
        public const int CardAddedToHand       = 20;
        public const int CardBounced           = 21;
        public const int EffectResolved        = 22;
        public const int SummonFizzled         = 23;
        public const int UnseenPoolChanged     = 24;
        public const int TurnEnded             = 25;
        public const int MatchEnded            = 26;
        public const int EffectChoiceRequested = 27;
        public const int EffectChoiceResolved  = 28;
    }

    /// <summary> Why a critter left the board dead. </summary>
    [MetaSerializable]
    public enum CritterDeathCause
    {
        /// <summary> Damage reached its health. </summary>
        LethalDamage = 0,
        /// <summary> Its maximum health was reduced to nothing under it. </summary>
        StatLoss     = 1,
    }

    /// <summary> Why a summon produced nothing. </summary>
    [MetaSerializable]
    public enum SummonFizzleReason
    {
        BoardFull = 0,
    }

    /// <summary>
    /// One thing that happened, in the order it happened. The events are the engine's real product: the match
    /// actor writes them onto the replicated timeline, the client renders its board changes and its beats from
    /// them, the bots read the tail of them, and the tests assert on them.
    /// <para>
    /// <b>Every event is public-safe by construction.</b> An event never carries an identity that is not
    /// already public — the draw event says which seat drew and what the counts became, never what was drawn.
    /// There is no filtering step over a richer internal log, and therefore no future event that leaks by being
    /// forgotten (<c>Docs/rules.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public abstract class MatchEvent
    {
    }

    /// <summary> The deal is done and the Weather is in force. Counts only: neither hand is named. </summary>
    [MetaSerializableDerived(MatchEventCodes.MatchDealt)]
    public class MatchDealtEvent : MatchEvent
    {
        [MetaMember(1)] public int       FirstSeat     { get; private set; }
        [MetaMember(2)] public WeatherId Weather       { get; private set; }
        [MetaMember(3)] public int       Seat0HandCount { get; private set; }
        [MetaMember(4)] public int       Seat1HandCount { get; private set; }
        [MetaMember(5)] public int       Seat0DeckCount { get; private set; }
        [MetaMember(6)] public int       Seat1DeckCount { get; private set; }

        public MatchDealtEvent() { }

        public MatchDealtEvent(int firstSeat, WeatherId weather, int seat0HandCount, int seat1HandCount, int seat0DeckCount, int seat1DeckCount)
        {
            FirstSeat      = firstSeat;
            Weather        = weather;
            Seat0HandCount = seat0HandCount;
            Seat1HandCount = seat1HandCount;
            Seat0DeckCount = seat0DeckCount;
            Seat1DeckCount = seat1DeckCount;
        }
    }

    /// <summary>
    /// A seat's mulligan resolved. The replaced <em>count</em> only: what was swapped is secret while it
    /// happens and irrelevant afterwards, and the pool is unchanged either way.
    /// </summary>
    [MetaSerializableDerived(MatchEventCodes.MulliganResolved)]
    public class MulliganResolvedEvent : MatchEvent
    {
        [MetaMember(1)] public int Seat          { get; private set; }
        [MetaMember(2)] public int ReplacedCount { get; private set; }
        [MetaMember(3)] public int HandCount     { get; private set; }
        [MetaMember(4)] public int DeckCount     { get; private set; }

        public MulliganResolvedEvent() { }

        public MulliganResolvedEvent(int seat, int replacedCount, int handCount, int deckCount)
        {
            Seat          = seat;
            ReplacedCount = replacedCount;
            HandCount     = handCount;
            DeckCount     = deckCount;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.TurnStarted)]
    public class TurnStartedEvent : MatchEvent
    {
        [MetaMember(1)] public int Seat { get; private set; }
        [MetaMember(2)] public int Turn { get; private set; }

        public TurnStartedEvent() { }

        public TurnStartedEvent(int seat, int turn)
        {
            Seat = seat;
            Turn = turn;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.ManaChanged)]
    public class ManaChangedEvent : MatchEvent
    {
        [MetaMember(1)] public int Seat    { get; private set; }
        [MetaMember(2)] public int Mana    { get; private set; }
        [MetaMember(3)] public int MaxMana { get; private set; }

        public ManaChangedEvent() { }

        public ManaChangedEvent(int seat, int mana, int maxMana)
        {
            Seat    = seat;
            Mana    = mana;
            MaxMana = maxMana;
        }
    }

    /// <summary> A seat drew. Counts only — never the card. </summary>
    [MetaSerializableDerived(MatchEventCodes.CardDrawn)]
    public class CardDrawnEvent : MatchEvent
    {
        [MetaMember(1)] public int Seat      { get; private set; }
        [MetaMember(2)] public int HandCount { get; private set; }
        [MetaMember(3)] public int DeckCount { get; private set; }

        public CardDrawnEvent() { }

        public CardDrawnEvent(int seat, int handCount, int deckCount)
        {
            Seat      = seat;
            HandCount = handCount;
            DeckCount = deckCount;
        }
    }

    /// <summary> The hand was full, so the card went to the bottom of the deck. Counts only — never the card. </summary>
    [MetaSerializableDerived(MatchEventCodes.DrawOverflowed)]
    public class DrawOverflowedEvent : MatchEvent
    {
        [MetaMember(1)] public int Seat      { get; private set; }
        [MetaMember(2)] public int HandCount { get; private set; }
        [MetaMember(3)] public int DeckCount { get; private set; }

        public DrawOverflowedEvent() { }

        public DrawOverflowedEvent(int seat, int handCount, int deckCount)
        {
            Seat      = seat;
            HandCount = handCount;
            DeckCount = deckCount;
        }
    }

    /// <summary> A draw the seat could not take. Public: deck counts are public information anyway. </summary>
    [MetaSerializableDerived(MatchEventCodes.TuckeredOut)]
    public class TuckeredOutEvent : MatchEvent
    {
        [MetaMember(1)] public int Seat      { get; private set; }
        [MetaMember(2)] public int TickIndex { get; private set; }
        [MetaMember(3)] public int Damage    { get; private set; }

        public TuckeredOutEvent() { }

        public TuckeredOutEvent(int seat, int tickIndex, int damage)
        {
            Seat      = seat;
            TickIndex = tickIndex;
            Damage    = damage;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.CrittersWoke)]
    public class CrittersWokeEvent : MatchEvent
    {
        [MetaMember(1)] public int                  Seat      { get; private set; }
        [MetaMember(2)] public List<CardInstanceId> Instances { get; private set; }

        public CrittersWokeEvent() { }

        public CrittersWokeEvent(int seat, List<CardInstanceId> instances)
        {
            Seat      = seat;
            Instances = instances;
        }
    }

    /// <summary> A card was played from hand. This is the moment the card becomes public. </summary>
    [MetaSerializableDerived(MatchEventCodes.CardPlayed)]
    public class CardPlayedEvent : MatchEvent
    {
        [MetaMember(1)] public int             Seat     { get; private set; }
        [MetaMember(2)] public CardInstanceId  Instance { get; private set; }
        [MetaMember(3)] public CardId          Card     { get; private set; }
        [MetaMember(4)] public int             Rank     { get; private set; }
        [MetaMember(5)] public EffectTargetRef Target   { get; private set; }
        [MetaMember(6)] public int             ManaSpent { get; private set; }

        public CardPlayedEvent() { }

        public CardPlayedEvent(int seat, CardInstanceId instance, CardId card, int rank, EffectTargetRef target, int manaSpent)
        {
            Seat      = seat;
            Instance  = instance;
            Card      = card;
            Rank      = rank;
            Target    = target;
            ManaSpent = manaSpent;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.CritterEnteredPlay)]
    public class CritterEnteredPlayEvent : MatchEvent
    {
        [MetaMember(1)] public int            Seat      { get; private set; }
        [MetaMember(2)] public CardInstanceId Instance  { get; private set; }
        [MetaMember(3)] public CardId         Card      { get; private set; }
        [MetaMember(4)] public int            Attack    { get; private set; }
        [MetaMember(5)] public int            MaxHealth { get; private set; }
        [MetaMember(6)] public KeywordFlags   Keywords  { get; private set; }
        [MetaMember(7)] public bool           IsSleepy  { get; private set; }

        public CritterEnteredPlayEvent() { }

        public CritterEnteredPlayEvent(int seat, CardInstanceId instance, CardId card, int attack, int maxHealth, KeywordFlags keywords, bool isSleepy)
        {
            Seat      = seat;
            Instance  = instance;
            Card      = card;
            Attack    = attack;
            MaxHealth = maxHealth;
            Keywords  = keywords;
            IsSleepy  = isSleepy;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.AttackDeclared)]
    public class AttackDeclaredEvent : MatchEvent
    {
        [MetaMember(1)] public CardInstanceId  Attacker { get; private set; }
        [MetaMember(2)] public EffectTargetRef Target   { get; private set; }

        public AttackDeclaredEvent() { }

        public AttackDeclaredEvent(CardInstanceId attacker, EffectTargetRef target)
        {
            Attacker = attacker;
            Target   = target;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.DamageDealt)]
    public class DamageDealtEvent : MatchEvent
    {
        /// <summary> The critter that dealt it, or none for a Weather's or a trick's own damage. </summary>
        [MetaMember(1)] public CardInstanceId  Source           { get; private set; }
        [MetaMember(2)] public EffectTargetRef Target           { get; private set; }
        [MetaMember(3)] public int             Amount           { get; private set; }
        [MetaMember(4)] public bool            AbsorbedByBubble { get; private set; }

        public DamageDealtEvent() { }

        public DamageDealtEvent(CardInstanceId source, EffectTargetRef target, int amount, bool absorbedByBubble)
        {
            Source           = source;
            Target           = target;
            Amount           = amount;
            AbsorbedByBubble = absorbedByBubble;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.BubblePopped)]
    public class BubblePoppedEvent : MatchEvent
    {
        [MetaMember(1)] public CardInstanceId Instance { get; private set; }

        public BubblePoppedEvent() { }

        public BubblePoppedEvent(CardInstanceId instance)
        {
            Instance = instance;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.SneakyRevealed)]
    public class SneakyRevealedEvent : MatchEvent
    {
        [MetaMember(1)] public CardInstanceId Instance { get; private set; }

        public SneakyRevealedEvent() { }

        public SneakyRevealedEvent(CardInstanceId instance)
        {
            Instance = instance;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.Healed)]
    public class HealedEvent : MatchEvent
    {
        [MetaMember(1)] public EffectTargetRef Target   { get; private set; }
        [MetaMember(2)] public int             Amount   { get; private set; }
        /// <summary> The Den's hit points, or the critter's current health, after the heal. </summary>
        [MetaMember(3)] public int             NewValue { get; private set; }

        public HealedEvent() { }

        public HealedEvent(EffectTargetRef target, int amount, int newValue)
        {
            Target   = target;
            Amount   = amount;
            NewValue = newValue;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.DenDamaged)]
    public class DenDamagedEvent : MatchEvent
    {
        [MetaMember(1)] public int Seat   { get; private set; }
        [MetaMember(2)] public int Amount { get; private set; }
        [MetaMember(3)] public int NewHp  { get; private set; }

        public DenDamagedEvent() { }

        public DenDamagedEvent(int seat, int amount, int newHp)
        {
            Seat   = seat;
            Amount = amount;
            NewHp  = newHp;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.CritterDied)]
    public class CritterDiedEvent : MatchEvent
    {
        [MetaMember(1)] public CardInstanceId    Instance { get; private set; }
        [MetaMember(2)] public int               Seat     { get; private set; }
        [MetaMember(3)] public CritterDeathCause Cause    { get; private set; }

        public CritterDiedEvent() { }

        public CritterDiedEvent(CardInstanceId instance, int seat, CritterDeathCause cause)
        {
            Instance = instance;
            Seat     = seat;
            Cause    = cause;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.StatsChanged)]
    public class StatsChangedEvent : MatchEvent
    {
        [MetaMember(1)] public CardInstanceId Instance  { get; private set; }
        [MetaMember(2)] public int            Attack    { get; private set; }
        [MetaMember(3)] public int            MaxHealth { get; private set; }
        [MetaMember(4)] public int            Damage    { get; private set; }

        public StatsChangedEvent() { }

        public StatsChangedEvent(CardInstanceId instance, int attack, int maxHealth, int damage)
        {
            Instance  = instance;
            Attack    = attack;
            MaxHealth = maxHealth;
            Damage    = damage;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.KeywordsChanged)]
    public class KeywordsChangedEvent : MatchEvent
    {
        [MetaMember(1)] public CardInstanceId Instance { get; private set; }
        [MetaMember(2)] public KeywordFlags   Keywords { get; private set; }

        public KeywordsChangedEvent() { }

        public KeywordsChangedEvent(CardInstanceId instance, KeywordFlags keywords)
        {
            Instance = instance;
            Keywords = keywords;
        }
    }

    /// <summary>
    /// A card reached a hand other than by a draw. The identity is carried only when the card is already
    /// public — a copy out of a public graveyard is, a peeked card is not.
    /// </summary>
    [MetaSerializableDerived(MatchEventCodes.CardAddedToHand)]
    public class CardAddedToHandEvent : MatchEvent
    {
        [MetaMember(1)] public int            Seat      { get; private set; }
        [MetaMember(2)] public int            HandCount { get; private set; }
        /// <summary> The instance, or <see cref="CardInstanceId.None"/> when the card is not public. </summary>
        [MetaMember(3)] public CardInstanceId Instance  { get; private set; }
        /// <summary> The card, or null when the card is not public. </summary>
        [MetaMember(4)] public CardId         Card      { get; private set; }

        public CardAddedToHandEvent() { }

        public CardAddedToHandEvent(int seat, int handCount, CardInstanceId instance, CardId card)
        {
            Seat      = seat;
            HandCount = handCount;
            Instance  = instance;
            Card      = card;
        }
    }

    /// <summary> A critter went back to its owner's hand. It was on the board, so it is public. </summary>
    [MetaSerializableDerived(MatchEventCodes.CardBounced)]
    public class CardBouncedEvent : MatchEvent
    {
        [MetaMember(1)] public CardInstanceId Instance  { get; private set; }
        [MetaMember(2)] public int            Seat      { get; private set; }
        [MetaMember(3)] public int            HandCount { get; private set; }
        public CardBouncedEvent() { }

        public CardBouncedEvent(CardInstanceId instance, int seat, int handCount)
        {
            Instance  = instance;
            Seat      = seat;
            HandCount = handCount;
        }
    }

    /// <summary> One effect step finished. Names the step, which is config the client already has. </summary>
    [MetaSerializableDerived(MatchEventCodes.EffectResolved)]
    public class EffectResolvedEvent : MatchEvent
    {
        [MetaMember(1)] public EffectStepId   Step          { get; private set; }
        [MetaMember(2)] public int            ResolvingSeat { get; private set; }
        /// <summary> The source critter, or none for a Weather's or a trick's own step. </summary>
        [MetaMember(3)] public CardInstanceId Source        { get; private set; }

        public EffectResolvedEvent() { }

        public EffectResolvedEvent(EffectStepId step, int resolvingSeat, CardInstanceId source)
        {
            Step          = step;
            ResolvingSeat = resolvingSeat;
            Source        = source;
        }
    }

    /// <summary> A summon produced nothing because the board was full. </summary>
    [MetaSerializableDerived(MatchEventCodes.SummonFizzled)]
    public class SummonFizzledEvent : MatchEvent
    {
        [MetaMember(1)] public int                Seat   { get; private set; }
        [MetaMember(2)] public CardId             Card   { get; private set; }
        [MetaMember(3)] public int                Count  { get; private set; }
        [MetaMember(4)] public SummonFizzleReason Reason { get; private set; }

        public SummonFizzledEvent() { }

        public SummonFizzledEvent(int seat, CardId card, int count, SummonFizzleReason reason)
        {
            Seat   = seat;
            Card   = card;
            Count  = count;
            Reason = reason;
        }
    }

    /// <summary> Cards left a seat's unseen pool by becoming public. The pool only ever shrinks. </summary>
    [MetaSerializableDerived(MatchEventCodes.UnseenPoolChanged)]
    public class UnseenPoolChangedEvent : MatchEvent
    {
        [MetaMember(1)] public int          Seat    { get; private set; }
        [MetaMember(2)] public List<CardId> Removed { get; private set; }

        public UnseenPoolChangedEvent() { }

        public UnseenPoolChangedEvent(int seat, List<CardId> removed)
        {
            Seat    = seat;
            Removed = removed;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.TurnEnded)]
    public class TurnEndedEvent : MatchEvent
    {
        [MetaMember(1)] public int Seat { get; private set; }
        [MetaMember(2)] public int Turn { get; private set; }

        public TurnEndedEvent() { }

        public TurnEndedEvent(int seat, int turn)
        {
            Seat = seat;
            Turn = turn;
        }
    }

    [MetaSerializableDerived(MatchEventCodes.MatchEnded)]
    public class MatchEndedEvent : MatchEvent
    {
        [MetaMember(1)] public MatchResult Result { get; private set; }

        public MatchEndedEvent() { }

        public MatchEndedEvent(MatchResult result)
        {
            Result = result;
        }
    }

    /// <summary>
    /// A resolution is held on one seat for its own choice. The board shows that publicly; what was revealed
    /// travels on that seat's private channel and is not in this event.
    /// </summary>
    [MetaSerializableDerived(MatchEventCodes.EffectChoiceRequested)]
    public class EffectChoiceRequestedEvent : MatchEvent
    {
        [MetaMember(1)] public int Seat          { get; private set; }
        [MetaMember(2)] public int RevealedCount { get; private set; }
        [MetaMember(3)] public int KeepCount     { get; private set; }

        public EffectChoiceRequestedEvent() { }

        public EffectChoiceRequestedEvent(int seat, int revealedCount, int keepCount)
        {
            Seat          = seat;
            RevealedCount = revealedCount;
            KeepCount     = keepCount;
        }
    }

    /// <summary> The held resolution has its answer and the queue resumes. Counts only. </summary>
    [MetaSerializableDerived(MatchEventCodes.EffectChoiceResolved)]
    public class EffectChoiceResolvedEvent : MatchEvent
    {
        [MetaMember(1)] public int  Seat        { get; private set; }
        [MetaMember(2)] public int  KeptCount   { get; private set; }
        /// <summary> True when nobody chose in time and the deterministic default was applied. </summary>
        [MetaMember(3)] public bool WasDefaulted { get; private set; }

        public EffectChoiceResolvedEvent() { }

        public EffectChoiceResolvedEvent(int seat, int keptCount, bool wasDefaulted)
        {
            Seat         = seat;
            KeptCount    = keptCount;
            WasDefaulted = wasDefaulted;
        }
    }
}
