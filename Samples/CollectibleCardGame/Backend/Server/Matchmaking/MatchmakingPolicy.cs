using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Server.Matchmaking
{
    /// <summary>
    /// One step of the widening schedule: from <see cref="Threshold"/> of elapsed wait onward, two waiters are
    /// compatible when both gaps fall inside these bands.
    /// </summary>
    public sealed class MatchmakingBandStep
    {
        /// <summary> The elapsed wait at which this step begins. The first step's is zero. </summary>
        public readonly MetaDuration Threshold;

        /// <summary> The rating gap this step tolerates, or null for no bound at all. </summary>
        public readonly int? RatingBand;

        /// <summary> The Power Score gap this step tolerates. Never unbounded — see the class comment. </summary>
        public readonly int PowerScoreBand;

        public MatchmakingBandStep(MetaDuration threshold, int? ratingBand, int powerScoreBand)
        {
            Threshold      = threshold;
            RatingBand     = ratingBand;
            PowerScoreBand = powerScoreBand;
        }
    }

    /// <summary>
    /// The widening schedule and the wait that ends it. Data rather than a table inlined in the policy, so
    /// <see cref="MatchmakingOptions"/> can move the fill wait for a test without the policy knowing.
    /// </summary>
    public sealed class MatchmakingBandSchedule
    {
        /// <summary> Ordered by <see cref="MatchmakingBandStep.Threshold"/> ascending, the first at zero. </summary>
        public readonly IReadOnlyList<MatchmakingBandStep> Steps;

        /// <summary> How long a waiter may go unpaired before it takes the labelled bot instead. </summary>
        public readonly MetaDuration FillWait;

        public MatchmakingBandSchedule(IReadOnlyList<MatchmakingBandStep> steps, MetaDuration fillWait)
        {
            Steps    = steps;
            FillWait = fillWait;
        }
    }

    /// <summary>
    /// Given the queue and an instant: pair these two, seat this one against a bot, or wait — and when to look
    /// again. <b>Pure.</b> It reads no clock of its own, touches no actor and looks at no config, and it is
    /// re-derived from scratch on every trigger: an arrival, a cancellation, a formation that left a
    /// remainder, and a scheduled wake-up all call <see cref="Evaluate"/> the same way
    /// (<c>Docs/matchmaking.md</c>, "The band policy").
    /// <para>
    /// Statelessness is what makes the host's timer need no bookkeeping: a stale callback firing late simply
    /// re-derives the current, correct verdict, so nothing is ever cancelled and there is nothing to
    /// supersede.
    /// </para>
    /// </summary>
    public static class MatchmakingPolicy
    {
        /// <summary> What to do about the queue right now. Exactly one action per call, by design. </summary>
        public abstract record Verdict;

        /// <summary> Seat these two against each other. </summary>
        public sealed record FormPair(MatchmakingTicket A, MatchmakingTicket B) : Verdict;

        /// <summary> This waiter has passed every band; seat it against a labelled bot at practice stakes. </summary>
        public sealed record FormBotMatch(MatchmakingTicket Waiter) : Verdict;

        /// <summary> Nothing to do. <paramref name="NextEvaluationAt"/> is null only for an empty queue. </summary>
        public sealed record Wait(MetaTime? NextEvaluationAt) : Verdict;

        /// <summary>
        /// The design's own table (<c>Docs/matchmaking.md</c>, "The band policy"), and not this
        /// code's to change. Three things about it are deliberate: the narrowest Power Score band
        /// sits inside the even-stakes threshold, so a fast pairing is always a plain symmetric Heist; the
        /// rating band opens all the way and the Power Score band never does, because a rating mismatch
        /// produces an unpleasant game while a Power Score mismatch produces a distorted wager; and the bands
        /// are steps rather than a ramp, which gives the timer a small set of instants at which the answer can
        /// change.
        /// </summary>
        public static readonly IReadOnlyList<MatchmakingBandStep> DesignSteps = new List<MatchmakingBandStep>
        {
            new MatchmakingBandStep(MetaDuration.Zero,             ratingBand: 100,  powerScoreBand: 10),
            new MatchmakingBandStep(MetaDuration.FromSeconds(10),  ratingBand: 250,  powerScoreBand: 25),
            new MatchmakingBandStep(MetaDuration.FromSeconds(25),  ratingBand: null, powerScoreBand: 40),
        };

        /// <summary> The design's schedule with one fill wait in it. </summary>
        public static MatchmakingBandSchedule ScheduleWithFillWait(MetaDuration fillWait)
            => new MatchmakingBandSchedule(DesignSteps, fillWait);

        /// <summary> How long this ticket has been waiting, floored at zero. </summary>
        public static MetaDuration Waited(MatchmakingTicket ticket, MetaTime now)
        {
            MetaDuration waited = now - ticket.ArrivedAt;
            return waited < MetaDuration.Zero ? MetaDuration.Zero : waited;
        }

        /// <summary>
        /// The band step in force for a ticket. "Waited" is always this ticket's own elapsed time — never the
        /// queue's age and never the host timer's, which is a different mechanism with a similar-sounding
        /// description (<c>Docs/matchmaking.md</c>, "The timer is armed from the oldest waiter").
        /// </summary>
        public static MatchmakingBandStep BandFor(MatchmakingTicket ticket, MetaTime now, MatchmakingBandSchedule schedule)
        {
            MetaDuration        waited = Waited(ticket, now);
            MatchmakingBandStep band   = schedule.Steps[0];

            foreach (MatchmakingBandStep step in schedule.Steps)
            {
                if (waited >= step.Threshold)
                    band = step;
            }

            return band;
        }

        /// <summary>
        /// Whether two waiters may be paired: both gaps inside <b>the more tolerant of the two waiters'
        /// bands</b>, which is the design's rule verbatim. Requiring both bands to hold would mean a fresh
        /// arrival can never be paired outside its opening band, which starves precisely the person the
        /// widening exists for — the long waiter would watch compatible-to-them arrivals pass through
        /// untouched.
        /// <para>
        /// In <see cref="Evaluate"/>'s own scan order the pivot is always the older of the two, so the maximum
        /// collapses to the pivot's own band. The general rule is implemented anyway because it is the rule,
        /// and because this function is the one a test can ask with the fresher ticket first.
        /// </para>
        /// </summary>
        public static bool AreCompatible(MatchmakingTicket a, MatchmakingTicket b, MetaTime now, MatchmakingBandSchedule schedule)
        {
            MatchmakingBandStep bandA = BandFor(a, now, schedule);
            MatchmakingBandStep bandB = BandFor(b, now, schedule);

            // A null rating band is unbounded, so either side being null makes the rating axis unbounded.
            bool ratingFits = !bandA.RatingBand.HasValue
                              || !bandB.RatingBand.HasValue
                              || RatingGap(a, b) <= Math.Max(bandA.RatingBand.Value, bandB.RatingBand.Value);

            bool powerScoreFits = PowerScoreGap(a, b) <= Math.Max(bandA.PowerScoreBand, bandB.PowerScoreBand);

            return ratingFits && powerScoreFits;
        }

        public static int RatingGap(MatchmakingTicket a, MatchmakingTicket b) => Math.Abs(a.Rating - b.Rating);

        public static int PowerScoreGap(MatchmakingTicket a, MatchmakingTicket b) => Math.Abs(a.PowerScore - b.PowerScore);

        /// <summary>
        /// One action for this queue at this instant. The scan is oldest-first, so the longest waiter gets
        /// first refusal, and among that waiter's compatible partners it takes the <b>closest</b> rather than
        /// the first found: burning a near-perfect pairing on a marginal one is cheap to avoid at this queue
        /// size, and the quadratic scan it implies is not a cost worth thinking about below numbers this
        /// sample will never see.
        /// <para>
        /// Pairing beats the bot fallback whenever both are available: the fill wait is what the queue does
        /// when it has run out of humans, not a deadline that outranks one.
        /// </para>
        /// </summary>
        public static Verdict Evaluate(IReadOnlyList<MatchmakingTicket> queue, MetaTime now, MatchmakingBandSchedule schedule)
        {
            if (queue == null || queue.Count == 0)
                return new Wait(null);

            List<MatchmakingTicket> byAge = OldestFirst(queue);

            for (int pivotNdx = 0; pivotNdx < byAge.Count; pivotNdx++)
            {
                MatchmakingTicket pivot = byAge[pivotNdx];
                MatchmakingTicket best  = ClosestPartner(pivot, byAge, now, schedule);

                if (best != null)
                    return new FormPair(pivot, best);
            }

            // Only the oldest can have reached the fill wait first, but the loop is over every ticket so that
            // a queue whose stamps arrived out of order still answers for the one that is actually expired.
            foreach (MatchmakingTicket ticket in byAge)
            {
                if (Waited(ticket, now) >= schedule.FillWait)
                    return new FormBotMatch(ticket);
            }

            return new Wait(NextEvaluationAt(byAge, now, schedule));
        }

        /// <summary>
        /// The next instant at which <see cref="Evaluate"/> could answer differently: the earlier of the next
        /// band-step boundary for any waiter and the earliest fill deadline. A fill-only timer would leave two
        /// perfectly pairable players sitting in the same queue until one of them timed out into a bot,
        /// because compatibility changes with elapsed time and nothing else has to happen for it to.
        /// </summary>
        public static MetaTime? NextEvaluationAt(IReadOnlyList<MatchmakingTicket> queue, MetaTime now, MatchmakingBandSchedule schedule)
        {
            MetaTime? next = null;

            foreach (MatchmakingTicket ticket in queue)
            {
                MetaDuration waited = Waited(ticket, now);

                foreach (MatchmakingBandStep step in schedule.Steps)
                {
                    if (waited < step.Threshold)
                    {
                        next = Earlier(next, ticket.ArrivedAt + step.Threshold);
                        break;
                    }
                }

                if (waited < schedule.FillWait)
                    next = Earlier(next, ticket.ArrivedAt + schedule.FillWait);
            }

            return next;
        }

        /// <summary>
        /// The pivot's compatible candidate that is closest to it, or null when it has none. The comparator is
        /// smallest rating gap, then smallest Power Score gap, then the oldest arrival — rating pairing is
        /// what makes the game worth playing, so it is the primary key, and the arrival tie-break is what
        /// keeps the answer a function of the queue rather than of its list order.
        /// </summary>
        static MatchmakingTicket ClosestPartner(MatchmakingTicket pivot, List<MatchmakingTicket> byAge, MetaTime now, MatchmakingBandSchedule schedule)
        {
            MatchmakingTicket best = null;

            foreach (MatchmakingTicket candidate in byAge)
            {
                // By account rather than by reference. Two tickets for one account are compatible with each
                // other at a gap of zero, so a queue that ever held both — an actor that restarted while a
                // requeue was in flight is the way there — would pair a player with themselves. The seat
                // reservation declines the second ask and would unwind it, but an invariant this plain should
                // not rest on the unwinding.
                if (candidate.PlayerId == pivot.PlayerId)
                    continue;
                if (!AreCompatible(pivot, candidate, now, schedule))
                    continue;

                if (best == null || IsCloser(pivot, candidate, best))
                    best = candidate;
            }

            return best;
        }

        static bool IsCloser(MatchmakingTicket pivot, MatchmakingTicket candidate, MatchmakingTicket incumbent)
        {
            int ratingGap = RatingGap(pivot, candidate) - RatingGap(pivot, incumbent);
            if (ratingGap != 0)
                return ratingGap < 0;

            int powerScoreGap = PowerScoreGap(pivot, candidate) - PowerScoreGap(pivot, incumbent);
            if (powerScoreGap != 0)
                return powerScoreGap < 0;

            return CompareByAge(candidate, incumbent) < 0;
        }

        /// <summary> A copy of the queue in arrival order, so the caller's list is never reordered. </summary>
        static List<MatchmakingTicket> OldestFirst(IReadOnlyList<MatchmakingTicket> queue)
        {
            List<MatchmakingTicket> byAge = new List<MatchmakingTicket>(queue);
            byAge.Sort(CompareByAge);
            return byAge;
        }

        /// <summary>
        /// Arrival first, then the account id. The second key is not cosmetic: two tickets can share an
        /// arrival instant, and a verdict that depended on which of them the list happened to hold first would
        /// be a different answer on two nodes running the same queue.
        /// </summary>
        static int CompareByAge(MatchmakingTicket a, MatchmakingTicket b)
        {
            int byArrival = a.ArrivedAt.CompareTo(b.ArrivedAt);
            return byArrival != 0 ? byArrival : a.PlayerId.CompareTo(b.PlayerId);
        }

        static MetaTime? Earlier(MetaTime? current, MetaTime candidate)
        {
            if (!current.HasValue)
                return candidate;

            return candidate < current.Value ? candidate : current;
        }
    }
}
