using Metaplay.Core.Math;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One runner's go at a course, as the developer course tools list it: the scripted runner at one skill
    /// level, or the run with no input at all, and what the simulation made of it.
    /// </summary>
    public sealed class CourseRunnerTime
    {
        /// <summary>The scripted runner's skill level, or null for the run with no input.</summary>
        public TimeTrialBotSkill? Skill { get; }

        /// <summary>The finish, gate and climb times the run produced, or null when it never finished.</summary>
        public TimeTrialResult Result { get; }

        public CourseRunnerTime(TimeTrialBotSkill? skill, TimeTrialResult result)
        {
            Skill  = skill;
            Result = result;
        }
    }

    /// <summary>
    /// What the developer course tools read off a course without playing it: the scripted runners' times over
    /// it, the lengths of the segments it is raced in, and a part composed on its own. Everything here is the
    /// same code the config build and the tests run, so the page shows exactly what the gate saw.
    /// </summary>
    public static class CourseInspection
    {
        /// <summary>The skill levels the tools time a course at: the <see cref="TimeTrialSeedRunners.SeedLevels"/>, slowest first.</summary>
        public static readonly IReadOnlyList<TimeTrialBotSkill> TimedSkills = SlowestFirst(TimeTrialSeedRunners.SeedLevels);

        static TimeTrialBotSkill[] SlowestFirst(IReadOnlyList<TimeTrialBotSkill> fastestFirst)
        {
            TimeTrialBotSkill[] reversed = new TimeTrialBotSkill[fastestFirst.Count];
            for (int ndx = 0; ndx < reversed.Length; ndx++)
                reversed[ndx] = fastestFirst[fastestFirst.Count - 1 - ndx];
            return reversed;
        }

        /// <summary>
        /// The run with no input first, then each scripted runner of <see cref="TimedSkills"/>, every one driven
        /// over <paramref name="course"/> and replayed from its recording, the way a seed ghost's times are
        /// produced, so these are the times a new pool would be seeded with.
        /// </summary>
        public static List<CourseRunnerTime> RunnerTimes(CourseDefinition course, TimeTrialRules rules)
        {
            List<CourseRunnerTime> times = new List<CourseRunnerTime>();
            times.Add(new CourseRunnerTime(null, TimeTrialSimulation.Replay(course, rules, new TimeTrialRecording())));

            foreach (TimeTrialBotSkill skill in TimedSkills)
                times.Add(new CourseRunnerTime(skill, TimeTrialSimulation.Replay(course, rules, TimeTrialBotDriver.Drive(course, rules, skill))));

            return times;
        }

        /// <summary>
        /// How long each raced segment is along X, in course order: from the start line or a gate to the next
        /// bottom torii, or to the finish line for the last. The climbs between them belong to no segment, so
        /// they are not in any of these lengths.
        /// </summary>
        public static List<F32> SegmentLengths(CourseDefinition course)
        {
            List<F32> lengths = new List<F32>();

            F32 segmentStart = course.StartX;
            foreach (ClimbDefinition climb in course.Climbs)
            {
                lengths.Add(climb.BottomX - segmentStart);
                segmentStart = climb.GateX;
            }

            lengths.Add(course.FinishX - segmentStart);
            return lengths;
        }

        /// <summary>
        /// <paramref name="part"/> composed on its own, named by the part's id: the course the config build
        /// drives to prove a part is playable before it is composed with anything. The id is not one
        /// <see cref="CourseResolver"/> answers for, so this is a course to look at and time; the one run that
        /// is opened on it is the part editor's, whose ticket carries the part itself
        /// (<see cref="DraftCoursePart"/>). Null for a part that breaks one of its own rules.
        /// </summary>
        public static CourseDefinition PartAlone(CoursePartInfo part, GlobalConfig global)
        {
            if (part == null || global == null || part.Validate().Count > 0)
                return null;

            return CourseComposer.Compose(CourseId.FromString(part.Id.Value), new[] { part }, global);
        }
    }
}
