using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// The base for every change to the replicated match timeline — which means every
    /// change to the game. Every match action is <b>leader-synchronized only</b>: with no
    /// <c>FollowerSynchronized</c> action declared, the SDK refuses any action a client enqueues, so the actor is
    /// the only writer. Client moves arrive as intents instead (<c>Docs/protocol.md</c>).
    /// <para>
    /// <b>What every subclass owes.</b> The mutation of public members must be a function of (this
    /// action's own payload, the public model) only. <c>ServerOnly</c> inputs may drive <c>ServerOnly</c>
    /// mutations only, through <see cref="SecretOps"/>. Every action must execute correctly and identically —
    /// on its public side — against a model where every <c>ServerOnly</c> member is default, because that
    /// model is the follower. An action may not declare a <c>[ServerOnly]</c> member of its own; a reflection
    /// test refuses one.
    /// </para>
    /// </summary>
    [MetaSerializable]
    [ModelActionExecuteFlags(ModelActionExecuteFlags.LeaderSynchronized)]
    public abstract class MatchAction : ModelAction<MatchModel>
    {
        /// <summary>
        /// The SDK's entry point. A <c>commit:false</c> call is structurally a no-op here: nothing in this
        /// game dry-runs a match action through the SDK — an action is checked before it is issued, by the
        /// intent that produced it or by its own <c>ServerPrepare</c>, and the SDK itself only ever runs a
        /// multiplayer-entity action to commit (<c>ModelUtil.RunAction</c>, leader and followers alike).
        /// </summary>
        public sealed override MetaActionResult InvokeExecute(MatchModel match, bool commit)
            => commit ? Execute(match) : MetaActionResult.Success;

        /// <summary>
        /// <b>Apply this action.</b> Runs on the server and again on both followers, so its mutation of
        /// public members must be a function of (this action's payload, the public model) only —
        /// <c>ServerOnly</c> state may drive <c>ServerOnly</c> mutations and nothing else.
        /// <para>
        /// It does not re-check what the intent or the host already asked. The leader issues no action that
        /// failed that question, and a follower re-asking it against a model with no secrets would be
        /// answering a different question.
        /// </para>
        /// </summary>
        protected abstract MetaActionResult Execute(MatchModel match);
    }

    /// <summary>
    /// An action delivered to <b>one seat alone</b>, and executed by nobody else — not even the server, which
    /// stages a <c>NoopAction</c> in its place and hands this to the one member entitled to it
    /// (<c>MultiplayerEntityActorBase.ExecuteActionPerMember</c>).
    /// <para>
    /// It exists to carry the private half of a change that the public half already announced: the timeline
    /// says a hand grew by one, and this says which card it was, to the seat that may know. Riding the
    /// timeline is the whole point — it arrives ordered against the operation that explains it, which a
    /// directed message cannot be.
    /// </para>
    /// <para>
    /// <b>It must not touch the checksummed model.</b> The server executes a no-op in its place, so any public
    /// mutation here is a mutation the other seat never makes. Only per-viewer state may move — in this game
    /// <see cref="MatchModel.OwnHand"/>, which is not serialized and therefore out of the checksum. A
    /// violation surfaces as a checksum mismatch on the recipient rather than as silent drift.
    /// </para>
    /// </summary>
    public abstract class MatchAddressedAction : MatchAction
    {
    }

    /// <summary>
    /// An action the host issues on its own: a lapsed deadline, a seat change, the table's own phase. No
    /// intent answers it, so it carries its own check — and the staging path has to ask, because the SDK's
    /// <c>ExecuteAction</c> stages before it runs and only warns when an action refuses.
    /// </summary>
    public abstract class MatchHostAction : MatchAction
    {
        public virtual MatchIntentResult ServerPrepare(MatchModel match) => MatchIntentResults.Success;
    }
}
