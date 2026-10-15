using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Server.CourseGhosts
{
    /// <summary>
    /// One opponent kept for a course: who ran it, what they ran, and the inputs to reproduce it.
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(4, 10)]
    public class CourseGhostEntry
    {
        /// <summary>The player who ran it, or <see cref="EntityId.None"/> for a scripted runner.</summary>
        [MetaMember(1)] public EntityId PlayerId { get; private set; }

        [MetaMember(2)] public string             DisplayName  { get; private set; }
        [MetaMember(3)] public int                FinishTimeMs { get; private set; }
        [MetaMember(5)] public TimeTrialRecording Recording    { get; private set; }

        /// <summary>Whether this is a scripted runner, which a validated player run may displace.</summary>
        [MetaMember(6)] public bool IsSeed { get; private set; }

        /// <summary>
        /// When this run crossed each of the course's gates, in milliseconds. It comes from the same replay
        /// that produced <see cref="FinishTimeMs"/>, so a segment can be placed against it without replaying
        /// the recording again.
        /// </summary>
        [MetaMember(7)] public List<int> GateTimesMs { get; private set; } = new List<int>();

        /// <summary>
        /// The Skill the run was made with, or null for none. Kept with the recording because the recording is
        /// only the inputs: replaying it under any other Skill would produce a different run from the one the
        /// server accepted.
        /// </summary>
        [MetaMember(8)] public SkillId SkillId { get; private set; }

        /// <summary>
        /// When this run reached each climb's bottom torii, in milliseconds, from the same replay. A segment
        /// ends at a bottom torii and the next starts at the top one, so placing one takes both lists.
        /// </summary>
        [MetaMember(9)] public List<int> ClimbEntryTimesMs { get; private set; } = new List<int>();

        public CourseGhostEntry() { }

        public CourseGhostEntry(EntityId playerId, string displayName, int finishTimeMs, TimeTrialRecording recording, bool isSeed, List<int> gateTimesMs, List<int> climbEntryTimesMs, SkillId skillId = null)
        {
            PlayerId          = playerId;
            DisplayName       = displayName;
            FinishTimeMs      = finishTimeMs;
            Recording         = recording;
            IsSeed            = isSeed;
            GateTimesMs       = gateTimesMs ?? new List<int>();
            ClimbEntryTimesMs = climbEntryTimesMs ?? new List<int>();
            SkillId           = skillId;
        }

        /// <summary>
        /// This entry as an opponent to hand out, carrying the Skill it was run with so whoever replays it
        /// replays it under the movement values it actually ran on.
        /// </summary>
        public TimeTrialGhost ToGhost(CourseId courseId, int rulesetVersion)
            => new TimeTrialGhost(courseId, rulesetVersion, Recording, DisplayName, FinishTimeMs, IsSeed ? TimeTrialGhostKind.Seed : TimeTrialGhostKind.Player, new List<int>(GateTimesMs), SkillId, new List<int>(ClimbEntryTimesMs));
    }

    /// <summary>
    /// The opponents kept for one stretch of pace on a course. A band holds a bounded sample rather than
    /// everything that was run at that pace: what the lineup needs is a few runs at each pace, not every run.
    /// </summary>
    [MetaSerializable]
    public class CourseGhostBand
    {
        [MetaMember(1)] public List<CourseGhostEntry> Entries { get; private set; } = new List<CourseGhostEntry>();

        /// <summary>Which slot the next arrival overwrites once the band is full, so the sample keeps turning over.</summary>
        [MetaMember(2)] public int NextSlot { get; private set; }

        public void Insert(CourseGhostEntry entry, int capacity)
        {
            if (Entries.Count < capacity)
            {
                Entries.Add(entry);
                return;
            }

            // A validated run takes a scripted runner's slot before it takes another player's.
            int seedSlot = Entries.FindIndex(existing => existing.IsSeed);
            if (!entry.IsSeed && seedSlot >= 0)
            {
                Entries[seedSlot] = entry;
                return;
            }

            int slot = ((NextSlot % capacity) + capacity) % capacity;
            Entries[slot] = entry;
            NextSlot      = slot + 1;
        }

        public bool RemovePlayer(EntityId playerId)
        {
            int index = Entries.FindIndex(entry => entry.PlayerId == playerId);
            if (index < 0)
                return false;

            Entries.RemoveAt(index);
            return true;
        }
    }

    /// <summary>
    /// Everything a course keeps about how it has been run: a bounded set of opponents at each pace relative
    /// to par, and the spread of times on it. This is the state <see cref="CourseGhostPoolActor"/> owns and persists.
    /// </summary>
    [MetaSerializable]
    [SupportedSchemaVersions(1, 1)]
    public class CourseGhostPoolState : ISchemaMigratable
    {
        /// <summary>
        /// Exclusive upper edge of each band, as a permille of par. The first band also holds every time faster
        /// than par, which a player with a Skill equipped can set. The last band catches everything slower. The
        /// edges sit close together because the whole spread between the scripted runners is small.
        /// </summary>
        static readonly int[] BandUpperPermille = { 1000, 1015, 1035, 1060, 1100, int.MaxValue };

        public const int BandCount    = 6;
        public const int SlotsPerBand = 6;

        /// <summary>
        /// The pace a player with no time on this course is assumed to run at, as a permille of par. Set just
        /// off the cautious scripted runner, so a first attempt is paced against something it can chase.
        /// </summary>
        public const int AssumedFirstRunPermille = 1100;

        /// <summary>
        /// The ladder a lineup is drawn along, in permille of the player's own pace on this course, filled
        /// from the front: one a shade behind the player, one just ahead, and one clearly ahead. Three of them
        /// make a field of four, so a run at the player's own pace comes third and a better one climbs.
        /// </summary>
        static readonly int[] LineupPacePermille = { 1015, 985, 950 };

        [MetaMember(1)] public CourseId CourseId       { get; private set; }
        [MetaMember(2)] public int      RulesetVersion { get; private set; }

        /// <summary>The sharpest scripted runner's time, in milliseconds. Zero until the seeds have been driven.</summary>
        [MetaMember(3)] public int ParTimeMs { get; private set; }

        [MetaMember(4)] public List<CourseGhostBand> Bands { get; private set; } = new List<CourseGhostBand>();

        [MetaMember(5)] public CourseTimeDistribution Distribution { get; private set; } = new CourseTimeDistribution();

        /// <summary>When a run was last accepted into this pool, for operators looking at a quiet course.</summary>
        [MetaMember(6)] public MetaTime LastAcceptedAt { get; private set; }

        public CourseGhostPoolState() { }

        /// <summary>Whether the pool has a par time, and so can band a result at all.</summary>
        public bool IsSeeded => ParTimeMs > 0;

        /// <summary>
        /// Resets the pool and seeds it from the house field (<see cref="TimeTrialHouseField"/>) driven over
        /// <paramref name="course"/>: its par, its first opponents and its first best times. Returns false if any
        /// seed level's own run fails to finish, in which case the pool keeps no par and accepts no results.
        /// </summary>
        public bool Seed(CourseDefinition course, TimeTrialRules rules)
        {
            CourseId       = course.Id;
            RulesetVersion = TimeTrialConstants.RulesetVersion;
            ParTimeMs      = 0;
            Distribution   = new CourseTimeDistribution();
            Bands          = new List<CourseGhostBand>();
            for (int ndx = 0; ndx < BandCount; ndx++)
                Bands.Add(new CourseGhostBand());

            TimeTrialHouseFieldResult field = TimeTrialHouseField.Drive(course, rules);
            if (field.Ghosts.Count == 0 || !field.EverySeedLevelFinished)
                return false;

            ParTimeMs = field.ParTimeMs;

            foreach (TimeTrialGhost seed in field.Ghosts)
                InsertEntry(SeedEntryOf(seed));

            CountHouseField(field);
            return true;
        }

        static CourseGhostEntry SeedEntryOf(TimeTrialGhost seed)
            => new CourseGhostEntry(
                EntityId.None,
                seed.DisplayName,
                seed.FinishTimeMs,
                seed.Recording,
                isSeed: true,
                gateTimesMs: new List<int>(seed.GateTimesMs),
                climbEntryTimesMs: new List<int>(seed.ClimbEntryTimesMs));

        /// <summary>Count every scripted run and every synthetic time of <paramref name="field"/> as a best time against this pool's par.</summary>
        void CountHouseField(TimeTrialHouseFieldResult field)
        {
            foreach (TimeTrialGhost seed in field.Ghosts)
                Distribution.AddHouse(seed.FinishTimeMs, ParTimeMs);
            foreach (int timeMs in field.SyntheticTimesMs)
                Distribution.AddHouse(timeMs, ParTimeMs);
        }

        /// <summary>
        /// Which band a time belongs to, fastest first, or -1 when the pool is not seeded or the time is not
        /// positive.
        /// </summary>
        public int BandFor(int timeMs)
        {
            if (!IsSeeded || timeMs <= 0)
                return -1;

            int permille = (int)((long)timeMs * 1000 / ParTimeMs);
            for (int ndx = 0; ndx < BandCount; ndx++)
            {
                if (permille < BandUpperPermille[ndx])
                    return ndx;
            }
            return BandCount - 1;
        }

        /// <summary>
        /// Takes a validated run into the pool: counts it and, when it is the player's new best, keeps it as
        /// that player's opponent. <paramref name="previousBestMs"/> is the player's best on this course before
        /// this run, or zero if they had none. An unseeded pool ignores the run.
        /// </summary>
        public void AcceptRun(EntityId playerId, string displayName, int finishTimeMs, bool isNewBest, int previousBestMs, TimeTrialRecording recording, List<int> gateTimesMs, List<int> climbEntryTimesMs, MetaTime now, SkillId skillId = null)
        {
            if (!IsSeeded || finishTimeMs <= 0)
                return;

            Distribution.AddRun(finishTimeMs, ParTimeMs);
            LastAcceptedAt = now;

            if (!isNewBest)
                return;

            Distribution.MovePlayerBest(previousBestMs, finishTimeMs, ParTimeMs);

            if (recording == null || !TimeTrialRecording.IsWellFormed(recording))
                return;

            // One player holds at most one slot, their best run.
            foreach (CourseGhostBand band in Bands)
                band.RemovePlayer(playerId);

            InsertEntry(new CourseGhostEntry(playerId, displayName, finishTimeMs, recording, isSeed: false, gateTimesMs: gateTimesMs, climbEntryTimesMs: climbEntryTimesMs, skillId: skillId));
        }

        /// <summary>
        /// The opponents to put in front of a player opening a run, fastest first: at most
        /// <paramref name="maxGhosts"/> laddered around the player's best on this course
        /// (<see cref="AssumedFirstRunPermille"/> of par when they have none), never including the requester's
        /// own recording. Empty when the pool is not seeded.
        /// </summary>
        public List<TimeTrialGhost> SelectLineup(EntityId requester, int playerBestMs, int maxGhosts, RandomPCG rng)
        {
            List<TimeTrialGhost> lineup = new List<TimeTrialGhost>();
            if (!IsSeeded || maxGhosts <= 0)
                return lineup;

            // Paced from the best and never the last run, so running slowly on purpose cannot lower the ladder.
            int pace = playerBestMs > 0 ? playerBestMs : (int)((long)ParTimeMs * AssumedFirstRunPermille / 1000);

            List<CourseGhostEntry> taken = new List<CourseGhostEntry>();

            foreach (int step in LineupPacePermille)
            {
                if (lineup.Count >= maxGhosts)
                    break;

                CourseGhostEntry entry = PickNearest((int)((long)pace * step / 1000), requester, taken, rng);
                if (entry == null)
                    break;

                taken.Add(entry);
                lineup.Add(entry.ToGhost(CourseId, RulesetVersion));
            }

            // Fastest first, so a client that can only draw some of them draws the ones worth chasing.
            lineup.Sort((a, b) => a.FinishTimeMs.CompareTo(b.FinishTimeMs));

            return lineup;
        }

        /// <summary>
        /// A time with a copy of how the bests of the players who have run this course are spread. The copy is
        /// the standing's own, so the pool keeps counting without changing a standing it has already handed
        /// out. An unseeded pool has no par to bucket against and gives no counts.
        /// </summary>
        public TimeTrialStanding StandingFor(int finishTimeMs)
            => new TimeTrialStanding(
                CourseId,
                finishTimeMs,
                ParTimeMs,
                IsSeeded ? (int[])Distribution.PlayerBestCounts.Clone() : null);

        /// <summary>How many opponents the pool is holding, across every band.</summary>
        public int EntryCount
        {
            get
            {
                int count = 0;
                foreach (CourseGhostBand band in Bands)
                    count += band.Entries.Count;
                return count;
            }
        }

        void InsertEntry(CourseGhostEntry entry)
        {
            int band = BandFor(entry.FinishTimeMs);
            if (band < 0)
                return;

            Bands[band].Insert(entry, SlotsPerBand);
        }

        /// <summary>How many of the nearest opponents a slot is drawn from, so a course does not always field the same runner.</summary>
        const int NearestConsidered = 3;

        /// <summary>
        /// A kept opponent whose time sits close to <paramref name="targetMs"/>, drawn from the nearest few so
        /// a player running the same course again does not meet the same opponent every time. The requester's
        /// own entry is never returned to them.
        /// </summary>
        CourseGhostEntry PickNearest(int targetMs, EntityId requester, List<CourseGhostEntry> taken, RandomPCG rng)
        {
            List<CourseGhostEntry> candidates = new List<CourseGhostEntry>();

            foreach (CourseGhostBand band in Bands)
            {
                foreach (CourseGhostEntry entry in band.Entries)
                {
                    if (requester != EntityId.None && entry.PlayerId == requester)
                        continue;
                    if (taken.Contains(entry))
                        continue;

                    candidates.Add(entry);
                }
            }

            if (candidates.Count == 0)
                return null;

            candidates.Sort((a, b) =>
            {
                int byDistance = Distance(a.FinishTimeMs, targetMs).CompareTo(Distance(b.FinishTimeMs, targetMs));
                if (byDistance != 0)
                    return byDistance;

                int byTime = a.FinishTimeMs.CompareTo(b.FinishTimeMs);
                return byTime != 0 ? byTime : a.PlayerId.CompareTo(b.PlayerId);
            });

            int considered = candidates.Count < NearestConsidered ? candidates.Count : NearestConsidered;
            return rng == null ? candidates[0] : candidates[rng.NextInt(considered)];
        }

        static int Distance(int timeMs, int targetMs) => timeMs > targetMs ? timeMs - targetMs : targetMs - timeMs;
    }
}
