using Game.Server.Match;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Threading.Tasks;

namespace Game.Server.Matchmaking
{
    /// <summary>
    /// The queue's own timings. A table's options and a queue's are different objects on purpose: they are set
    /// by different people for different reasons, and the one relationship that spans them is checked
    /// explicitly below rather than left to whoever edits one of the two files.
    /// <para>
    /// The band schedule itself is <em>not</em> here. It is design-fixed and explicitly untuned
    /// (<see cref="MatchmakingPolicy.DesignSteps"/>); what this can move is the fill wait, which is what lets
    /// the end-to-end profile make the solo case cheap without touching the policy's code.
    /// </para>
    /// </summary>
    [RuntimeOptions("Matchmaking", isStatic: false, "Timings for the ranked queue.")]
    public class MatchmakingOptions : RuntimeOptionsBase
    {
        [MetaDescription("How long a waiter goes unpaired before the queue seats it against a labelled bot at practice stakes. The queue always produces a game, so this is the honest worst case of a ranked tap.")]
        public TimeSpan FillWait { get; private set; } = TimeSpan.FromSeconds(45);

        [MetaDescription("How long the matchmaker waits for one player actor to commit to a seat. Well under the fill wait, because an unanswered ask holds a queue slot in the one singleton.")]
        public TimeSpan SeatAskTimeout { get; private set; } = TimeSpan.FromSeconds(3);

        [MetaDescription("How long minting a table may take before formation is treated as failed. The seats are already committed by then, so both are released and re-queued with the stamps they arrived with.")]
        public TimeSpan MintTimeout { get; private set; } = TimeSpan.FromSeconds(10);

        [MetaDescription("The player-side bound on Searching. The queue is in memory only, so a matchmaker restart loses every waiter's spot with nothing to rebuild it from — this is what turns that into 'tap Play again' rather than a client spinning on a queue it is not in.")]
        public TimeSpan SearchingBound { get; private set; } = TimeSpan.FromSeconds(75);

        [MetaDescription("The player-side bound on a committed seat. This is the one way a reservation could otherwise strand a player, because there is genuinely nothing left to cancel.")]
        public TimeSpan SeatedBound { get; private set; } = TimeSpan.FromSeconds(30);

        [MetaDescription("How far past a computed instant the queue's wake-up is scheduled. A punctual timer fires a hair early and the re-derived verdict is then the same 'wait', which costs a wake for nothing.")]
        public TimeSpan TimerPadding { get; private set; } = TimeSpan.FromMilliseconds(50);

        /// <summary> The design's band schedule, with this configuration's fill wait in it. </summary>
        public MatchmakingBandSchedule ToSchedule()
            => MatchmakingPolicy.ScheduleWithFillWait(MetaDuration.FromTimeSpan(FillWait));

        /// <summary>
        /// The bound ordering <c>matchmaking.md</c> states, and one relationship that spans two options
        /// classes. Every one of them fails <em>silently</em> when violated, which is what earns them a load
        /// failure: an actor that gave up on a search the matchmaker still went on to honour would tell a
        /// player the search failed and then pull them into a match a moment later.
        /// <para>
        /// Returns the complaint, or null when the two are consistent. Split out so it can be asked of the
        /// shipped defaults by a test without a running registry.
        /// </para>
        /// </summary>
        public static string DescribeViolation(MatchmakingOptions matchmaking, MatchOptions match)
        {
            if (matchmaking.FillWait < TimeSpan.Zero)
                return "Matchmaking:FillWait must not be negative.";

            if (matchmaking.SearchingBound <= matchmaking.FillWait + matchmaking.SeatAskTimeout)
            {
                return $"Matchmaking:SearchingBound ({matchmaking.SearchingBound}) must exceed Matchmaking:FillWait "
                     + $"({matchmaking.FillWait}) plus Matchmaking:SeatAskTimeout ({matchmaking.SeatAskTimeout}). A backstop "
                     + "that fires while the search it backs is still running tells a player their search failed and then "
                     + "pulls them into a match a moment later.";
            }

            if (matchmaking.SeatedBound <= matchmaking.SeatAskTimeout + matchmaking.MintTimeout)
            {
                return $"Matchmaking:SeatedBound ({matchmaking.SeatedBound}) must exceed Matchmaking:SeatAskTimeout "
                     + $"({matchmaking.SeatAskTimeout}) plus Matchmaking:MintTimeout ({matchmaking.MintTimeout}), which is the "
                     + "longest a formation can legitimately take after this seat committed to it.";
            }

            if (matchmaking.SearchingBound + matchmaking.SeatedBound >= match.ActorLinger)
            {
                return $"Matchmaking:SearchingBound ({matchmaking.SearchingBound}) plus Matchmaking:SeatedBound "
                     + $"({matchmaking.SeatedBound}) must stay under Match:ActorLinger ({match.ActorLinger}). The linger is "
                     + "also how long a formed table waits for its first subscriber, so a search whose bounds outlast it can "
                     + "seat a player at a table that has already been lost.";
            }

            return null;
        }

        public override async Task OnLoadedAsync(RuntimeOptionsRegistry registry)
        {
            // The cross-options relationship needs the other options block, and the SDK's own dependency
            // helper is what makes waiting for it a supported thing to do rather than a load-order gamble.
            MatchOptions match = await GetDependencyAsync<MatchOptions>(registry);

            string violation = DescribeViolation(this, match);
            if (violation != null)
                throw new InvalidOperationException(violation);
        }
    }
}
