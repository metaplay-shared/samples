using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Game.Logic
{
    /// <summary>
    /// One game of Sticky Paws as a replicated entity. <b>The model is the game.</b> The rules run on the
    /// members below and the actions that mutate them are the rules: the actor executes each one and every
    /// follower re-executes the identical body, so the SDK's own checksum covers the game rather than a
    /// picture of it (<c>Docs/match.md</c>, <c>Docs/rules.md</c>).
    /// <para>
    /// <b>One secret root.</b> <see cref="Secret"/> is the only <c>[ServerOnly]</c> member: non-null on the
    /// server and null on every client, so <c>Secret != null</c> <em>is</em> "I am the authority", and
    /// <see cref="SecretSeat"/> is the only path to a seat's hidden half. A shape test asserts by reflection
    /// that it is the only one. The per-viewer view (<see cref="OwnHand"/>, <see cref="OwnPeek"/>,
    /// <see cref="Outbox"/>) is not serialized at all.
    /// </para>
    /// <para>
    /// <b>No shared action takes <c>ServerOnly</c> state as input to a public mutation.</b> Every
    /// action's mutation of public members is a function of (its own payload, the public model) only; secret
    /// inputs drive secret mutations only. That is what makes the follower's copy — where every
    /// <c>ServerOnly</c> member is default — reach the same public state. It is enforced by running every
    /// action twice offline against a network-masked clone, not by review
    /// (<c>Backend/SharedCode.Tests/SelfPlay/FollowerMirror.cs</c>).
    /// </para>
    /// <para>
    /// <c>ServerOnly</c>, never <c>Hidden</c>: <c>ServerOnly</c> is <c>Hidden | NoChecksum</c>, so
    /// <c>Hidden</c> alone would still be checksummed and both clients would have to agree about a value
    /// neither may see. There is no standalone <c>[Hidden]</c> attribute to reach for by mistake, and
    /// <c>[NoChecksum]</c> beside <c>[ServerOnly]</c> is a no-op. <c>MatchModelTests</c> asserts the flag
    /// pair rather than trusting this paragraph.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(10)]
    [SupportedSchemaVersions(1, 1)]
    public class MatchModel : MultiplayerModelBase<MatchModel>
    {
        // ---- the one secret root: never sent, never checksummed ----

        /// <summary>
        /// Everything only the server may know: both deck orders, both hands, the seeded stream, the bot
        /// seed, the hidden half of the card registry, and what a held peek revealed. Non-null on the
        /// server and null on every client.
        /// </summary>
        [MetaMember(3), ServerOnly] public MatchSecrets Secret { get; set; }

        // ---- the public game: the rules run on exactly these members and the SDK checksums them ----

        /// <summary> The public rules state. Every rule reads and writes this and nothing hidden is in it. </summary>
        [MetaMember(12)] public MatchRulesState Rules { get; set; }

        /// <summary> Where the table's clocks are, as absolute stamps. Written by the rule actions that arm them. </summary>
        [MetaMember(23)] public MatchPacing Pacing { get; set; } = new MatchPacing();

        /// <summary>
        /// The durations the rules derive their stamps from. On the model because a follower re-executing an
        /// action has to reach the same stamp from the same tick, and the durations cannot ride a payload.
        /// Assigned once, by the actor's setup, and never again — a table plays out at the pacing it was born
        /// with.
        /// <para>
        /// Writing this outside an action is the classic desync: it is public and therefore checksummed. The
        /// setup is the one sanctioned exception, for the reason <c>MatchActor.SetUpModelAsync</c> states —
        /// there is no timeline yet and no subscriber to send one to.
        /// </para>
        /// </summary>
        [MetaMember(13)] public MatchTimings Timings { get; set; }

        /// <summary>
        /// Every event of the whole match, appended and never rewritten. Kept whole rather than for the
        /// current turn: the event feed reads its tail, a player reconnecting at turn eight can read the game
        /// they missed, and the Heist eligibility list is a projection of it. Each action appends only its
        /// own events, and a follower appends them by running the same body — so the history costs no traffic
        /// at all.
        /// </summary>
        [MaxCollectionSize(4096)]
        [MetaMember(11)] public List<MatchEvent> History { get; set; } = new List<MatchEvent>();

        // ---- the table's own public state ----

        /// <summary> Exactly two, index == seat. Hard two-seat, baked in. </summary>
        [MetaMember(20)] public List<MatchSeat>     Seats  { get; set; }
        [MetaMember(21)] public MatchStakes         Stakes { get; set; }
        [MetaMember(22)] public MatchTablePhase     Phase  { get; set; }
        /// <summary> Null until the game is over. </summary>
        [MetaMember(24)] public MatchOutcomeRecord  Result { get; set; }

        /// <summary>
        /// Per seat, whether that account has acknowledged the result. Public rather than <c>ServerOnly</c>
        /// because an action writes it and the acknowledgements are not secret: the losing client showing
        /// "recording result…" is a feature.
        /// It stops a retry telling an account twice, and it is what the result panel reads.
        /// </summary>
        [MetaMember(25)] public List<bool> ResultAcked { get; set; }

        /// <summary>
        /// Per seat, the cards that seat played from its own starting deck minus the ones its owner had
        /// locked at enqueue, each with the rank its owner brought it at. Kept explicitly rather than
        /// re-derived: it has to outlive the board, and it is read again in the result payload after the game
        /// state has stopped meaning anything.
        /// <para>
        /// The <b>loser's</b> row is the Heist menu, and it has the winner's frozen locks subtracted as well:
        /// a lock runs both ways, so a card the winner has frozen would pay them nothing and still cost the
        /// loser a rank (<c>MatchHeistPolicy.Eligibility</c>). The unfiltered played list survives on
        /// <c>Rules.Result.PlayedBy(seat)</c>, which is what the lineup is drawn from.
        /// </para>
        /// </summary>
        [MetaMember(32)] public List<List<HeistEligibleCard>> HeistEligibility { get; set; }

        // ---- the local client's own hand: delivered per viewer, so never sent and never checksummed ----

        /// <summary>
        /// This client's own view of the match: the hand it holds, and what a held peek is showing it.
        /// <para>
        /// <b>Client-only state, and not serialized at all.</b> Its value differs per viewer, so it can never
        /// ride the checksummed timeline; it is null on the server, which reads hands out of
        /// <see cref="Secret"/>. It needs no <c>ServerOnly</c>: the match entity is ephemeral and nothing in
        /// the follower path copies a model, so nothing ever serializes it.
        /// </para>
        /// <para>
        /// The baseline arrives as subscribe-time private state; every change after it is an <b>addressed</b>
        /// timeline operation — <see cref="MatchOwnCardGained"/>, <see cref="MatchOwnCardLost"/> and
        /// <see cref="MatchOwnPeekRevealed"/> — which the server never executes and no other seat receives.
        /// </para>
        /// </summary>
        public List<HandCard> OwnHand { get; set; }

        /// <summary> What a held peek is showing this client, or null. Client-only, for the reason above. </summary>
        public PendingChoiceView OwnPeek { get; set; }

        /// <summary>
        /// Addressed operations the host has produced and not yet staged, each with the seat it is for — an
        /// <b>outbox</b>, not state.
        /// <para>
        /// A rule that moves a card into or out of a hidden hand knows which card it moved and the host does
        /// not, so the rule queues the operation that will say so and the host stages it after the action
        /// returns. Nothing is diffed, nothing is inferred, and the queue is written by the same statement
        /// that moves the card.
        /// </para>
        /// <para>
        /// The <b>seat rides here rather than on the operation</b>. It is routing: the host reads it once to
        /// pick which member to address, and it never reaches a wire. The operation itself says only what
        /// happened, because the SDK delivers it to exactly one member and gives everyone else a no-op — the
        /// recipient does not need to be told that it is the recipient.
        /// </para>
        /// <para>
        /// Not serialized, for the same reason as the two above and one more: it is drained inside the actor
        /// turn that fills it. On a follower nothing ever appends here, because <see cref="SecretOps"/> queues
        /// only where there is a secret to move a card in.
        /// </para>
        /// </summary>
        public List<(int Seat, MatchAddressedAction Action)> Outbox { get; } = new List<(int, MatchAddressedAction)>();

        [IgnoreDataMember] public IMatchModelClientListener ClientListener { get; set; } = EmptyMatchModelClientListener.Instance;

        /// <summary>
        /// What one effect step <em>means</em>. Not serialized, like the listener, and defaulted to the
        /// launch implementation — it is a hook rather than state, and it exists so a test can drive the
        /// queue with a stub, which is the only way to reach the failure modes real content cannot produce
        /// (<c>Docs/rules.md</c>, "The effect interpreter is a component behind a boundary").
        /// </summary>
        [IgnoreDataMember] public IEffectInterpreter Interpreter { get; set; } = StepInterpreter.Instance;

        /// <summary>
        /// The model's clock is what every deadline is stamped from and what a client's countdown reads. On the
        /// server it is the wall clock floored to the tick, because the SDK runs the pending ticks before
        /// every action; a client plays the same ticks off the timeline.
        /// <para>
        /// <b>One tick a second</b> is the lowest rate that serves. Every shipped duration is whole seconds,
        /// so a finer tick buys no stamp precision; a deadline armed mid-tick runs up to one tick short, which
        /// the presentation allowance on each decision clock covers; and each tick is a timeline operation
        /// flushed to every subscriber. Nothing happens on a tick.
        /// </para>
        /// </summary>
        public override int TicksPerSecond => 1;

        public override void OnTick() { }

        public override void OnFastForwardTime(MetaDuration elapsedTime) { }

        // ---- the one accessor, and the one predicate ----

        /// <summary>
        /// Record one thing that happened. It goes on the model's own history <em>and</em> to the client
        /// listener, so the recorded stream, the event feed and the follower's comparison are all one list
        /// with no second copy.
        /// </summary>
        public void Emit(MatchEvent ev)
        {
            History.Add(ev);
            ClientListener.OnMatchEvent(ev);
        }

        /// <summary> Whether this copy of the model is the authoritative one. </summary>
        public bool IsAuthority => Secret != null;

        /// <summary>
        /// One seat's hidden half, or null on a follower. <b>The only path to it.</b> Every reader and writer
        /// of the secret goes through <see cref="SecretOps"/>, which begins each method by resolving this and
        /// returning on null — which is what makes a follower run every secret excursion as a no-op.
        /// </summary>
        public SeatSecrets SecretSeat(int seat) => Secret?.Seats[seat];

        /// <summary> The content this match runs on. </summary>
        public SharedGameConfig Content => (SharedGameConfig)GameConfig;

        public override string GetDisplayNameForDashboard()
        {
            if (Seats == null || Seats.Count < MatchSeats.Count)
                return EntityId.ToString();

            return $"{Seats[0].DisplayName} vs {Seats[1].DisplayName}";
        }

        /// <summary> The seat this account owns, or <see cref="MatchSeats.None"/> when it owns neither. </summary>
        public int SeatIndexOfPlayer(EntityId playerId)
        {
            if (Seats == null || !playerId.IsValid)
                return MatchSeats.None;

            for (int seat = 0; seat < Seats.Count; seat++)
            {
                if (Seats[seat].PlayerId == playerId)
                    return seat;
            }

            return MatchSeats.None;
        }

        /// <summary>
        /// The subscribe-time private payload for one member: that seat's own hand, built from the secret
        /// rather than mirrored into a second field. The SDK calls this per subscriber and the client applies
        /// the answer before the initial checksum check.
        /// <para>
        /// Reached only on the server, where <see cref="Secret"/> is populated. On a client it answers null,
        /// which is also what a subscriber who is not a seat gets.
        /// </para>
        /// </summary>
        public override MultiplayerMemberPrivateStateBase GetMemberPrivateState(EntityId memberId)
        {
            int seat = SeatIndexOfPlayer(memberId);
            if (seat < 0 || !IsAuthority)
                return null;

            return new MatchMemberPrivateState(memberId, SecretOps.HandOf(this, seat), HandViews.BuildPendingChoice(this, seat));
        }

        /// <summary> Whether the table has stopped for good, with or without a result. </summary>
        public bool IsTerminal => Phase == MatchTablePhase.Ended || Phase == MatchTablePhase.Abandoned;

        /// <summary> Whether every seat with an owner has acknowledged the outcome. </summary>
        public bool AllSeatsAcked
        {
            get
            {
                if (Seats == null || ResultAcked == null)
                    return true;

                for (int seat = 0; seat < Seats.Count; seat++)
                {
                    if (Seats[seat].PlayerId.IsValid && !ResultAcked[seat])
                        return false;
                }

                return true;
            }
        }
    }
}
