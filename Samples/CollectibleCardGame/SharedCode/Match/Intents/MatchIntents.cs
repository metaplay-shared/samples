using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Type codes for the match intent hierarchy. Its own registry, separate from
    /// <see cref="MatchEventCodes"/>. The host wraps these in directed messages, whose
    /// <c>[MetaMessage]</c> codes are <c>Docs/protocol.md</c>'s registry and are not allocated here.
    /// </summary>
    public static class MatchIntentCodes
    {
        public const int Mulligan     = 1;
        public const int PlayCard     = 2;
        public const int Attack       = 3;
        public const int EndTurn      = 4;
        public const int EffectChoice = 5;
    }

    /// <summary>
    /// Why an intent was refused. Named results rather than one opaque failure, mirroring the
    /// <see cref="ActionResults"/> idiom: a refusal is loggable, dashboard-visible, and sendable to the client
    /// verbatim so it can put down the card it lifted.
    /// </summary>
    public readonly struct MatchIntentResult : IEquatable<MatchIntentResult>
    {
        public readonly string Name;

        public MatchIntentResult(string name)
        {
            Name = name;
        }

        public bool IsSuccess => Name == null;

        public bool Equals(MatchIntentResult other) => string.Equals(Name, other.Name, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is MatchIntentResult other && Equals(other);
        public override int GetHashCode() => Name?.GetHashCode(StringComparison.Ordinal) ?? 0;

        public static bool operator ==(MatchIntentResult a, MatchIntentResult b) => a.Equals(b);
        public static bool operator !=(MatchIntentResult a, MatchIntentResult b) => !a.Equals(b);

        public override string ToString() => Name ?? "Success";
    }

    /// <summary> Every way an intent can be refused, and the only ones the engine ever returns. </summary>
    public static class MatchIntentResults
    {
        public static readonly MatchIntentResult Success                = new MatchIntentResult(null);

        public static readonly MatchIntentResult NotYourTurn            = new MatchIntentResult(nameof(NotYourTurn));
        public static readonly MatchIntentResult WrongPhase             = new MatchIntentResult(nameof(WrongPhase));
        /// <summary> A resolution is held for a choice, and this intent is not that choice. </summary>
        public static readonly MatchIntentResult ResolutionHeld         = new MatchIntentResult(nameof(ResolutionHeld));
        public static readonly MatchIntentResult NoChoicePending        = new MatchIntentResult(nameof(NoChoicePending));
        /// <summary> The answer names a peek choice other than the one held: an earlier reveal, already answered. </summary>
        public static readonly MatchIntentResult StaleChoice            = new MatchIntentResult(nameof(StaleChoice));
        public static readonly MatchIntentResult InvalidChoice          = new MatchIntentResult(nameof(InvalidChoice));
        public static readonly MatchIntentResult AlreadyMulliganed      = new MatchIntentResult(nameof(AlreadyMulliganed));
        public static readonly MatchIntentResult UnknownInstance        = new MatchIntentResult(nameof(UnknownInstance));
        public static readonly MatchIntentResult NotInYourHand          = new MatchIntentResult(nameof(NotInYourHand));
        public static readonly MatchIntentResult NotEnoughMana          = new MatchIntentResult(nameof(NotEnoughMana));
        public static readonly MatchIntentResult BoardFull              = new MatchIntentResult(nameof(BoardFull));
        public static readonly MatchIntentResult NotYourCritter         = new MatchIntentResult(nameof(NotYourCritter));
        public static readonly MatchIntentResult CritterAsleep          = new MatchIntentResult(nameof(CritterAsleep));
        public static readonly MatchIntentResult CritterAlreadyAttacked = new MatchIntentResult(nameof(CritterAlreadyAttacked));
        /// <summary> A critter with no attack has nothing to declare. </summary>
        public static readonly MatchIntentResult CritterHasNoAttack     = new MatchIntentResult(nameof(CritterHasNoAttack));
        public static readonly MatchIntentResult TargetIsSneaky         = new MatchIntentResult(nameof(TargetIsSneaky));
        public static readonly MatchIntentResult DenProtectedByGuard    = new MatchIntentResult(nameof(DenProtectedByGuard));
        public static readonly MatchIntentResult IllegalTarget          = new MatchIntentResult(nameof(IllegalTarget));
        /// <summary>
        /// A host-built action whose payload the table cannot apply: a seat outside the table, a roster of the
        /// wrong size, a clock push that is not forward, a developer win after the game is over. Never the
        /// answer to a seat's intent.
        /// </summary>
        public static readonly MatchIntentResult InvalidHostAction      = new MatchIntentResult(nameof(InvalidHostAction));

        /// <summary> Every refusal the engine may return, for the self-play invariant that asserts no other one appears. </summary>
        public static readonly MatchIntentResult[] All =
        {
            Success,
            NotYourTurn, WrongPhase, ResolutionHeld, NoChoicePending, StaleChoice, InvalidChoice,
            AlreadyMulliganed, UnknownInstance, NotInYourHand, NotEnoughMana, BoardFull, NotYourCritter,
            CritterAsleep, CritterAlreadyAttacked, CritterHasNoAttack, TargetIsSneaky, DenProtectedByGuard,
            IllegalTarget, InvalidHostAction,
        };
    }

    /// <summary>
    /// What a seat asks the game to do. An intent addresses cards by match instance identity, never by a hand
    /// index or a board slot (<c>Docs/hidden-information.md</c>), and it is judged against the state it
    /// arrives at: a second tap on a card that has already left the hand is refused because it has left, not
    /// because the sender saw an older board (<c>Docs/match.md</c>, "Legality settles every race").
    /// Serializable so the host wraps them in directed messages with no reshaping.
    /// </summary>
    [MetaSerializable]
    public abstract class MatchIntent
    {
        protected MatchIntent() { }

        /// <summary>
        /// <b>Validate this intent and produce the action it becomes</b>, or refuse it. The one place a seat's
        /// ask turns into a change to the timeline, so the pairing of intent to action lives here rather than
        /// in a switch somewhere else that could disagree with it.
        /// <para>
        /// It runs <b>on the server only</b>, which is what lets it read <c>ServerOnly</c> state: whether
        /// these are your cards, in your hand, is a question about a hand, and a follower has none. An action
        /// it produces has already been checked, so <see cref="MatchAction.Execute"/> does not re-ask —
        /// a follower re-running it could not ask honestly anyway.
        /// </para>
        /// <para>
        /// <paramref name="seat"/> is the <em>authenticated</em> seat the host received this from. An intent
        /// does not name a seat, so a client cannot claim somebody else's.
        /// </para>
        /// <para>
        /// It must not touch the model. It answers a question and builds a payload; a test asserts the model
        /// is byte-identical across it. A secret that has to reach <c>Execute</c> travels on the action, on a
        /// member the wire never sees.
        /// </para>
        /// </summary>
        public abstract MatchIntentResult Prepare(MatchModel match, int seat, out MatchAction action);
    }

    /// <summary> Replace any subset of the opening hand, once. Both seats submit, in either order. </summary>
    [MetaSerializableDerived(MatchIntentCodes.Mulligan)]
    public class MulliganIntent : MatchIntent
    {
        /// <summary> The instances to put back. May be empty or the whole hand. </summary>
        [MetaMember(3)] public List<CardInstanceId> Replace { get; private set; }

        public MulliganIntent() { }

        public MulliganIntent(List<CardInstanceId> replace)
        {
            Replace = replace ?? new List<CardInstanceId>();
        }

        public override MatchIntentResult Prepare(MatchModel match, int seat, out MatchAction action)
        {
            action = null;

            MatchIntentResult gate = MulliganRules.CheckSubmit(match, seat, Replace);
            if (!gate.IsSuccess)
                return gate;

            action = new MatchMulliganSubmit(seat, Replace);
            return MatchIntentResults.Success;
        }
    }

    /// <summary> Play a card from hand, with the one target the card asked for if it asked for one. </summary>
    [MetaSerializableDerived(MatchIntentCodes.PlayCard)]
    public class PlayCardIntent : MatchIntent
    {
        [MetaMember(3)] public CardInstanceId  Card   { get; private set; }
        /// <summary> The single cast-time choice. <see cref="EffectTargetRef.None"/> for a card that asks for none. </summary>
        [MetaMember(4)] public EffectTargetRef Target { get; private set; }

        public PlayCardIntent() { }

        public PlayCardIntent(CardInstanceId card, EffectTargetRef target)
        {
            Card   = card;
            Target = target;
        }

        public override MatchIntentResult Prepare(MatchModel match, int seat, out MatchAction action)
        {
            action = null;

            MatchIntentResult gate = TurnRules.CheckOnTurn(match, seat);
            if (!gate.IsSuccess)
                return gate;

            // The one hidden identity in the game that becomes public. Read here, where the secret is
            // readable, and carried on the payload so every follower reads it back as public input.
            HandCard? held = SecretOps.HandCardOf(match, Card);
            if (held == null)
                return MatchIntentResults.NotInYourHand;

            MatchIntentResult legal = Legality.CanPlayCard(match, seat, held.Value, Target);
            if (!legal.IsSuccess)
                return legal;

            action = new MatchPlayCard(seat, Card, held.Value.Card, held.Value.Rank, Target);
            return MatchIntentResults.Success;
        }
    }

    /// <summary> Attack with one awake critter, into an enemy critter or the enemy Den. </summary>
    [MetaSerializableDerived(MatchIntentCodes.Attack)]
    public class AttackIntent : MatchIntent
    {
        [MetaMember(3)] public CardInstanceId  Attacker { get; private set; }
        [MetaMember(4)] public EffectTargetRef Target   { get; private set; }

        public AttackIntent() { }

        public AttackIntent(CardInstanceId attacker, EffectTargetRef target)
        {
            Attacker = attacker;
            Target   = target;
        }

        public override MatchIntentResult Prepare(MatchModel match, int seat, out MatchAction action)
        {
            action = null;

            MatchIntentResult gate = TurnRules.CheckAttack(match, seat, Attacker, Target);
            if (!gate.IsSuccess)
                return gate;

            action = new MatchAttack(seat, Attacker, Target);
            return MatchIntentResults.Success;
        }
    }

    /// <summary>
    /// Finish the turn. Explicit by design: "the deadline lapsed" and "I am finished" must stay
    /// distinguishable, because only one of them is a strike.
    /// </summary>
    [MetaSerializableDerived(MatchIntentCodes.EndTurn)]
    public class EndTurnIntent : MatchIntent
    {
        public EndTurnIntent() { }


        public override MatchIntentResult Prepare(MatchModel match, int seat, out MatchAction action)
        {
            action = null;

            MatchIntentResult gate = TurnRules.CheckEndTurn(match, seat);
            if (!gate.IsSuccess)
                return gate;

            action = new MatchEndTurn(seat);
            return MatchIntentResults.Success;
        }
    }

    /// <summary>
    /// The answer to a held resolution: which of the revealed cards the owner keeps. The one intent that
    /// carries a token: its indices mean something only within one reveal, so it names the choice it answers
    /// and an answer to any other is refused.
    /// </summary>
    [MetaSerializableDerived(MatchIntentCodes.EffectChoice)]
    public class EffectChoiceIntent : MatchIntent
    {
        /// <summary>
        /// Which of the revealed cards to keep, <b>by index in the reveal</b> — not by identity. A card
        /// instance id would name a card, and naming one is what the whole peek is careful not to do; an
        /// index means nothing without the revealed list, which only this seat and the server have, so it
        /// rides the intent, the action and the timeline in the clear.
        /// <para>
        /// Reveal order is the shared frame. <c>HandViews</c> builds the owner's view by walking
        /// <c>SeatSecrets.PeekRevealed</c> in order, so index <c>n</c> is the same card on both sides.
        /// </para>
        /// </summary>
        [MetaMember(3)] public List<int> Keep     { get; private set; }
        /// <summary> The <see cref="PendingEffectChoice.Id"/> this answers. </summary>
        [MetaMember(4)] public int       ChoiceId { get; private set; }

        public EffectChoiceIntent() { }

        public EffectChoiceIntent(int choiceId, List<int> keep)
        {
            ChoiceId = choiceId;
            Keep     = keep ?? new List<int>();
        }

        /// <summary>
        /// The answer a seat that is gone would have given: keep the costliest, ties on canonical card order.
        /// The host answers on its behalf by building this and preparing it like any other, so an absent
        /// seat's answer and a present one take exactly the same path.
        /// </summary>
        public static EffectChoiceIntent Default(MatchModel match, int seat)
            => new EffectChoiceIntent(match.Rules.PendingChoice?.Id ?? 0, SecretOps.DefaultPeekKeep(match, seat)) { _wasDefaulted = true };

        /// <summary>
        /// Whether the host built this rather than a seat sending it. Plain field: intents declare their
        /// members explicitly, so this is not serialized and no client can claim it.
        /// </summary>
        bool _wasDefaulted;

        public override MatchIntentResult Prepare(MatchModel match, int seat, out MatchAction action)
        {
            action = null;

            // No secret is read. Whether an answer is legal is a question about indices, the choice's id and two
            // public counts, so the whole check is public — which is the point of answering by index.
            MatchIntentResult gate = ChoiceRules.CheckApply(match, seat, ChoiceId, Keep);
            if (!gate.IsSuccess)
                return gate;

            action = new MatchEffectChoice(seat, Keep, _wasDefaulted);
            return MatchIntentResults.Success;
        }
    }
}
