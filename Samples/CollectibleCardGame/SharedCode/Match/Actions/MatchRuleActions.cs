using Metaplay.Core;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Metaplay.Core.Model;

namespace Game.Logic
{
    // The rules, as actions. Each one is two methods and the split is the whole discipline:
    //
    //   1. Its check, before it is issued. For an action a seat asked for that is MatchIntent.Prepare, which
    //      produced it; for one the host issues alone it is the action's own ServerPrepare.
    //   2. Execute — the mutation, recording events through MatchModel.Emit and ending with one
    //      OnBoardChanged, never in a finally, so a body that throws fires nothing. It runs on both followers
    //      too, so its public mutation must come from (payload, public state) only. It does not re-ask what
    //      step 1 asked.
    //
    // A deadline a rule arms is stamped from the model's own clock, which every follower has at the same
    // tick, so no payload carries a time.

    /// <summary>
    /// One seat's mulligan answer, swapped when it executes. <b>How many</b> cards it swapped rides the
    /// payload; <b>which</b> ones never reach it.
    /// </summary>
    [ModelAction(ActionCodes.MatchMulliganSubmit)]
    public class MatchMulliganSubmit : MatchAction
    {
        public int           Seat         { get; private set; }
        /// <summary> How many this seat put back; which ones never reach a payload. </summary>
        public int           ReplaceCount { get; private set; }

        /// <summary>
        /// Which instances this seat named, set by <see cref="MulliganIntent.Prepare"/>. <c>[IgnoreDataMember]</c>
        /// keeps it out of the serialized format, so it is live on the leader and null on a follower, which
        /// learns its own new cards from the addressed hand operations the swap queues.
        /// </summary>
        [IgnoreDataMember] List<CardInstanceId> _replace;

        public MatchMulliganSubmit() { }

        public MatchMulliganSubmit(int seat, List<CardInstanceId> replace)
        {
            Seat         = seat;
            _replace     = replace;
            ReplaceCount = replace?.Count ?? 0;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            // The swap first, then the submit: the second submission opens the first turn, which must deal from
            // the settled hands. Secret in, secret out: a no-op on a follower.
            match.Rules.CountAction(actsOnTurn: false);
            SecretOps.ReplaceInMulligan(match, Seat, _replace);
            MulliganRules.Submit(match, Seat, ReplaceCount);
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// End the mulligan and open the first turn; a seat that never answered keeps its hand. Submitted when the
    /// shared deadline lapses; when both seats answer in time this happens inside the second
    /// <see cref="MatchMulliganSubmit"/> instead, because there is nothing left to wait for.
    /// </summary>
    [ModelAction(ActionCodes.MatchMulliganResolve)]
    public class MatchMulliganResolve : MatchHostAction
    {
        public MatchMulliganResolve() { }

        public override MatchIntentResult ServerPrepare(MatchModel match)
        {
            if (match.Rules.Phase != MatchPhase.Mulligan)
                return MatchIntentResults.WrongPhase;
            return MatchIntentResults.Success;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            MulliganRules.Resolve(match);
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Play one card from a hand. <b>The only action that reveals a hidden identity</b>, and it carries it in
    /// its payload: the server read it out of the secret before authoring this, so a follower reveals the
    /// same card without ever having known it. That one-line invariant is what makes the whole scheme small
    /// enough to hold in your head, and a reflection test pins it.
    /// </summary>
    [ModelAction(ActionCodes.MatchPlayCard)]
    public class MatchPlayCard : MatchAction
    {
        public int             Seat         { get; private set; }
        public CardInstanceId  Card         { get; private set; }
        /// <summary>
        /// Which card the instance is, and at which rank its owner brought it. <b>The one hidden identity in
        /// the game that becomes public</b>, and the only reveal: read off the secret hand by
        /// <see cref="PlayCardIntent.Prepare"/>, carried here on the payload, and read back off the payload
        /// by every follower.
        /// </summary>
        public CardId          RevealedCard { get; private set; }
        public int             RevealedRank { get; private set; }
        public EffectTargetRef Target       { get; private set; }

        public MatchPlayCard() { }

        public MatchPlayCard(int seat, CardInstanceId card, CardId revealedCard, int revealedRank, EffectTargetRef target)
        {
            Seat         = seat;
            Card         = card;
            RevealedCard = revealedCard;
            RevealedRank = revealedRank;
            Target       = target;
        }


        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Rules.CountAction(actsOnTurn: true);
            TurnRules.PlayCard(match, Seat, Card, RevealedCard, RevealedRank, Target);
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// One attack. Nothing about it is hidden — two boards, their numbers and their keywords are all public —
    /// so it carries no secret input and needs no excursion.
    /// </summary>
    [ModelAction(ActionCodes.MatchAttack)]
    public class MatchAttack : MatchAction
    {
        public int             Seat     { get; private set; }
        public CardInstanceId  Attacker { get; private set; }
        public EffectTargetRef Target   { get; private set; }

        public MatchAttack() { }

        public MatchAttack(int seat, CardInstanceId attacker, EffectTargetRef target)
        {
            Seat     = seat;
            Attacker = attacker;
            Target   = target;
        }


        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Rules.CountAction(actsOnTurn: true);
            CombatRules.ApplyAttack(match, Seat, Attacker, Target);
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// End the turn: end-of-turn triggers resolve, then the next seat's start-of-turn package runs in the
    /// same action.
    /// </summary>
    [ModelAction(ActionCodes.MatchEndTurn)]
    public class MatchEndTurn : MatchAction
    {
        public int           Seat     { get; private set; }

        public MatchEndTurn() { }

        public MatchEndTurn(int seat)
        {
            Seat     = seat;
        }


        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Rules.CountAction(actsOnTurn: true);
            TurnRules.EndTurn(match);
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Answer a held peek: which revealed cards the seat keeps, from its own intent or, when the deadline
    /// lapsed, from the deterministic default.
    /// <para>
    /// <see cref="WasDefaulted"/> is a payload field rather than a second action because the two differ only
    /// in what the event says about them, and a client that is told "you did not answer" should learn it from
    /// the same place it learns everything else.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.MatchEffectChoice)]
    public class MatchEffectChoice : MatchAction
    {
        public int           Seat         { get; private set; }
        /// <summary>
        /// Which of the revealed cards the seat kept, <b>by index in the reveal</b>. Public in full, and
        /// safely so: an index names no card without the revealed list, which no follower has. This is why
        /// the choice needs nothing hidden on the payload and nothing stashed beside it.
        /// </summary>
        public List<int>     Keep         { get; private set; }
        /// <summary> Whether nobody answered and the deterministic default was applied. </summary>
        public bool          WasDefaulted { get; private set; }

        public MatchEffectChoice() { }

        public MatchEffectChoice(int seat, List<int> keep, bool wasDefaulted)
        {
            Seat         = seat;
            Keep         = keep ?? new List<int>();
            WasDefaulted = wasDefaulted;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Rules.CountAction(actsOnTurn: false);
            ChoiceRules.Apply(match, Seat, Keep, WasDefaulted);
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// The seat on turn draws one extension out of its reserve bank. Whether it may is
    /// <see cref="TurnRules.CanExtendFromReserve"/>, a pure query over public state — so the host routes on
    /// it rather than deciding, and a follower reaches the same verdict.
    /// </summary>
    [ModelAction(ActionCodes.MatchExtendReserve)]
    public class MatchExtendReserve : MatchHostAction
    {
        public int      Seat { get; private set; }

        public MatchExtendReserve() { }

        public MatchExtendReserve(int seat)
        {
            Seat = seat;
        }

        public override MatchIntentResult ServerPrepare(MatchModel match)
        {
            if (!MatchSeats.IsValid(Seat))
                return MatchIntentResults.InvalidHostAction;
            if (!TurnRules.CanExtendFromReserve(match, Seat))
                return MatchIntentResults.WrongPhase;
            return MatchIntentResults.Success;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            TurnRules.ExtendFromReserve(match, Seat);
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }
}
