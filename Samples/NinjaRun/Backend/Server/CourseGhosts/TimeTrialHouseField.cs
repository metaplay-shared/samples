using Game.Logic;
using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Server.CourseGhosts
{
    /// <summary>
    /// The best times a course's pool is seeded with, so a standing means something on a course few players
    /// have run: the scripted <see cref="Drives"/>, kept as opponents, and synthetic times hung off each drive,
    /// counted as best times only. A course's house field is the same wherever and whenever it is built.
    /// </summary>
    public static class TimeTrialHouseField
    {
        /// <summary>
        /// How many further runs the competent level drives after its own. The extra runs are all competent
        /// ones: the sharp level has no jitter, and the cautious level over-charges so far that its jitter is
        /// clamped away on most jumps, so further runs of either would repeat the same time.
        /// </summary>
        public const int ExtraCompetentRuns = 3;

        /// <summary>
        /// The scripted runs driven over a course: each of <see cref="TimeTrialSeedRunners.SeedLevels"/> in
        /// order, fastest first, as its own run (variant 0), with the <see cref="ExtraCompetentRuns"/> following
        /// the competent level's. The first is par.
        /// </summary>
        public static readonly IReadOnlyList<(TimeTrialBotSkill Skill, int Variant)> Drives = DrivesInOrder();

        static List<(TimeTrialBotSkill Skill, int Variant)> DrivesInOrder()
        {
            List<(TimeTrialBotSkill Skill, int Variant)> drives = new List<(TimeTrialBotSkill Skill, int Variant)>();
            foreach (TimeTrialBotSkill skill in TimeTrialSeedRunners.SeedLevels)
            {
                drives.Add((skill, 0));
                if (skill == TimeTrialBotSkill.Competent)
                {
                    for (int variant = 1; variant <= ExtraCompetentRuns; variant++)
                        drives.Add((skill, variant));
                }
            }
            return drives;
        }

        /// <summary>How many synthetic times are hung off each real drive that finishes.</summary>
        public const int SyntheticsPerDrive = 5;

        /// <summary>The least one botched jump costs, in permille of par.</summary>
        public const int BotchMinPermille = 5;

        /// <summary>The most one botched jump costs, in permille of par.</summary>
        public const int BotchMaxPermille = 25;

        /// <summary>How many botched jumps one synthetic time carries at most; it always carries at least one.</summary>
        public const int MaxBotchesPerSynthetic = 2;

        /// <summary>
        /// Drive the house field over <paramref name="course"/>. A drive that does not finish is left out along
        /// with its synthetics. If the sharp drive does not finish there is no par to measure botches against,
        /// and the whole field comes back empty.
        /// </summary>
        public static TimeTrialHouseFieldResult Drive(CourseDefinition course, TimeTrialRules rules)
        {
            List<TimeTrialGhost> ghosts        = new List<TimeTrialGhost>();
            List<int>            synthetics    = new List<int>();
            int                  parTimeMs     = 0;
            bool                 everyLevelRan = true;

            for (int driveIndex = 0; driveIndex < Drives.Count; driveIndex++)
            {
                (TimeTrialBotSkill skill, int variant) = Drives[driveIndex];

                TimeTrialGhost ghost = TimeTrialGhost.CreateSeed(course, rules, skill, variant);
                if (driveIndex == 0)
                {
                    if (ghost == null)
                        return new TimeTrialHouseFieldResult(new List<TimeTrialGhost>(), new List<int>(), everySeedLevelFinished: false);
                    parTimeMs = ghost.FinishTimeMs;
                }

                if (ghost == null)
                {
                    if (variant == 0)
                        everyLevelRan = false;
                    continue;
                }

                ghosts.Add(ghost);

                // A botched jump only ever costs time and never ends a run (see TimeTrialBotStyle.MissEveryNth),
                // so a synthetic is an honest time for the course, slower than the drive it hangs off.
                RandomPCG botches = CreateBotches(course, driveIndex);
                for (int ndx = 0; ndx < SyntheticsPerDrive; ndx++)
                    synthetics.Add(ghost.FinishTimeMs + BotchCostMs(botches, parTimeMs));
            }

            return new TimeTrialHouseFieldResult(ghosts, synthetics, everyLevelRan);
        }

        /// <summary>
        /// What one synthetic time adds to the run it hangs off: one to <see cref="MaxBotchesPerSynthetic"/>
        /// botched jumps, each <see cref="BotchMinPermille"/> to <see cref="BotchMaxPermille"/> of par. Never
        /// less than a millisecond, so a synthetic is always slower than its run.
        /// </summary>
        static int BotchCostMs(RandomPCG botches, int parTimeMs)
        {
            int count    = 1 + botches.NextInt(MaxBotchesPerSynthetic);
            int permille = 0;
            for (int ndx = 0; ndx < count; ndx++)
                permille += BotchMinPermille + botches.NextInt(BotchMaxPermille - BotchMinPermille + 1);

            int costMs = (int)((long)parTimeMs * permille / 1000);
            return costMs < 1 ? 1 : costMs;
        }

        /// <summary>
        /// The generator a drive's synthetic times are drawn from. Seeded from the course, the current
        /// <see cref="TimeTrialConstants.RulesetVersion"/> and the drive's index in <see cref="Drives"/>, so the same course gives the same
        /// house field wherever it is built and a new ruleset gives it a fresh one.
        /// </summary>
        static RandomPCG CreateBotches(CourseDefinition course, int driveIndex)
        {
            // The constants differ from the bot driver's jitter seed, so the botches do not follow the jitter of
            // the run they hang off.
            ulong seed = CourseIdHash.Of(course.Id);
            seed ^= unchecked((ulong)TimeTrialConstants.RulesetVersion * 0xD6E8FEB86659FD93UL);
            seed ^= (ulong)(driveIndex + 1) * 0xA0761D6478BD642FUL;
            return RandomPCG.CreateFromSeed(seed);
        }
    }

    /// <summary>
    /// A course's house field as driven: the real runs, kept as opponents, and the synthetic times hung off
    /// them, which are counted and nothing else. Both are empty when the course has no par.
    /// </summary>
    public sealed class TimeTrialHouseFieldResult
    {
        /// <summary>The scripted runs that finished, in <see cref="TimeTrialHouseField.Drives"/> order; the first is par.</summary>
        public List<TimeTrialGhost> Ghosts { get; }

        /// <summary>
        /// The synthetic finish times, in milliseconds: <see cref="TimeTrialHouseField.SyntheticsPerDrive"/> per
        /// entry of <see cref="Ghosts"/>, in the same order, each slower than the run it hangs off.
        /// </summary>
        public List<int> SyntheticTimesMs { get; }

        /// <summary>
        /// Whether every seed level's own run (variant 0 of each) finished. A course where one did not is not
        /// fit to be raced, whatever the further variants managed.
        /// </summary>
        public bool EverySeedLevelFinished { get; }

        /// <summary>The sharp drive's time, or 0 when it did not finish and the field is empty.</summary>
        public int ParTimeMs => Ghosts.Count > 0 ? Ghosts[0].FinishTimeMs : 0;

        /// <summary>How many times the field counts into a distribution: every real run and every synthetic.</summary>
        public int Count => Ghosts.Count + SyntheticTimesMs.Count;

        public TimeTrialHouseFieldResult(List<TimeTrialGhost> ghosts, List<int> syntheticTimesMs, bool everySeedLevelFinished)
        {
            Ghosts                 = ghosts;
            SyntheticTimesMs       = syntheticTimesMs;
            EverySeedLevelFinished = everySeedLevelFinished;
        }
    }
}
