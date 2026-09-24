using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// What a card actually does. The engine owns the queue, the resolution points and the mutation surface —
    /// the three things that have to be the same for every effect ever authored — and hands the meaning of one
    /// step to this (<c>Docs/rules.md</c>, "The effect interpreter is a component behind a boundary").
    /// <para>
    /// The launch implementation is <see cref="StepInterpreter"/>, which walks
    /// <see cref="EffectVocabulary"/>'s ten primitives. It is an interface rather than a static call so a test
    /// can drive the queue with a stub, which is the only way to reach the failure modes real content cannot
    /// produce.
    /// </para>
    /// </summary>
    public interface IEffectInterpreter
    {
        /// <summary>
        /// Resolve one dequeued step. Mutating anything except through <paramref name="mut"/> is a contract
        /// violation: an interpreter that could write a zone directly would be able to break the unseen pool,
        /// the caps and the event stream at once, and the leak would be invisible.
        /// </summary>
        void Resolve(EffectQueueItem item, IEffectContext ctx, IEffectMutations mut);
    }

    /// <summary>
    /// The read surface handed to an interpreter. Nothing on it is a way to change anything: no property is
    /// settable, no method returns void, and every collection comes back as <c>IReadOnlyList</c>. Nothing on
    /// it reaches the match's secret half either.
    /// <para>
    /// The honest limit of that: the rules and their interpreter compile into one assembly, so an interpreter
    /// that went looking could still reach the <c>internal</c> mutators on a <see cref="BoardCritter"/> this
    /// hands back. The boundary is the shape of this interface, not something the type system can enforce
    /// across a wall that is not there. Handing back defensive copies would make it airtight and would
    /// allocate on every effect resolution; the trade is deliberate, and <c>EffectBoundaryTests</c> guards
    /// the surface rather than the assembly.
    /// </para>
    /// </summary>
    public interface IEffectContext
    {
        SharedGameConfig Config { get; }

        /// <summary> One seat's board, in entered-play order. That order is the canonical iteration order. </summary>
        IReadOnlyList<BoardCritter> Board(int seat);
        /// <summary> The critter with this identity on either board, or null. </summary>
        BoardCritter CritterAt(CardInstanceId id);
        /// <summary> The catalogue card a public instance is, or null when it is hidden or names nothing. </summary>
        CardInfo CardOf(CardInstanceId id);

        /// <summary> One seat's graveyard, in arrival order. Public information. </summary>
        IReadOnlyList<CardInstanceId> Graveyard(int seat);
        /// <summary> Cards the seat has played this match, in play order. What <c>Per:PlayedThisMatch</c> counts. </summary>
        IReadOnlyList<CardId> PlayedThisMatch(int seat);

        // \note There is deliberately no seeded stream on this surface. A follower re-executing an effect has
        //       none, so an effect that drew from one would compute a different public outcome on each side —
        //       the one hole the whole design cannot survive. A future effect that genuinely wants randomness
        //       must have the SERVER draw it and put the resolved outcome in the action's payload, the way a
        //       played card's identity travels (Docs/hidden-information.md, Docs/rules.md).
    }

    /// <summary>
    /// The write surface: the only way an interpreter changes anything. Every call maintains the caps, the
    /// unseen pool, the health clamps, the public/secret event choice, the instance bookkeeping and the
    /// follow-on triggers identically, no matter which effect asked.
    /// </summary>
    public interface IEffectMutations
    {
        void DealDamage(EffectTargetRef target, int amount);
        void Heal(EffectTargetRef target, int amount);
        void Draw(int seat, int count);
        /// <summary> Gain mana for the turn, or permanently — the same primitive with a parameter. </summary>
        void GainMana(int seat, int amount, ManaDuration duration);
        void Buff(CardInstanceId critter, int attackDelta, int maxHealthDelta);
        void GrantKeywords(CardInstanceId critter, KeywordFlags keywords);
        /// <summary> Put a token critter onto the seat's board. A full board fizzles with an event. </summary>
        void Summon(int seat, CardInfo card, int count);
        /// <summary> Return a critter to its owner's hand. It stays public: the pool never grows. </summary>
        void Bounce(CardInstanceId critter);
        /// <summary>
        /// Add a fresh instance of a catalogue card to a hand. Overflow goes to the deck bottom.
        /// <para>
        /// The copy is public, and there is deliberately no way to ask for one that is not. A card created in
        /// play is created by a public event, and a secret one would have to enter its owner's unseen pool —
        /// which only ever shrinks. An effect that genuinely wants a hidden addition has to be argued against
        /// <c>Docs/hidden-information.md</c> first, and it would be a different mutation.
        /// </para>
        /// </summary>
        void AddCopyToHand(int seat, CardInfo card);
        /// <summary>
        /// Hold the resolution for this seat's choice over the top <paramref name="lookCount"/> cards of its
        /// deck. The revealed cards go on the seat's private channel and never touch the public pool.
        /// </summary>
        void PeekDeck(int seat, int lookCount, int keepCount);
    }
}
