using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Where an opponent's run came from: a scripted runner or a run the server validated.
    /// </summary>
    [MetaSerializable]
    public enum TimeTrialGhostKind
    {
        /// <summary>A scripted runner, driven from the course itself. What a course has before it has players.</summary>
        Seed = 0,

        /// <summary>A player's run the server validated, replayed from the inputs it accepted.</summary>
        Player = 1,
    }

    /// <summary>
    /// One opponent recording selected by the server for a particular course and simulation ruleset, with the
    /// little the client needs to present it. A seed ghost is driven from <see cref="TimeTrialBotDriver"/>;
    /// every other ghost is a run the server itself validated, replayed from the inputs it accepted.
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(10)]
    public sealed class TimeTrialGhost
    {
        [MetaMember(1)] public CourseId           CourseId       { get; private set; }
        [MetaMember(2)] public int                RulesetVersion { get; private set; }
        [MetaMember(3)] public TimeTrialRecording Recording      { get; private set; }

        /// <summary>What to call this opponent on screen. A house name for a seed, the player's name otherwise.</summary>
        [MetaMember(4)] public string DisplayName { get; private set; }

        /// <summary>The time the recording produces, so the client can label the opponent without replaying it first.</summary>
        [MetaMember(5)] public int FinishTimeMs { get; private set; }

        [MetaMember(6)] public TimeTrialGhostKind Kind { get; private set; }

        /// <summary>
        /// When this opponent crossed each of the course's gates, in milliseconds. Carried with the recording
        /// so a segment can be placed against it without replaying it first.
        /// </summary>
        [MetaMember(7)] public List<int> GateTimesMs { get; private set; } = new List<int>();

        /// <summary>
        /// The Skill this run was made with, or null for none. It is what the recording has to be replayed
        /// against to come out as it was run: the inputs are the same, but a Skill changes what they do.
        /// </summary>
        [MetaMember(8)] public SkillId SkillId { get; private set; }

        /// <summary>
        /// When this opponent reached each climb's bottom torii, in milliseconds. A segment ends there, so it
        /// takes both lists to place one against this opponent.
        /// </summary>
        [MetaMember(9)] public List<int> ClimbEntryTimesMs { get; private set; } = new List<int>();

        public TimeTrialGhost() { }

        public TimeTrialGhost(CourseId courseId, int rulesetVersion, TimeTrialRecording recording, string displayName, int finishTimeMs, TimeTrialGhostKind kind, List<int> gateTimesMs = null, SkillId skillId = null, List<int> climbEntryTimesMs = null)
        {
            CourseId          = courseId;
            RulesetVersion    = rulesetVersion;
            Recording         = recording;
            DisplayName       = displayName;
            FinishTimeMs      = finishTimeMs;
            Kind              = kind;
            GateTimesMs       = gateTimesMs ?? new List<int>();
            SkillId           = skillId;
            ClimbEntryTimesMs = climbEntryTimesMs ?? new List<int>();
        }

        /// <summary>
        /// Drives <paramref name="skill"/> around <paramref name="course"/> and returns the seed opponent it
        /// makes, carrying no Skill, or null if the scripted run does not reach the finish line.
        /// <paramref name="variant"/> picks which run of the level this is
        /// (<see cref="TimeTrialBotDriver.Drive(CourseDefinition, TimeTrialRules, TimeTrialBotSkill, int)"/>)
        /// and which house name it runs under. Variant 0 is the level's own seed ghost.
        /// </summary>
        public static TimeTrialGhost CreateSeed(CourseDefinition course, TimeTrialRules rules, TimeTrialBotSkill skill, int variant = 0)
        {
            TimeTrialRecording recording = TimeTrialBotDriver.Drive(course, rules, skill, variant);
            TimeTrialResult    result    = TimeTrialSimulation.Replay(course, rules, recording);
            if (result == null)
                return null;

            return new TimeTrialGhost(
                course.Id,
                TimeTrialConstants.RulesetVersion,
                recording,
                TimeTrialSeedRunners.NameFor(skill, variant),
                result.FinishTimeMs,
                TimeTrialGhostKind.Seed,
                result.GateTimesMs,
                climbEntryTimesMs: result.ClimbEntryTimesMs);
        }

        /// <summary>
        /// Whether this payload belongs to the open attempt and its recording is well formed and finishes
        /// <paramref name="course"/>. A ghost that is not playable is not shown.
        /// <paramref name="rules"/> must be this opponent's own effective rules, from
        /// <see cref="TimeTrialSkills.EffectiveRules(SharedGameConfig, SkillId)"/> with its <see cref="SkillId"/>,
        /// or what is checked is not the run it was.
        /// </summary>
        public bool IsPlayableFor(TimeTrialTicket ticket, CourseDefinition course, TimeTrialRules rules)
        {
            if (ticket == null || course == null || rules == null || Recording == null)
                return false;
            if (CourseId != ticket.CourseId || CourseId != course.Id)
                return false;
            if (RulesetVersion != ticket.RulesetVersion || RulesetVersion != TimeTrialConstants.RulesetVersion)
                return false;
            if (!TimeTrialRecording.IsWellFormed(Recording))
                return false;

            return TimeTrialSimulation.Replay(course, rules, Recording) != null;
        }
    }

    /// <summary>
    /// One opponent as the player model keeps it: what they did, with the recording left behind. This is what
    /// a segment is placed against, so it holds only the times.
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(1, 2)]
    public class TimeTrialLineupOpponent
    {
        /// <summary>This opponent's crossing time at each gate (each climb's top torii) in course order.</summary>
        [MetaMember(3)] public List<int> GateTimesMs { get; private set; } = new List<int>();

        [MetaMember(4)] public int FinishTimeMs { get; private set; }

        /// <summary>This opponent's crossing time at each climb's bottom torii, in course order.</summary>
        [MetaMember(5)] public List<int> ClimbEntryTimesMs { get; private set; } = new List<int>();

        public TimeTrialLineupOpponent() { }

        public TimeTrialLineupOpponent(List<int> gateTimesMs, int finishTimeMs, List<int> climbEntryTimesMs = null)
        {
            GateTimesMs       = gateTimesMs ?? new List<int>();
            FinishTimeMs      = finishTimeMs;
            ClimbEntryTimesMs = climbEntryTimesMs ?? new List<int>();
        }

        /// <summary>How long this opponent took over segment <paramref name="index"/>, on the same clock as the player's.</summary>
        public int SegmentTimeMs(int index) => TimeTrialSegments.TimeMs(GateTimesMs, ClimbEntryTimesMs, FinishTimeMs, index);
    }

    /// <summary>
    /// The field the server put in front of one attempt, tagged with the ticket it belongs to. It is kept on
    /// the player so the segments of that attempt can be placed against the very opponents it was raced
    /// against, and it is dropped the moment the attempt closes.
    /// </summary>
    [MetaSerializable]
    public class TimeTrialLineup
    {
        [MetaMember(1)] public int TicketId { get; private set; }

        [MetaMember(2)] public List<TimeTrialLineupOpponent> Opponents { get; private set; } = new List<TimeTrialLineupOpponent>();

        public TimeTrialLineup() { }

        public TimeTrialLineup(int ticketId, List<TimeTrialLineupOpponent> opponents)
        {
            TicketId  = ticketId;
            Opponents = opponents ?? new List<TimeTrialLineupOpponent>();
        }

        /// <summary>The lineup the given attempt was raced against, drawn from the opponents the server sent for it.</summary>
        public static TimeTrialLineup From(int ticketId, IReadOnlyList<TimeTrialGhost> lineup)
        {
            List<TimeTrialLineupOpponent> opponents = new List<TimeTrialLineupOpponent>();
            if (lineup != null)
            {
                foreach (TimeTrialGhost ghost in lineup)
                {
                    opponents.Add(new TimeTrialLineupOpponent(
                        new List<int>(ghost.GateTimesMs),
                        ghost.FinishTimeMs,
                        new List<int>(ghost.ClimbEntryTimesMs)));
                }
            }

            return new TimeTrialLineup(ticketId, opponents);
        }
    }

    /// <summary>
    /// The house runners a course's seed ghosts are presented as. They are named rather than anonymous so a
    /// scripted opponent never reads as another player, and the names are fixed in code so they are the same
    /// on every course and every server.
    /// </summary>
    public static class TimeTrialSeedRunners
    {
        /// <summary>
        /// How many opponents one run is given. With the player they make the field of four that segment
        /// placements are ranked in.
        /// </summary>
        public const int GhostsPerRun = 3;

        /// <summary>
        /// The scripted runner levels every course is seeded and checked with, fastest expected finish first.
        /// The first is par. The order must not change, because the house field's synthetic times are drawn per
        /// drive in this order.
        /// </summary>
        public static readonly IReadOnlyList<TimeTrialBotSkill> SeedLevels = new[]
        {
            TimeTrialBotSkill.Sharp,
            TimeTrialBotSkill.Competent,
            TimeTrialBotSkill.Cautious,
        };

        /// <summary>
        /// The house name of <paramref name="skill"/>'s run number <paramref name="variant"/>. Variant 0, and a
        /// variant with no name of its own, carries the level's name.
        /// </summary>
        public static string NameFor(TimeTrialBotSkill skill, int variant)
        {
            switch ((skill, variant))
            {
                case (TimeTrialBotSkill.Competent, 1): return "Cedar Messenger";
                case (TimeTrialBotSkill.Competent, 2): return "Rice Bale";
                case (TimeTrialBotSkill.Competent, 3): return "Temple Crow";
                default:                               return NameFor(skill);
            }
        }

        public static string NameFor(TimeTrialBotSkill skill)
        {
            switch (skill)
            {
                case TimeTrialBotSkill.Clumsy:   return "Paper Lantern";
                case TimeTrialBotSkill.Cautious: return "Straw Sandal";
                case TimeTrialBotSkill.Sharp:    return "Shrine Fox";
                default:                         return "Ash Courier";
            }
        }

        /// <summary>
        /// The <see cref="SeedLevels"/> driven over <paramref name="course"/>, fastest expected finish first,
        /// leaving out any runner that cannot finish it. This is the field a run gets when no pool gave it one.
        /// </summary>
        public static List<TimeTrialGhost> Lineup(CourseDefinition course, TimeTrialRules rules)
        {
            List<TimeTrialGhost> lineup = new List<TimeTrialGhost>();

            foreach (TimeTrialBotSkill skill in SeedLevels)
            {
                TimeTrialGhost seed = TimeTrialGhost.CreateSeed(course, rules, skill);
                if (seed != null)
                    lineup.Add(seed);
            }

            return lineup;
        }

        /// <summary>
        /// The field a player races on <paramref name="course"/>: <paramref name="lineup"/> as it was selected,
        /// with the clumsy runner added (<see cref="WithFirstRunGhost"/>) when
        /// <paramref name="hasCompletedARun"/> is false.
        /// </summary>
        public static List<TimeTrialGhost> FieldFor(bool hasCompletedARun, List<TimeTrialGhost> lineup, CourseDefinition course, TimeTrialRules rules, int maxGhosts)
        {
            if (hasCompletedARun)
                return lineup;

            return WithFirstRunGhost(lineup, course, rules, maxGhosts);
        }

        /// <summary>
        /// The field a run on <paramref name="course"/> is raced against: <paramref name="selected"/>, the
        /// opponents a pool gave it, or the scripted runners (<see cref="Lineup"/>) when there are none, drawn
        /// as <see cref="FieldFor"/> draws it with <see cref="GhostsPerRun"/> as the field size.
        /// </summary>
        public static List<TimeTrialGhost> RunField(bool hasCompletedARun, List<TimeTrialGhost> selected, CourseDefinition course, TimeTrialRules rules)
        {
            List<TimeTrialGhost> lineup = selected != null && selected.Count > 0 ? selected : Lineup(course, rules);
            return FieldFor(hasCompletedARun, lineup, course, rules, GhostsPerRun);
        }

        /// <summary>
        /// A copy of <paramref name="lineup"/> with the clumsy runner driven over <paramref name="course"/> in it,
        /// fastest first. The slowest opponents give way so the field stays within <paramref name="maxGhosts"/>.
        /// A course the clumsy runner cannot finish leaves the field as it was.
        /// </summary>
        public static List<TimeTrialGhost> WithFirstRunGhost(List<TimeTrialGhost> lineup, CourseDefinition course, TimeTrialRules rules, int maxGhosts)
        {
            List<TimeTrialGhost> field = lineup != null ? new List<TimeTrialGhost>(lineup) : new List<TimeTrialGhost>();
            if (course == null || maxGhosts <= 0)
                return field;

            // The pool never holds the clumsy runner, so it is driven here. It walks into every obstacle, which
            // gives a first run someone in the field to finish ahead of.
            TimeTrialGhost clumsy = TimeTrialGhost.CreateSeed(course, rules, TimeTrialBotSkill.Clumsy);
            if (clumsy == null)
                return field;

            // Fastest first, so the opponents given up for it are the slowest ones.
            field.Sort((a, b) => a.FinishTimeMs.CompareTo(b.FinishTimeMs));
            while (field.Count >= maxGhosts)
                field.RemoveAt(field.Count - 1);

            field.Add(clumsy);
            field.Sort((a, b) => a.FinishTimeMs.CompareTo(b.FinishTimeMs));
            return field;
        }
    }
}
