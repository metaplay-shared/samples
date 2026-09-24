using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// The player joined a tournament season and was placed in a group.
    /// <para>
    /// The group is reported as its index within the season rather than as an entity id, so an analyst can see
    /// whether human players were placed together in the lowest-numbered groups.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.TournamentJoined, displayName: "Tournament joined", docString: "The player entered the seasonal tournament, and how far into the season they arrived.")]
    [AnalyticsAlias("tournament_joined")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Tournament)]
    public class PlayerEventTournamentJoined : PlayerEventBase
    {
        [MetaMember(1)] public int Season { get; private set; }

        /// <summary>The zero-based group index within the season.</summary>
        [MetaMember(2)] public int Group { get; private set; }

        /// <summary>How far into the season the player joined, in tenths: 0 at the start, 10 at the end.</summary>
        [MetaMember(3)] public int ElapsedTenths { get; private set; }

        /// <summary>The season's reward table.</summary>
        [MetaMember(4)] public TournamentRewardTableId RewardTable { get; private set; }

        public override string EventDescription => $"Joined tournament season {Season}, group {Group}, {ElapsedTenths}/10 of the way in.";

        public PlayerEventTournamentJoined() { }

        public PlayerEventTournamentJoined(int season, int group, int elapsedTenths, TournamentRewardTableId rewardTable)
        {
            Season        = season;
            Group         = group;
            ElapsedTenths = elapsedTenths;
            RewardTable   = rewardTable;
        }
    }

    /// <summary>A completed match counted towards the season. Emitted by <see cref="PlayerTournamentState.OnMatchCompleted"/>.</summary>
    [AnalyticsEvent(AnalyticsEventCodes.TournamentMatchCounted, displayName: "Tournament match counted", docString: "A completed match used one of the player's scored attempts, and whether it scored.")]
    [AnalyticsAlias("tournament_match_counted")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Tournament, AnalyticsKeywords.Progression)]
    public class PlayerEventTournamentMatchCounted : PlayerEventBase
    {
        [MetaMember(1)] public int      Season  { get; private set; }
        [MetaMember(2)] public EntityId MatchId { get; private set; }
        [MetaMember(3)] public bool     IsWin   { get; private set; }

        /// <summary>The one-based number of this scored match in the season.</summary>
        [MetaMember(4)] public int Attempt { get; private set; }

        [MetaMember(5)] public int WinsAfter { get; private set; }

        public override string EventDescription =>
            $"Tournament attempt {Attempt}/{TournamentRules.ScoredMatchCap} at {MatchId} {(IsWin ? "scored" : "did not score")}, reaching {WinsAfter} pts.";

        public PlayerEventTournamentMatchCounted() { }

        public PlayerEventTournamentMatchCounted(int season, EntityId matchId, bool isWin, int attempt, int winsAfter)
        {
            Season    = season;
            MatchId   = matchId;
            IsWin     = isWin;
            Attempt   = attempt;
            WinsAfter = winsAfter;
        }
    }

    /// <summary>A participation milestone was paid. Its correlation id matches the economy rows of the payment.</summary>
    [AnalyticsEvent(AnalyticsEventCodes.TournamentMilestoneClaimed, displayName: "Tournament milestone claimed", docString: "The player claimed a participation milestone of the seasonal tournament.")]
    [AnalyticsAlias("tournament_milestone_claimed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Tournament, AnalyticsKeywords.Claim, AnalyticsKeywords.Source)]
    public class PlayerEventTournamentMilestoneClaimed : PlayerEventBase
    {
        [MetaMember(1)] public int Season { get; private set; }

        /// <summary>The zero-based milestone index in the reward table.</summary>
        [MetaMember(2)] public int Milestone { get; private set; }

        /// <summary>The number of scored matches the milestone required.</summary>
        [MetaMember(3)] public int ScoredMatches { get; private set; }

        [MetaMember(4)] public TournamentRewardTableId RewardTable { get; private set; }

        /// <summary>The same id as the <c>economy_transaction</c> rows this claim wrote.</summary>
        [MetaMember(5)] public AnalyticsCorrelationId Correlation { get; private set; }

        public override string EventDescription => $"Claimed tournament milestone {Milestone} at {ScoredMatches} matches in season {Season}.";

        public PlayerEventTournamentMilestoneClaimed() { }

        public PlayerEventTournamentMilestoneClaimed(int season, int milestone, int scoredMatches, TournamentRewardTableId rewardTable, AnalyticsCorrelationId correlation)
        {
            Season        = season;
            Milestone     = milestone;
            ScoredMatches = scoredMatches;
            RewardTable   = rewardTable;
            Correlation   = correlation;
        }
    }

    /// <summary>
    /// The player's season concluded, with their placement. Written once per group, when the result first
    /// reaches the player's timeline.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.TournamentResolved, displayName: "Tournament resolved", docString: "A tournament season the player took part in concluded, and where they placed in their group.")]
    [AnalyticsAlias("tournament_resolved")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Tournament, AnalyticsKeywords.Progression)]
    public class PlayerEventTournamentResolved : PlayerEventBase
    {
        [MetaMember(1)] public int Season    { get; private set; }
        [MetaMember(2)] public int Placement { get; private set; }
        [MetaMember(3)] public int Wins      { get; private set; }
        [MetaMember(4)] public int ScoredMatches { get; private set; }

        /// <summary>How many of the group's participants were human players.</summary>
        [MetaMember(5)] public int HumanCount { get; private set; }

        /// <summary>How many of the group's participants were bots.</summary>
        [MetaMember(6)] public int BotCount { get; private set; }

        public override string EventDescription =>
            $"Finished tournament season {Season} in place {Placement} with {Wins} pts, against {HumanCount} people and {BotCount} computer players.";

        public PlayerEventTournamentResolved() { }

        public PlayerEventTournamentResolved(int season, int placement, int wins, int scoredMatches, int humanCount, int botCount)
        {
            Season        = season;
            Placement     = placement;
            Wins          = wins;
            ScoredMatches = scoredMatches;
            HumanCount    = humanCount;
            BotCount      = botCount;
        }
    }

    /// <summary>A placement reward was paid. Its correlation id matches the economy rows of the payment.</summary>
    [AnalyticsEvent(AnalyticsEventCodes.TournamentPlacementRewardClaimed, displayName: "Tournament placement reward claimed", docString: "The player claimed the placement reward of a concluded tournament season.")]
    [AnalyticsAlias("tournament_placement_reward_claimed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Tournament, AnalyticsKeywords.Claim, AnalyticsKeywords.Source)]
    public class PlayerEventTournamentPlacementRewardClaimed : PlayerEventBase
    {
        [MetaMember(1)] public int Season    { get; private set; }
        [MetaMember(2)] public int Placement { get; private set; }

        /// <summary>The best placement of the reward band that paid, so analysts can group placements by band.</summary>
        [MetaMember(3)] public int Band { get; private set; }

        [MetaMember(4)] public TournamentRewardTableId RewardTable { get; private set; }

        /// <summary>The cosmetic the band awarded, or null.</summary>
        [MetaMember(5)] public CosmeticId Cosmetic { get; private set; }

        [MetaMember(6)] public AnalyticsCorrelationId Correlation { get; private set; }

        public override string EventDescription => $"Claimed the placement reward for finishing {Placement} in tournament season {Season}.";

        public PlayerEventTournamentPlacementRewardClaimed() { }

        public PlayerEventTournamentPlacementRewardClaimed(int season, int placement, int band, TournamentRewardTableId rewardTable, CosmeticId cosmetic, AnalyticsCorrelationId correlation)
        {
            Season      = season;
            Placement   = placement;
            Band        = band;
            RewardTable = rewardTable;
            Cosmetic    = cosmetic;
            Correlation = correlation;
        }
    }
}
