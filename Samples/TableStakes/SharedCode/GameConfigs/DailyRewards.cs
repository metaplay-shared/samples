using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Schedule;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// Identifies one published version of the daily-reward cycle. Do not edit a published table. To retune,
    /// add a table with a new id and point <see cref="GlobalConfig.ActiveDailyRewardTable"/> at it, so a player
    /// part-way through a cycle keeps the rewards already shown to them.
    /// </summary>
    [MetaSerializable]
    public class DailyRewardTableId : StringId<DailyRewardTableId> { }

    /// <summary>
    /// Identifies one step of a published cycle, independently of its display position.
    /// <para>
    /// Claims are recorded and reported under this id. The display position (<see cref="DailyRewardStepInfo.Step"/>)
    /// can differ between tables, so analytics joins on this id, and the analytics payload carries both
    /// (<c>docs/daily-rewards.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class DailyRewardStepId : StringId<DailyRewardStepId> { }

    /// <summary>One step of the daily-reward cycle.</summary>
    [MetaSerializable]
    public class DailyRewardStepInfo
    {
        [MetaMember(1)] public DailyRewardStepId Id { get; private set; }

        /// <summary>The display position of this step in the cycle, 1 to <see cref="DailyRewardTableInfo.NumSteps"/>.</summary>
        [MetaMember(2)] public int Step { get; private set; }

        [MetaMember(3)] public RewardBundle Reward { get; private set; }

        public DailyRewardStepInfo() { }

        public DailyRewardStepInfo(DailyRewardStepId id, int step, RewardBundle reward)
        {
            Id     = id;
            Step   = step;
            Reward = reward;
        }

        public override string ToString() => $"{Id} (step {Step}): {Reward}";
    }

    /// <summary>
    /// One version of the daily-reward cycle (<c>docs/daily-rewards.md</c>). Step 1 is the first claim of a
    /// cycle. After step <see cref="NumSteps"/>, the cycle starts again from step 1.
    /// </summary>
    [MetaSerializable]
    public class DailyRewardTableInfo : IGameConfigData<DailyRewardTableId>, IValidatedConfigItem
    {
        /// <summary>The number of steps in a cycle. A game rule, not configurable.</summary>
        public const int NumSteps = 7;

        [MetaMember(1)] public DailyRewardTableId Id      { get; private set; }
        [MetaMember(3)] List<DailyRewardStepInfo> _steps;

        // MetaMember id 2 is retired. Do not reuse it.

        /// <summary>The steps of the cycle, in cycle order.</summary>
        public IReadOnlyList<DailyRewardStepInfo> Steps => _steps;

        public DailyRewardTableId ConfigKey => Id;

        public DailyRewardTableInfo() { }

        /// <summary>
        /// Built from <c>GameConfigSource/DailyRewards.csv</c>, one row per step. Only a table's first row has
        /// the id. The <c>Steps[]</c> columns of the following rows with an empty id add steps to that table.
        /// </summary>
        [MetaGameConfigBuildConstructor]
        public DailyRewardTableInfo(DailyRewardTableId id, List<DailyRewardStepInfo> steps)
        {
            Id     = id;
            _steps = steps.ToList();
        }

        /// <summary>
        /// The step at display position <paramref name="step"/>, or null if the table has no such step.
        /// <para>
        /// The config build requires every position from 1 to <see cref="NumSteps"/>, so null means the caller
        /// asked for a position outside that range. The caller must refuse the claim rather than pay a guessed
        /// reward.
        /// </para>
        /// </summary>
        public DailyRewardStepInfo StepAt(int step)
        {
            if (_steps == null)
                return null;

            foreach (DailyRewardStepInfo info in _steps)
            {
                if (info != null && info.Step == step)
                    return info;
            }
            return null;
        }

        /// <summary>The display position after <paramref name="step"/>, wrapping after the last one.</summary>
        public static int NextStepAfter(int step) => step >= NumSteps || step < 1 ? 1 : step + 1;

        public void Validate(ConfigItemValidation validation)
        {
            validation.RequireCount(_steps, NumSteps, nameof(Steps));
            if (_steps == null)
                return;

            HashSet<DailyRewardStepId> ids       = new HashSet<DailyRewardStepId>();
            HashSet<int>               positions = new HashSet<int>();

            int previousCoins = 0;
            for (int index = 0; index < _steps.Count; index++)
            {
                string              hint = $"{nameof(Steps)}[{index}]";
                DailyRewardStepInfo step = _steps[index];

                if (step == null)
                {
                    validation.Error("is missing", hint);
                    continue;
                }

                // Claims are recorded by step id, so a duplicate id would make two steps indistinguishable in
                // the claim records. Each position from 1 to NumSteps must appear exactly once, in order. A gap or
                // duplicate would leave the claim after some step with no next step.
                validation.RequireNumberedRow(step.Id, step.Step, index, NumSteps, ids, positions, "step", "the table is authored in cycle order", hint);

                if (step.Reward == null)
                {
                    validation.Error("grants nothing", hint);
                    continue;
                }

                step.Reward.Validate(validation, hint);

                // Every step grants coins. The claim button label shows the coin amount.
                int coins = step.Reward.AmountOf(CurrencyType.Coins);
                validation.RequirePositive(coins, hint);

                // The player sees the whole cycle in advance, and the coin rewards must not decrease from one
                // step to the next.
                validation.Require(coins >= previousCoins, $"grants {coins} coins after a step that granted {previousCoins}; the cycle must not shrink", hint);
                previousCoins = coins;

                // Daily rewards grant coins and the last step's spin token only. The economy design gives this
                // feature no gems.
                validation.Require(!step.Reward.Grants(CurrencyType.Gems), "grants gems; daily rewards pay coins and the final spin token only", hint);

                // The last step grants exactly one spin token and no other step grants any, so the token is the
                // reward for completing the cycle.
                int tokens = step.Reward.AmountOf(CurrencyType.SpinTokens);
                if (step.Step == NumSteps)
                    validation.Require(tokens == 1, $"grants {tokens} spin tokens; the last step grants exactly one", hint);
                else
                    validation.Require(tokens == 0, $"grants {tokens} spin tokens; only the last step grants one", hint);
            }
        }

        public override string ToString() => Id?.Value ?? "(no daily reward table)";
    }

    /// <summary>
    /// Validation rules for the daily-reset schedule, which defines the player-local day boundary.
    /// <para>
    /// Daily rewards treat each activation of the schedule as one day and count missed days from the gaps
    /// between activations, which is only correct for a schedule that recurs daily from local midnight.
    /// Missions compute their day from <c>PlayerCalendar</c>, and these rules keep the schedule on the same
    /// boundary. <c>MissionTests</c> checks that the two agree (<c>docs/daily-rewards.md</c>).
    /// </para>
    /// </summary>
    public static class DailyResetScheduleRules
    {
        /// <summary>A calendar period of one day. The schedule's recurrence and duration must both equal it.</summary>
        public static MetaCalendarPeriod OneDay => new MetaCalendarPeriod(0, 0, 1, 0, 0, 0);

        /// <summary>Whether <paramref name="period"/> is exactly one day, with every other field zero.</summary>
        public static bool IsOneDay(MetaCalendarPeriod period) =>
            period.Days == 1 && period.Years == 0 && period.Months == 0 && period.Hours == 0 && period.Minutes == 0 && period.Seconds == 0;

        /// <summary>
        /// Validates a daily-reset schedule. The daily-reward streak calculation depends on every rule checked
        /// here.
        /// </summary>
        public static void Validate(ConfigItemValidation validation, MetaRecurringCalendarSchedule schedule, string memberHint)
        {
            if (schedule == null)
            {
                validation.Error("is missing", memberHint);
                return;
            }

            // The reset must be at the player's local midnight, not at a UTC time.
            validation.Require(schedule.TimeMode == MetaScheduleTimeMode.Local,
                $"is a {schedule.TimeMode} schedule; the daily reset is player-local", memberHint);

            validation.Require(schedule.Recurrence.HasValue && IsOneDay(schedule.Recurrence.Value),
                "must recur exactly once a day", memberHint);

            // With duration equal to recurrence, every moment falls inside exactly one activation, so there is
            // no gap with no current day.
            validation.Require(IsOneDay(schedule.Duration),
                "must last exactly one day, so every moment falls inside one activation", memberHint);

            // A repeat limit would stop daily rewards after the last repeat.
            validation.Require(!schedule.NumRepeats.HasValue, "must not have a repeat limit; the daily reset is permanent", memberHint);

            // An activation is identified by its local calendar date. A start time other than midnight would
            // split one date across two activations.
            validation.Require(schedule.Start.Hour == 0 && schedule.Start.Minute == 0 && schedule.Start.Second == 0,
                $"starts at {schedule.Start.Hour:00}:{schedule.Start.Minute:00}:{schedule.Start.Second:00}; the daily reset starts at local midnight", memberHint);

            validation.Require(schedule.Start.Year > 0, "has no start date", memberHint);
        }
    }
}
