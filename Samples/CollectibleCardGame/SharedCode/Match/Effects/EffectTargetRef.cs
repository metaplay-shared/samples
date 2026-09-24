using Metaplay.Core.Model;
using System;

namespace Game.Logic
{
    /// <summary> What class of thing an <see cref="EffectTargetRef"/> addresses. </summary>
    [MetaSerializable]
    public enum EffectTargetRefKind
    {
        None    = 0,
        Den     = 1,
        Critter = 2,
    }

    /// <summary>
    /// What an effect actually hits: one critter in play, or one seat's Den. This is the <em>runtime</em> half
    /// of the target vocabulary — <see cref="EffectTargetKind"/> in game config says what class of thing a step
    /// addresses, and this says which one it landed on. A target is always addressed by match instance
    /// identity, never by a board slot (<c>Docs/hidden-information.md</c>).
    /// <para>
    /// It appears in three places and means the same thing in all of them: the chosen target on a
    /// <see cref="PlayCardIntent"/>, the argument to every mutation on <see cref="IEffectMutations"/>, and the
    /// expansion of a step's <see cref="EffectTargetKind"/>. What may legally sit here is checked against
    /// <see cref="EffectVocabulary"/>'s predicates in <see cref="Legality"/>, so config and engine cannot drift
    /// apart about which kinds admit a Den.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public readonly struct EffectTargetRef : IEquatable<EffectTargetRef>
    {
        /// <summary>
        /// Which of the two an address names, or neither. It is an explicit kind rather than a pair of
        /// nullable fields so that the <c>default</c> value of the struct is "no target": instance identity
        /// zero is a real card, and a target that defaulted to it would be a silent aiming error.
        /// </summary>
        [MetaMember(1)] public readonly EffectTargetRefKind Kind;
        /// <summary> The Den's seat. Meaningful only when <see cref="IsDen"/>. </summary>
        [MetaMember(2)] public readonly int                 DenSeat;

        // \note Private, and read through the property below, so that a target which is not a critter can
        //       never hand out a stale identity. The zero-initialised struct would otherwise carry instance
        //       id 0 — a real card, the first one dealt — as its critter.
        [MetaMember(3)] readonly CardInstanceId _critter;

        /// <summary> The critter, or <see cref="CardInstanceId.None"/> when this target is not one. </summary>
        public CardInstanceId Critter => Kind == EffectTargetRefKind.Critter ? _critter : CardInstanceId.None;

        [MetaDeserializationConstructor]
        public EffectTargetRef(EffectTargetRefKind kind, int denSeat, CardInstanceId critter)
        {
            Kind     = kind;
            DenSeat  = denSeat;
            _critter = critter;
        }

        /// <summary> No target. What a step with <see cref="EffectTargetKind.None"/> carries. </summary>
        public static readonly EffectTargetRef None = default;

        public static EffectTargetRef Den(int seat) => new EffectTargetRef(EffectTargetRefKind.Den, seat, CardInstanceId.None);

        public static EffectTargetRef OnCritter(CardInstanceId critter) => new EffectTargetRef(EffectTargetRefKind.Critter, MatchSeats.None, critter);

        public bool IsNone    => Kind == EffectTargetRefKind.None;
        public bool IsDen     => Kind == EffectTargetRefKind.Den;
        public bool IsCritter => Kind == EffectTargetRefKind.Critter;

        public bool Equals(EffectTargetRef other) => Kind == other.Kind && DenSeat == other.DenSeat && _critter == other._critter;
        public override bool Equals(object obj) => obj is EffectTargetRef other && Equals(other);
        public override int GetHashCode() => (int)Kind ^ (DenSeat << 2) ^ (_critter.Value << 4);

        public static bool operator ==(EffectTargetRef a, EffectTargetRef b) => a.Equals(b);
        public static bool operator !=(EffectTargetRef a, EffectTargetRef b) => !a.Equals(b);

        public override string ToString() => IsDen ? $"Den{DenSeat}" : (IsCritter ? Critter.ToString() : "none");
    }
}
