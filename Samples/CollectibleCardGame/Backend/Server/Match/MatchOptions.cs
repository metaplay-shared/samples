using Game.Logic;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Threading.Tasks;

namespace Game.Server.Match
{
    /// <summary>
    /// The match's timings (<c>Docs/match.md</c>, "Timing budget"). The durations the <b>rules</b>
    /// consume become <see cref="MatchTimings"/> on the model at setup; the rest are read by the actor
    /// (<c>Docs/protocol.md</c>).
    /// </summary>
    [RuntimeOptions("Match", isStatic: false, "Pacing and lifetime timings for the match entity.")]
    public class MatchOptions : RuntimeOptionsBase
    {
        // ---- handed to the engine ----

        [MetaDescription("The shared mulligan deadline. Zero arms none, and a seat then keeps the hand it was dealt only if it never submits.")]
        public TimeSpan MulliganDeadline { get; private set; } = TimeSpan.FromSeconds(30);

        [MetaDescription("How long a seat has to take its turn. Zero means no deadline is in force, so the board draws no ring.")]
        public TimeSpan TurnDeadline { get; private set; } = TimeSpan.FromSeconds(60);

        [MetaDescription("The per-seat, per-match reserve bank the turn deadline is extended out of.")]
        public TimeSpan TurnReserveBank { get; private set; } = TimeSpan.FromSeconds(60);

        [MetaDescription("One extension's worth of reserve. Only a seat that has already acted this turn may draw one.")]
        public TimeSpan TurnReserveExtension { get; private set; } = TimeSpan.FromSeconds(15);

        [MetaDescription("How long the owner of a held peek has to choose before the deterministic default applies.")]
        public TimeSpan EffectChoiceDeadline { get; private set; } = TimeSpan.FromSeconds(20);

        [MetaDescription("Fixed allowance on each newly opened decision clock for client catch-up. Never holds input or delays publication.")]
        public TimeSpan PresentationAllowance { get; private set; } = TimeSpan.FromSeconds(3);

        MetaDuration DecisionDuration(TimeSpan duration)
            => duration <= TimeSpan.Zero ? MetaDuration.Zero : MetaDuration.FromTimeSpan(duration + PresentationAllowance);

        // ---- the actor's own ----

        [MetaDescription("How long a formed table waits for both humans to subscribe. On expiry a human seat that never arrived is covered, and a table nobody came to is abandoned.")]
        public TimeSpan JoinWindow { get; private set; } = TimeSpan.FromSeconds(15);

        [MetaDescription("How long a dropped seat is held before a bot covers it. Reconnecting inside it re-attaches the player to the same match.")]
        public TimeSpan DisconnectGrace { get; private set; } = TimeSpan.FromSeconds(30);

        [MetaDescription("How many consecutive lapsed turn deadlines a connected seat takes before a bot covers it. Zero turns the count off, and a lapse then only auto-plays the rest of the turn.")]
        public int StrikesBeforeCover { get; private set; } = 2;

        [MetaDescription("How long a minted table waits for its first subscriber, and how long it stays alive after its last one leaves. Both windows, because a table is the only copy of its own game.")]
        public TimeSpan ActorLinger { get; private set; } = TimeSpan.FromSeconds(150);

        [MetaDescription("How far past a deadline a timer is scheduled. A punctual timer fires a hair early and the expiry check then no-ops, leaving the table waiting on a stamp nothing will offer again.")]
        public TimeSpan TimerPadding { get; private set; } = TimeSpan.FromMilliseconds(50);

        [MetaDescription("How long to wait before re-offering a result the account has not acknowledged.")]
        public TimeSpan ResultRetryInterval { get; private set; } = TimeSpan.FromSeconds(30);

        [MetaDescription("How long the actor is held awake to deliver a result, whether or not anybody is subscribed. It must have room for every attempt the budget allows.")]
        public TimeSpan ResultDeliveryWindow { get; private set; } = TimeSpan.FromSeconds(120);

        [MetaDescription("How many times the whole delivery round is attempted before it is given up and logged as an error.")]
        public int ResultDeliveryAttempts { get; private set; } = 4;

        // ---- bot pacing ----

        [MetaDescription("The whole turn's think budget for a bot seat. Budgeted per turn rather than per action: a CCG turn holds several, and two seconds each across eight actions eats a quarter of the turn timer.")]
        public TimeSpan BotTurnBudget { get; private set; } = TimeSpan.FromSeconds(12);

        [MetaDescription("The floor on one bot action's think delay.")]
        public TimeSpan BotActionDelay { get; private set; } = TimeSpan.FromMilliseconds(900);

        [MetaDescription("How much a bot action's delay may vary above its floor.")]
        public TimeSpan BotActionDelayJitter { get; private set; } = TimeSpan.FromMilliseconds(600);

        // ---- the Heist ----

        [MetaDescription("How long a present winner has to make the Heist pick.")]
        public TimeSpan HeistPickDeadline { get; private set; } = TimeSpan.FromSeconds(45);

        /// <summary> The durations the rules consume, in the shape the model holds them. </summary>
        public MatchTimings ToMatchTimings()
        {
            return new MatchTimings(
                mulliganDeadline:     DecisionDuration(MulliganDeadline),
                turnDeadline:         DecisionDuration(TurnDeadline),
                turnReserveBank:      MetaDuration.FromTimeSpan(TurnReserveBank),
                turnReserveExtension: MetaDuration.FromTimeSpan(TurnReserveExtension),
                effectChoiceDeadline: DecisionDuration(EffectChoiceDeadline));
        }

        /// <summary> The relationships that fail silently when violated, checked loudly at load instead. </summary>
        public override Task OnLoadedAsync()
        {
            if (PresentationAllowance < TimeSpan.Zero || PresentationAllowance > TimeSpan.FromSeconds(10))
                throw new InvalidOperationException("Match:PresentationAllowance must be between zero and ten seconds.");

            if (StrikesBeforeCover < 0)
            {
                throw new InvalidOperationException(
                    $"Match:StrikesBeforeCover ({StrikesBeforeCover}) must be zero or more. Zero turns the count off.");
            }
            if (ActorLinger <= DisconnectGrace)
            {
                throw new InvalidOperationException(
                    $"Match:ActorLinger ({ActorLinger}) must exceed Match:DisconnectGrace ({DisconnectGrace}). "
                    + "A shorter linger evicts the actor while a seat is still on grace, so the grace never lapses and the play-out "
                    + "that makes leaving cost what staying would never runs.");
            }

            if (ActorLinger <= JoinWindow)
            {
                throw new InvalidOperationException(
                    $"Match:ActorLinger ({ActorLinger}) must exceed Match:JoinWindow ({JoinWindow}). "
                    + "The linger is also how long a minted table waits for its first subscriber, and a table that dies before its "
                    + "players can boot into it is lost.");
            }

            // The last of N attempts lands (N-1) intervals after the first, plus the timer padding.
            TimeSpan lastAttemptAt = (ResultDeliveryAttempts - 1) * ResultRetryInterval + TimerPadding;
            if (ResultDeliveryWindow <= lastAttemptAt)
            {
                throw new InvalidOperationException(
                    $"Match:ResultDeliveryWindow ({ResultDeliveryWindow}) must exceed {lastAttemptAt}, which is when the last of "
                    + $"Match:ResultDeliveryAttempts ({ResultDeliveryAttempts}) attempts fires at Match:ResultRetryInterval "
                    + $"({ResultRetryInterval}) apart, plus Match:TimerPadding ({TimerPadding}).");
            }

            return Task.CompletedTask;
        }
    }
}
