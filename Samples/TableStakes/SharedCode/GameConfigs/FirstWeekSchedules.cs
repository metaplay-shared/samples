using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// Identifies one published version of the first-week schedule. A player keeps the schedule they started on
    /// for the whole week. To retune, add a schedule with a new id for future players instead of editing a
    /// published one.
    /// </summary>
    [MetaSerializable]
    public class FirstWeekScheduleId : StringId<FirstWeekScheduleId> { }

    /// <summary>
    /// Identifies one day of a published schedule, independently of its display position.
    /// <para>
    /// Claims name this id, progress is recorded under it, and analytics and economy events carry it. The
    /// payloads also carry the display position (<see cref="FirstWeekDayInfo.Day"/>), but analytics joins on
    /// this id (<c>docs/first-week-event.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class FirstWeekDayId : StringId<FirstWeekDayId> { }

    /// <summary>One day of the first-week event: a match-completion goal and the reward for meeting it.</summary>
    [MetaSerializable]
    public class FirstWeekDayInfo
    {
        [MetaMember(1)] public int          MatchesRequired { get; private set; }
        [MetaMember(2)] public RewardBundle Reward          { get; private set; }

        /// <summary>The id a claim names. Do not reuse an id for a different day in a later schedule.</summary>
        [MetaMember(3)] public FirstWeekDayId Id { get; private set; }

        /// <summary>The display position of this day, 1 to <see cref="FirstWeekScheduleInfo.NumDays"/>.</summary>
        [MetaMember(4)] public int Day { get; private set; }

        public FirstWeekDayInfo() { }

        public FirstWeekDayInfo(FirstWeekDayId id, int day, int matchesRequired, RewardBundle reward)
        {
            Id              = id;
            Day             = day;
            MatchesRequired = matchesRequired;
            Reward          = reward;
        }

        public override string ToString() => $"{Id} (day {Day}): {MatchesRequired} match(es) for {Reward}";
    }

    /// <summary>
    /// One version of the first-week event (<c>docs/first-week-event.md</c>). Each day is
    /// <see cref="DayLength"/> long, counted from the player's first session.
    /// </summary>
    [MetaSerializable]
    public class FirstWeekScheduleInfo : IGameConfigData<FirstWeekScheduleId>, IValidatedConfigItem
    {
        /// <summary>The number of days in the event. A game rule, not configurable.</summary>
        public const int NumDays = 7;

        /// <summary>
        /// The length of one event day, as elapsed time from the player's first session rather than a calendar
        /// day. A calendar day would give a player who starts just before midnight a first day of a few minutes.
        /// </summary>
        public static readonly MetaDuration DayLength = MetaDuration.FromHours(24);

        /// <summary>The length of the whole event, as elapsed time from the player's first session.</summary>
        public static MetaDuration EventLength => DayLength * NumDays;

        /// <summary>
        /// The allowed range of <see cref="FirstWeekDayInfo.MatchesRequired"/>. The upper bound keeps each day's
        /// goal completable in one evening session.
        /// </summary>
        public const int MinMatchesRequired = 1;
        public const int MaxMatchesRequired = 3;

        /// <summary>
        /// The allowed coin range for every day except the last, followed by the allowed ranges for the last
        /// day's reward. The economy design sets these ranges, and the config build enforces them.
        /// </summary>
        public const int MinDailyCoins = 250;
        public const int MaxDailyCoins = 500;

        public const int MinFinalCoins      = 1_000;
        public const int MaxFinalCoins      = 2_000;
        public const int MinFinalGems       = 50;
        public const int MaxFinalGems       = 100;
        public const int MinFinalSpinTokens = 1;
        public const int MaxFinalSpinTokens = 2;

        /// <summary>The allowed range for the total coins of all days combined.</summary>
        public const int MinTotalCoins = 3_250;
        public const int MaxTotalCoins = 5_000;

        [MetaMember(1)] public FirstWeekScheduleId Id { get; private set; }
        [MetaMember(2)] List<FirstWeekDayInfo>     _days;

        /// <summary>The days of the event, in day order. Empty when the schedule has no days.</summary>
        public IReadOnlyList<FirstWeekDayInfo> Days => _days ?? NoDays;

        static readonly List<FirstWeekDayInfo> NoDays = new List<FirstWeekDayInfo>();

        public FirstWeekScheduleId ConfigKey => Id;

        public FirstWeekScheduleInfo() { }

        /// <summary>Built from <c>GameConfigSource/FirstWeekSchedules.csv</c>, one row per day of the schedule.</summary>
        [MetaGameConfigBuildConstructor]
        public FirstWeekScheduleInfo(FirstWeekScheduleId id, List<FirstWeekDayInfo> days)
        {
            Id    = id;
            _days = days.ToList();
        }

        /// <summary>
        /// The day at display position <paramref name="day"/>, or null if the schedule has no such day.
        /// <para>
        /// The config build requires every position from 1 to <see cref="NumDays"/>, so null means the caller
        /// asked for a position outside that range. The caller must refuse the request rather than guess.
        /// </para>
        /// </summary>
        public FirstWeekDayInfo DayAt(int day)
        {
            foreach (FirstWeekDayInfo info in Days)
            {
                if (info != null && info.Day == day)
                    return info;
            }
            return null;
        }

        /// <summary>The day with id <paramref name="id"/>, or null if the schedule has none.</summary>
        public FirstWeekDayInfo Find(FirstWeekDayId id)
        {
            if (id == null)
                return null;

            foreach (FirstWeekDayInfo info in Days)
            {
                if (info != null && info.Id == id)
                    return info;
            }
            return null;
        }

        public void Validate(ConfigItemValidation validation)
        {
            validation.RequireCount(_days, NumDays, nameof(Days));
            if (_days == null)
                return;

            HashSet<FirstWeekDayId> ids        = new HashSet<FirstWeekDayId>();
            HashSet<int>            positions  = new HashSet<int>();
            int                     totalCoins = 0;

            for (int index = 0; index < _days.Count; index++)
            {
                string           hint = $"{nameof(Days)}[{index}]";
                FirstWeekDayInfo day  = _days[index];

                if (day == null)
                {
                    validation.Error("is missing", hint);
                    continue;
                }

                // Claims and progress are keyed by day id, so a duplicate id would make two days
                // indistinguishable. Each position from 1 to NumDays must appear exactly once, in order. A gap or
                // duplicate would leave an event day with no goal, or with two.
                validation.RequireNumberedRow(day.Id, day.Day, index, NumDays, ids, positions, "day", "the schedule is authored in day order", hint);

                validation.Require(
                    day.MatchesRequired >= MinMatchesRequired && day.MatchesRequired <= MaxMatchesRequired,
                    $"asks for {day.MatchesRequired} matches, outside the {MinMatchesRequired}-{MaxMatchesRequired} a first-week day may ask for",
                    hint);

                if (day.Reward == null)
                {
                    validation.Error("has no reward", hint);
                    continue;
                }

                day.Reward.Validate(validation, hint);
                totalCoins += day.Reward.AmountOf(CurrencyType.Coins);

                if (day.Day == NumDays)
                    ValidateCapstone(validation, day, hint);
                else
                    ValidateOrdinaryDay(validation, day, hint);
            }

            // The last day asks for one match, because its purpose is to reward the player for returning.
            FirstWeekDayInfo last = DayAt(NumDays);
            if (last != null)
                validation.Require(last.MatchesRequired == 1, $"the last day must ask for one match, asks for {last.MatchesRequired}", $"{nameof(Days)}[{NumDays - 1}]");

            // The total coins of all days must fall in the range the economy design is balanced against. Only
            // a schedule with every day present is checked.
            if (_days.Count == NumDays)
            {
                validation.Require(
                    totalCoins >= MinTotalCoins && totalCoins <= MaxTotalCoins,
                    $"pays {totalCoins} coins for a perfect run, outside the {MinTotalCoins}-{MaxTotalCoins} the economy is balanced against",
                    nameof(Days));
            }
        }

        /// <summary>
        /// Validates a day other than the last: coins within the allowed range and nothing else. Gems and spin
        /// tokens are reserved for the last day, so the last day stays the most valuable.
        /// </summary>
        static void ValidateOrdinaryDay(ConfigItemValidation validation, FirstWeekDayInfo day, string hint)
        {
            int coins = day.Reward.AmountOf(CurrencyType.Coins);
            validation.Require(
                coins >= MinDailyCoins && coins <= MaxDailyCoins,
                $"pays {coins} coins, outside the {MinDailyCoins}-{MaxDailyCoins} band days 1-{NumDays - 1} keep",
                hint);

            validation.Require(!day.Reward.Grants(CurrencyType.Gems), "grants gems; only the last day pays premium currency", hint);
            validation.Require(!day.Reward.Grants(CurrencyType.SpinTokens), "grants spin tokens; only the last day pays them", hint);
        }

        /// <summary>Validates the last day's reward: coins, gems and spin tokens, each within its allowed range.</summary>
        static void ValidateCapstone(ConfigItemValidation validation, FirstWeekDayInfo day, string hint)
        {
            int coins  = day.Reward.AmountOf(CurrencyType.Coins);
            int gems   = day.Reward.AmountOf(CurrencyType.Gems);
            int tokens = day.Reward.AmountOf(CurrencyType.SpinTokens);

            validation.Require(coins >= MinFinalCoins && coins <= MaxFinalCoins,
                $"pays {coins} coins, outside the {MinFinalCoins}-{MaxFinalCoins} the capstone keeps", hint);
            validation.Require(gems >= MinFinalGems && gems <= MaxFinalGems,
                $"pays {gems} gems, outside the {MinFinalGems}-{MaxFinalGems} the capstone keeps", hint);
            validation.Require(tokens >= MinFinalSpinTokens && tokens <= MaxFinalSpinTokens,
                $"pays {tokens} spin tokens, outside the {MinFinalSpinTokens}-{MaxFinalSpinTokens} the capstone keeps", hint);
        }

        public override string ToString() => Id?.Value ?? "(no first-week schedule)";
    }
}
