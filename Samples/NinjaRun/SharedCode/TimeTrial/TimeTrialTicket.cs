using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The identity of one attempt. Issued by <see cref="PlayerOpenTimeTrial"/>,
    /// <see cref="PlayerStartPracticeTimeTrial"/> or <see cref="PlayerStartDraftPartTimeTrial"/> before the run,
    /// submitted back with the recording, and consumed when the result is accepted or rejected, so one
    /// recording can be counted once and only against the course, ruleset and Skill it was played on, and a
    /// practice attempt is never counted at all.
    /// </summary>
    [MetaSerializable]
    public class TimeTrialTicket
    {
        /// <summary>Unique within the player, counting up from 1.</summary>
        [MetaMember(1)] public int      TicketId       { get; private set; }
        [MetaMember(2)] public CourseId CourseId       { get; private set; }
        [MetaMember(3)] public int      RulesetVersion { get; private set; }
        [MetaMember(4)] public MetaTime IssuedAt       { get; private set; }

        /// <summary>
        /// The Skill equipped when the attempt opened, or null for none. Fixed here for the life of the
        /// attempt: changing the slot mid-run cannot change what the server replays the recording against.
        /// </summary>
        [MetaMember(5)] public SkillId SkillId { get; private set; }

        /// <summary>
        /// Whether the attempt is practice: opened by a developer on a course they named, alone or against the
        /// field that course would give a real run. A practice run is still replayed and its time shown, but
        /// accepting it writes no best time, no run count, no Orbs, no placements and nothing into a pool.
        /// </summary>
        [MetaMember(6)] public bool IsPractice { get; private set; }

        /// <summary>
        /// The part the attempt is played on, composed alone, when it is a draft from the part editor rather
        /// than anything the published config holds; null for every other attempt. Carried by value because a
        /// draft has no id anything resolves, and it is what lets the server recompute the recording against the
        /// geometry the client played. <see cref="CourseId"/> is then the draft's own id, which
        /// <see cref="CourseResolver"/> answers for only through this ticket. Always a practice ticket.
        /// </summary>
        [MetaMember(7)] public DraftCoursePart DraftPart { get; private set; }

        public TimeTrialTicket() { }

        public TimeTrialTicket(int ticketId, CourseId courseId, int rulesetVersion, MetaTime issuedAt, SkillId skillId = null, bool isPractice = false, DraftCoursePart draftPart = null)
        {
            TicketId       = ticketId;
            CourseId       = courseId;
            RulesetVersion = rulesetVersion;
            IssuedAt       = issuedAt;
            SkillId        = skillId;
            IsPractice     = isPractice;
            DraftPart      = draftPart;
        }
    }

    /// <summary>
    /// What the simulation made of an attempt: the finish time it computed, how many steps the run took, and
    /// when it crossed each of the torii a course's climbs are bracketed by. The client shows its own copy as
    /// provisional; the one the server computed is the one that is stored and the one the segments are placed
    /// from.
    /// </summary>
    [MetaSerializable]
    public class TimeTrialResult
    {
        /// <summary>Time from the start to the finish line, in milliseconds, interpolated within the crossing step.</summary>
        [MetaMember(1)] public int FinishTimeMs { get; private set; }

        /// <summary>Whole simulation steps run, including the one the finish line was crossed on.</summary>
        [MetaMember(2)] public int Steps { get; private set; }

        /// <summary>
        /// Crossing time of each of the course's gates (the top torii of each climb) in milliseconds and in
        /// course order, timed inside the crossing step the same way <see cref="FinishTimeMs"/> is. Each one
        /// opens a segment. Empty on a course with no climbs.
        /// </summary>
        [MetaMember(3)] public List<int> GateTimesMs { get; private set; } = new List<int>();

        /// <summary>
        /// Crossing time of each climb's bottom torii, in milliseconds and in course order and timed the same
        /// way. Each one closes a segment, which is why it travels beside <see cref="GateTimesMs"/>.
        /// </summary>
        [MetaMember(4)] public List<int> ClimbEntryTimesMs { get; private set; } = new List<int>();

        public TimeTrialResult() { }

        public TimeTrialResult(int finishTimeMs, int steps, List<int> gateTimesMs = null, List<int> climbEntryTimesMs = null)
        {
            FinishTimeMs      = finishTimeMs;
            Steps             = steps;
            GateTimesMs       = gateTimesMs ?? new List<int>();
            ClimbEntryTimesMs = climbEntryTimesMs ?? new List<int>();
        }

        /// <summary>How many segments the course was raced in: one per climb, plus the one the finish closes.</summary>
        public int SegmentCount => TimeTrialSegments.CountFor(ClimbEntryTimesMs.Count);

        /// <summary>
        /// How long segment <paramref name="index"/> took, by the one definition of a segment's ends in
        /// <see cref="TimeTrialSegments"/>.
        /// </summary>
        public int SegmentTimeMs(int index) => TimeTrialSegments.TimeMs(GateTimesMs, ClimbEntryTimesMs, FinishTimeMs, index);

        public override string ToString() => $"{FinishTimeMs} ms ({Steps} steps)";
    }

    /// <summary>
    /// Why a submitted recording was not counted. Player-facing: the client turns one of these into the reason
    /// it shows, so each value names something the player could act on rather than an internal failure.
    /// </summary>
    [MetaSerializable]
    public enum TimeTrialRejectReason
    {
        /// <summary>No rejection recorded.</summary>
        None = 0,

        /// <summary>The player had no run open: nothing was started, or a previous submission consumed it.</summary>
        NoActiveTicket = 1,

        /// <summary>The submitted ticket is not the one the player has open, so a newer run replaced it.</summary>
        TicketMismatch = 2,

        /// <summary>The run was open for longer than <see cref="GlobalConfig.TimeTrialTicketLifetimeSeconds"/>.</summary>
        TicketExpired = 3,

        /// <summary>The ticket was issued against simulation rules this server no longer runs.</summary>
        UnsupportedRuleset = 4,

        /// <summary>The ticket names a course the published config does not resolve.</summary>
        UnknownCourse = 5,

        /// <summary>The recording is not something the simulation can run: out of order, non-alternating, or too large.</summary>
        MalformedRecording = 6,

        /// <summary>Running the recording did not reach the finish line, so there is no time to record.</summary>
        DidNotFinish = 7,
    }
}
