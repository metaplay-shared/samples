using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// How often a mission set resets (<c>docs/missions.md</c>). Cadences are an enum rather than config
    /// because their reset boundaries must match the daily reward's day boundary.
    /// </summary>
    [MetaSerializable]
    public enum MissionCadence
    {
        None = 0,
        Daily = 1,
        Weekly = 2,
    }

    /// <summary>
    /// What a mission counts. Every objective is counted from match completions. Objectives are an enum
    /// rather than config because the game code must implement the counting for each one.
    /// </summary>
    [MetaSerializable]
    public enum MissionObjective
    {
        None = 0,
        MatchesCompleted = 1,
        MatchesWon = 2,
    }

    [MetaSerializable]
    public class MissionId : StringId<MissionId> { }

    /// <summary>One mission: a counted objective, a target and the reward for reaching it.</summary>
    [MetaSerializable]
    public class MissionInfo : IGameConfigData<MissionId>, IValidatedConfigItem
    {
        [MetaMember(1)] public MissionId        Id          { get; private set; }
        [MetaMember(2)] public MissionCadence   Cadence     { get; private set; }
        [MetaMember(3)] public MissionObjective Objective   { get; private set; }
        [MetaMember(4)] public int              TargetCount { get; private set; }
        [MetaMember(5)] public RewardBundle     Reward      { get; private set; }

        public MissionId ConfigKey => Id;

        public MissionInfo() { }

        public MissionInfo(MissionId id, MissionCadence cadence, MissionObjective objective, int targetCount, RewardBundle reward)
        {
            Id          = id;
            Cadence     = cadence;
            Objective   = objective;
            TargetCount = targetCount;
            Reward      = reward;
        }

        public void Validate(ConfigItemValidation validation)
        {
            validation.Require(Cadence != MissionCadence.None, "has no cadence", nameof(Cadence));
            validation.Require(Objective != MissionObjective.None, "has no objective", nameof(Objective));
            validation.RequirePositive(TargetCount, nameof(TargetCount));

            // The target must fall in the allowed range for its cadence and objective, so it is neither trivial
            // nor too long for a short session.
            ValidateTargetBand(validation);

            if (Reward == null)
            {
                validation.Error("has no reward", nameof(Reward));
                return;
            }
            Reward.Validate(validation, nameof(Reward));

            // Per the economy design, daily missions pay coins, weekly missions pay spin tokens, and no mission
            // pays gems.
            validation.Require(!Reward.Grants(CurrencyType.Gems), "grants gems; missions pay coins or spin tokens", nameof(Reward));
            if (Cadence == MissionCadence.Daily)
                validation.Require(Reward.Grants(CurrencyType.Coins), "a daily mission must pay coins", nameof(Reward));
            if (Cadence == MissionCadence.Weekly)
                validation.Require(Reward.Grants(CurrencyType.SpinTokens), "a weekly mission must pay spin tokens", nameof(Reward));
        }

        void ValidateTargetBand(ConfigItemValidation validation)
        {
            if (Cadence == MissionCadence.None || Objective == MissionObjective.None || TargetCount <= 0)
                return;

            int low;
            int high;
            if (Cadence == MissionCadence.Daily)
            {
                low  = 1;
                high = Objective == MissionObjective.MatchesWon ? 2 : 4;
            }
            else
            {
                low  = Objective == MissionObjective.MatchesWon ? 2 : 5;
                high = Objective == MissionObjective.MatchesWon ? 5 : 20;
            }

            validation.Require(
                TargetCount >= low && TargetCount <= high,
                $"asks for {TargetCount}, which is outside the {low}-{high} a {Cadence} {Objective} mission may ask for",
                nameof(TargetCount));
        }

        public override string ToString() => Id?.Value ?? "(no mission)";
    }

    /// <summary>
    /// Identifies one published version of a mission set. To retune a set, add a set with a new id instead of
    /// editing the one players are part-way through.
    /// </summary>
    [MetaSerializable]
    public class MissionSetId : StringId<MissionSetId> { }

    /// <summary>
    /// The missions that are active together for one cadence: <see cref="NumDailyMissions"/> daily or
    /// <see cref="NumWeeklyMissions"/> weekly. The members are <see cref="MetaRef{TItem}"/>s, so a set naming a
    /// missing mission fails the config build.
    /// </summary>
    [MetaSerializable]
    public class MissionSetInfo : IGameConfigData<MissionSetId>, IValidatedConfigItem
    {
        public const int NumDailyMissions  = 3;
        public const int NumWeeklyMissions = 2;

        /// <summary>
        /// The allowed total reward of a completed set, from the economy design: a coin range for the daily set
        /// and a spin-token maximum for the weekly set. <see cref="GameConfigValidation"/> checks the totals
        /// across the whole set.
        /// </summary>
        public const int MinDailySetCoins   = 300;
        public const int MaxDailySetCoins   = 700;
        public const int MaxWeeklySetTokens = 2;

        [MetaMember(1)] public MissionSetId    Id      { get; private set; }
        [MetaMember(2)] public MissionCadence  Cadence { get; private set; }
        [MetaMember(3)] List<MetaRef<MissionInfo>> _missions;

        /// <summary>The missions in the set.</summary>
        public IReadOnlyList<MetaRef<MissionInfo>> Missions => _missions;

        public MissionSetId ConfigKey => Id;

        public MissionSetInfo() { }

        public MissionSetInfo(MissionSetId id, MissionCadence cadence, IEnumerable<MissionId> missions)
        {
            Id        = id;
            Cadence   = cadence;
            _missions = missions.Select(missionId => MetaRef<MissionInfo>.FromKey(missionId)).ToList();
        }

        /// <summary>
        /// Built from <c>GameConfigSource/MissionSets.csv</c>, one row per member mission. The sheet gives each
        /// mission's id, and the build resolves it against the Missions library.
        /// </summary>
        [MetaGameConfigBuildConstructor]
        public MissionSetInfo(MissionSetId id, MissionCadence cadence, List<MetaRef<MissionInfo>> missions)
        {
            Id        = id;
            Cadence   = cadence;
            _missions = missions;
        }

        public void Validate(ConfigItemValidation validation)
        {
            validation.Require(Cadence != MissionCadence.None, "has no cadence", nameof(Cadence));

            int expected = Cadence == MissionCadence.Weekly ? NumWeeklyMissions : NumDailyMissions;
            validation.RequireCount(_missions, expected, nameof(Missions));
            if (_missions == null)
                return;

            // GameConfigValidation checks that each mission exists and has the set's cadence, because that needs
            // the Missions library. This method checks the count and duplicates.
            HashSet<object> seen = new HashSet<object>();
            foreach (MetaRef<MissionInfo> reference in _missions)
            {
                if (reference == null)
                {
                    validation.Error("holds an empty slot", nameof(Missions));
                    continue;
                }

                if (!seen.Add(reference.KeyObject))
                    validation.Error($"names mission '{reference.KeyObject}' twice", nameof(Missions));
            }
        }

        public override string ToString() => Id?.Value ?? "(no mission set)";
    }
}
