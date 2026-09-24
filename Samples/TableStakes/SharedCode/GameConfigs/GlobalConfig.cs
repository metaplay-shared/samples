using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Schedule;

namespace Game.Logic
{
    /// <summary>
    /// Game-wide settings: the starting wallet, the wallet caps, the daily-reset schedule, and which published
    /// version of each table is active.
    /// <para>
    /// The active pointers let published tables stay unchanged. To retune, add a table with a new id and move
    /// the pointer instead of editing a table a player is part-way through (<c>docs/game-config.md</c>). The
    /// pointers are <see cref="MetaRef{TItem}"/>s, so a pointer to a missing table fails the config build.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class GlobalConfig : GameConfigKeyValue<GlobalConfig>
    {
        /// <summary>The wallet a new player starts with. Validation requires it to afford one wheel spin and the cheapest coin-priced cosmetic.</summary>
        [MetaMember(1)] public RewardBundle StartingWallet { get; private set; }

        /// <summary>The maximum balance of each currency. A grant that would exceed a cap fails as a whole and grants nothing.</summary>
        [MetaMember(2)] public int MaxCoins      { get; private set; }
        [MetaMember(3)] public int MaxGems       { get; private set; }
        [MetaMember(4)] public int MaxSpinTokens { get; private set; }

        /// <summary>
        /// When a player's day ends for daily rewards, in the player's local time. Missions do not read it and use
        /// the local-midnight day from <c>PlayerCalendar</c>. <see cref="DailyResetScheduleRules"/> accepts only a
        /// schedule with the same day boundaries, so both features reset at the same time
        /// (<c>docs/daily-rewards.md</c>, <c>docs/missions.md</c>).
        /// </summary>
        [MetaMember(5)] public MetaRecurringCalendarSchedule DailyResetSchedule { get; private set; }

        [MetaMember(10)] public MetaRef<DailyRewardTableInfo>      ActiveDailyRewardTable      { get; private set; }
        [MetaMember(11)] public MetaRef<FirstWeekScheduleInfo>     ActiveFirstWeekSchedule     { get; private set; }
        [MetaMember(12)] public MetaRef<WheelTableInfo>            ActiveWheelTable            { get; private set; }
        [MetaMember(13)] public MetaRef<MissionSetInfo>            ActiveDailyMissionSet       { get; private set; }
        [MetaMember(14)] public MetaRef<MissionSetInfo>            ActiveWeeklyMissionSet      { get; private set; }
        [MetaMember(15)] public MetaRef<TournamentRewardTableInfo> ActiveTournamentRewardTable { get; private set; }

        public GlobalConfig() { }

        public GlobalConfig(
            RewardBundle                  startingWallet,
            int                           maxCoins,
            int                           maxGems,
            int                           maxSpinTokens,
            MetaRecurringCalendarSchedule dailyResetSchedule,
            DailyRewardTableId            dailyRewardTable,
            FirstWeekScheduleId           firstWeekSchedule,
            WheelTableId                  wheelTable,
            MissionSetId                  dailyMissionSet,
            MissionSetId                  weeklyMissionSet,
            TournamentRewardTableId       tournamentRewardTable)
        {
            StartingWallet     = startingWallet;
            MaxCoins           = maxCoins;
            MaxGems            = maxGems;
            MaxSpinTokens      = maxSpinTokens;
            DailyResetSchedule = dailyResetSchedule;

            ActiveDailyRewardTable      = MetaRef<DailyRewardTableInfo>.FromKey(dailyRewardTable);
            ActiveFirstWeekSchedule     = MetaRef<FirstWeekScheduleInfo>.FromKey(firstWeekSchedule);
            ActiveWheelTable            = MetaRef<WheelTableInfo>.FromKey(wheelTable);
            ActiveDailyMissionSet       = MetaRef<MissionSetInfo>.FromKey(dailyMissionSet);
            ActiveWeeklyMissionSet      = MetaRef<MissionSetInfo>.FromKey(weeklyMissionSet);
            ActiveTournamentRewardTable = MetaRef<TournamentRewardTableInfo>.FromKey(tournamentRewardTable);
        }

        /// <summary>
        /// The cap on <paramref name="currency"/>, or zero for a currency with no cap. Unlike
        /// <see cref="WalletCaps.From"/>, it does not throw on a cap of zero, so the config build can report it.
        /// </summary>
        public int CapOf(CurrencyType currency) => new WalletCaps(MaxCoins, MaxGems, MaxSpinTokens).CapOf(currency);

        /// <summary>
        /// The name of the member that caps <paramref name="currency"/>, for errors reported against the cap.
        /// A currency with no cap is reported against <see cref="MaxCoins"/>.
        /// </summary>
        public static string CapMemberOf(CurrencyType currency)
        {
            switch (currency)
            {
                case CurrencyType.Coins:      return nameof(MaxCoins);
                case CurrencyType.Gems:       return nameof(MaxGems);
                case CurrencyType.SpinTokens: return nameof(MaxSpinTokens);
                default:                      return nameof(MaxCoins);
            }
        }
    }
}
