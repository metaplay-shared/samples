using Metaplay.Core;
using Metaplay.Core.Model;
using System;

namespace Game.Logic
{
    /// <summary>
    /// A card's identity for the length of one match. Dense and small: the value indexes the match's instance
    /// registry directly.
    /// <para>
    /// Identities for the deal are minted in a canonical, shuffle-independent order over the authored deck
    /// lists, before the shuffle — an identity minted in draw order would be the deck order in disguise
    /// (<c>Docs/hidden-information.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public readonly struct CardInstanceId : IEquatable<CardInstanceId>, IComparable<CardInstanceId>
    {
        [MetaMember(1)] public readonly int Value;

        [MetaDeserializationConstructor]
        public CardInstanceId(int value)
        {
            Value = value;
        }

        /// <summary> No instance. Used where a target or a source is absent, never as a real identity. </summary>
        public static readonly CardInstanceId None = new CardInstanceId(-1);

        public bool IsValid => Value >= 0;

        public bool Equals(CardInstanceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CardInstanceId other && Equals(other);
        public override int GetHashCode() => Value;
        public int CompareTo(CardInstanceId other) => Value.CompareTo(other.Value);

        public static bool operator ==(CardInstanceId a, CardInstanceId b) => a.Value == b.Value;
        public static bool operator !=(CardInstanceId a, CardInstanceId b) => a.Value != b.Value;

        public override string ToString() => Value >= 0 ? $"#{Value}" : "#none";
    }

    /// <summary>
    /// Where a card is, as far as <em>everybody</em> is allowed to know. This is the public half of a card's
    /// position and it is on the replicated model, so it says only what both clients may hold.
    /// </summary>
    [MetaSerializable]
    public enum CardPlace
    {
        /// <summary>
        /// In its owner's deck or hand, and not public. <b>Which of the two is secret</b>: publishing that
        /// would let a client subtract the hand out of the unseen pool, which is the attack
        /// <c>Docs/hidden-information.md</c>'s "Public deck contents and the subtraction problem"
        /// forbids.
        /// </summary>
        Unseen    = 0,
        /// <summary> Publicly in a hand: the second player's compensation, a bounced critter, a graveyard copy. </summary>
        Hand      = 1,
        /// <summary> Publicly in a deck: a public card that overflowed to the bottom. </summary>
        Deck      = 2,
        Board     = 3,
        Graveyard = 4,
        /// <summary> Minted but not yet placed: a token or a copy between its creation and its first zone. </summary>
        Limbo     = 5,
    }

    /// <summary>
    /// One card in the match, as the public registry holds it: whose it is, where it is as far as everybody
    /// may know, and — once it has become public — which catalogue card it is and at which rank.
    /// <para>
    /// The identity of a card that is still hidden is <b>not here</b>. It lives in its owner's
    /// <see cref="SeatSecrets.Cards"/> and is answered by <see cref="CardLookup"/>, which can answer only
    /// where the answer is knowable. That split is what stops the registry from being usable to split a
    /// seat's unseen pool between its deck and its hand.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class CardInstance
    {
        [MetaMember(1)] public CardInstanceId Id       { get; private set; }
        /// <summary> The seat that owns it. A critter's owner never changes. </summary>
        [MetaMember(4)] public int            Owner    { get; private set; }
        /// <summary> Whether the card has ever become public. Public cards are outside the unseen pool. </summary>
        [MetaMember(6)] public bool           IsPublic { get; private set; }
        /// <summary>
        /// Whether the instance came out of its owner's own starting deck. The Acorn, tokens, summons and
        /// graveyard copies are not: they are never Heist-eligible.
        /// <para>
        /// Public even while the card is hidden, and it narrows nothing: the deal publishes how many
        /// starting-deck instances each seat has, and the Acorn is public from the moment it is granted.
        /// </para>
        /// </summary>
        [MetaMember(7)] public bool           FromStartingDeck { get; private set; }
        [MetaMember(8)] public CardPlace      Place    { get; private set; }

        /// <summary> Null while the card is hidden; filled by <see cref="Reveal"/> when it becomes public. </summary>
        [MetaMember(2)] public MetaRef<CardInfo> Card { get; private set; }
        /// <summary> Zero while the card is hidden. A rank narrows which card an instance is, so it is part of the secret. </summary>
        [MetaMember(3)] public int               Rank { get; private set; }

        public CardInstance() { }

        public CardInstance(CardInstanceId id, int owner, CardPlace place, bool isPublic, bool fromStartingDeck)
        {
            Id               = id;
            Owner            = owner;
            Place            = place;
            IsPublic         = isPublic;
            FromStartingDeck = fromStartingDeck;
        }

        /// <summary>
        /// Every field explicitly, for an instance whose identity is already known. What a test that builds a
        /// board rather than dealing one needs, and what a fixture that re-mints a whole registry needs — both
        /// of which live in another assembly and so cannot reach <see cref="Reveal"/>.
        /// </summary>
        public CardInstance(CardInstanceId id, MetaRef<CardInfo> card, int rank, int owner, CardPlace place, bool isPublic, bool fromStartingDeck)
            : this(id, owner, place, isPublic, fromStartingDeck)
        {
            Card = card;
            Rank = rank;
        }

        /// <summary> Whether this entry knows what the card is. False for anything still hidden. </summary>
        public bool IsKnown => Card != null;

        /// <summary>
        /// The catalogue card. <b>Throws on a hidden instance, deliberately.</b> Any path that needs to know
        /// what a hidden card is has to say so by going through <see cref="CardLookup"/>, which can answer
        /// only where the answer exists; a path that reaches for this on a hidden card is a bug the first
        /// test finds rather than a null that flows into a public write.
        /// </summary>
        public CardInfo  Info   => Card.Ref;
        public CardId    CardId => Card.Ref.CardId;
        /// <summary> The card's numbers at the rank its owner brought it. </summary>
        public CardStats Stats  => Card.Ref.GetStatsAtRank(Rank);

        // \note Only ZoneOps calls these. Everything else — combat, the effect interpreter, the rule bodies —
        //       moves cards by asking ZoneOps, which is what keeps the pool, the caps and the events in one
        //       place.
        internal void Reveal(MetaRef<CardInfo> card, int rank)
        {
            Card = card;
            Rank = rank;
        }

        internal void SetPlace(CardPlace place) => Place = place;
        internal void MakePublic() => IsPublic = true;

        /// <summary>
        /// A hidden instance prints as hidden, because a log line or a debugger watch is the last place a
        /// hidden hand should be readable — and here it is structural rather than careful: the entry does not
        /// hold the answer.
        /// </summary>
        public override string ToString()
            => $"{Id} {(IsKnown ? Card.KeyObject?.ToString() : "<hidden>")} r{Rank} seat{Owner} {Place}{(IsPublic ? " public" : "")}";
    }

    /// <summary>
    /// A copy of what a critter was at one moment, taken when a trigger is enqueued so a <c>Goodbye:</c> reads
    /// its critter as it was at death rather than having to defend against its own source being swept off the
    /// board (<c>Docs/rules.md</c>, "A stable view of a dead source").
    /// </summary>
    [MetaSerializable]
    public readonly struct CardInstanceSnapshot
    {
        [MetaMember(1)] public readonly CardInstanceId Id;
        [MetaMember(2)] public readonly int            Owner;
        [MetaMember(3)] public readonly int            Attack;
        [MetaMember(4)] public readonly int            MaxHealth;
        [MetaMember(5)] public readonly int            Damage;
        [MetaMember(6)] public readonly KeywordFlags   Keywords;
        /// <summary> True when the snapshot was taken of a critter in play, false for a trick or an absent source. </summary>
        [MetaMember(7)] public readonly bool           WasOnBoard;

        [MetaDeserializationConstructor]
        public CardInstanceSnapshot(CardInstanceId id, int owner, int attack, int maxHealth, int damage, KeywordFlags keywords, bool wasOnBoard)
        {
            Id         = id;
            Owner      = owner;
            Attack     = attack;
            MaxHealth  = maxHealth;
            Damage     = damage;
            Keywords   = keywords;
            WasOnBoard = wasOnBoard;
        }

        /// <summary> No source: a Weather's own steps have none. </summary>
        public static CardInstanceSnapshot None(int owner)
            => new CardInstanceSnapshot(CardInstanceId.None, owner, 0, 0, 0, KeywordFlags.None, false);

        public static CardInstanceSnapshot OfCritter(BoardCritter critter, int owner)
            => new CardInstanceSnapshot(critter.Id, owner, critter.Attack, critter.MaxHealth, critter.Damage, critter.Keywords, true);

        /// <summary> A card that is not a critter in play — a trick being cast. </summary>
        public static CardInstanceSnapshot OfCard(CardInstanceId id, int owner)
            => new CardInstanceSnapshot(id, owner, 0, 0, 0, KeywordFlags.None, false);
    }

    /// <summary>
    /// A critter in play. Its numbers are the card's rank-track stats plus whatever happened since; where it
    /// got a keyword — printed, granted in play, or from the Weather aura — is deliberately not recorded,
    /// because no rule ever asks (<c>Docs/rules.md</c>, "Keywords, Weathers and rank tracks are all
    /// modifiers").
    /// </summary>
    [MetaSerializable]
    public class BoardCritter
    {
        [MetaMember(1)] public CardInstanceId Id                  { get; private set; }
        [MetaMember(2)] public int            Attack              { get; private set; }
        [MetaMember(3)] public int            MaxHealth           { get; private set; }
        /// <summary> Damage persists between turns (<c>Docs/game-design.md</c>, "Combat"). </summary>
        [MetaMember(4)] public int            Damage              { get; private set; }
        [MetaMember(5)] public KeywordFlags   Keywords            { get; private set; }
        [MetaMember(6)] public bool           IsSleepy            { get; private set; }
        [MetaMember(7)] public bool           HasAttackedThisTurn { get; private set; }
        /// <summary> Whether the Bubble is still unspent. Meaningless without <see cref="KeywordFlags.Bubble"/>. </summary>
        [MetaMember(8)] public bool           BubbleIntact        { get; private set; }

        public BoardCritter() { }

        public BoardCritter(CardInstanceId id, int attack, int maxHealth, KeywordFlags keywords, bool isSleepy)
        {
            Id           = id;
            Attack       = attack;
            MaxHealth    = maxHealth;
            Damage       = 0;
            Keywords     = keywords;
            IsSleepy     = isSleepy;
            BubbleIntact = (keywords & KeywordFlags.Bubble) != 0;
        }

        /// <summary>
        /// Every field explicitly, for reconstructing a critter that is mid-game: what a test that starts from
        /// a board rather than from a deal builds.
        /// </summary>
        public BoardCritter(CardInstanceId id, int attack, int maxHealth, int damage, KeywordFlags keywords, bool isSleepy, bool hasAttackedThisTurn, bool bubbleIntact)
        {
            Id                  = id;
            Attack              = attack;
            MaxHealth           = maxHealth;
            Damage              = damage;
            Keywords            = keywords;
            IsSleepy            = isSleepy;
            HasAttackedThisTurn = hasAttackedThisTurn;
            BubbleIntact        = bubbleIntact;
        }

        /// <summary> Derived: buffing maximum health does not heal, because damage is stored separately. </summary>
        public int CurrentHealth => MaxHealth - Damage;

        public bool IsDead => CurrentHealth <= 0;

        public bool HasBubble    => (Keywords & KeywordFlags.Bubble) != 0;
        public bool HasSneaky    => (Keywords & KeywordFlags.Sneaky) != 0;
        public bool HasSnacktime => (Keywords & KeywordFlags.Snacktime) != 0;

        /// <summary> Whether a live Bubble would absorb the next damage instance. </summary>
        public bool BubbleWouldAbsorb => HasBubble && BubbleIntact;

        // \note Written only from ZoneOps, CombatRules and the rule bodies' mutation surface — never from the
        //       effect interpreter, which reaches state through IEffectMutations alone.
        internal void AddDamage(int amount) => Damage += amount;
        internal void RemoveDamage(int amount) => Damage = Damage - amount < 0 ? 0 : Damage - amount;
        internal void PopBubble() => BubbleIntact = false;
        internal void RestoreBubble() => BubbleIntact = true;
        internal void AddKeywords(KeywordFlags flags) => Keywords |= flags;
        internal void RemoveKeywords(KeywordFlags flags) => Keywords &= ~flags;
        internal void SetSleepy(bool sleepy) => IsSleepy = sleepy;
        internal void SetHasAttacked(bool attacked) => HasAttackedThisTurn = attacked;

        internal void Buff(int attackDelta, int maxHealthDelta)
        {
            Attack    = Attack + attackDelta < 0 ? 0 : Attack + attackDelta;
            MaxHealth = MaxHealth + maxHealthDelta < 0 ? 0 : MaxHealth + maxHealthDelta;
        }

        public override string ToString() => $"{Id} {Attack}/{CurrentHealth}({MaxHealth}) {Keywords}{(IsSleepy ? " sleepy" : "")}";
    }
}
