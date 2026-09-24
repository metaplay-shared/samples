using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// One participation milestone for display: the scored-match threshold, the reward, and whether it is reached
    /// and claimed.
    /// </summary>
    public readonly struct TournamentMilestoneStatus
    {
        public int          Index         { get; }
        public int          ScoredMatches { get; }
        public RewardBundle Reward        { get; }
        public bool         IsReached     { get; }
        public bool         IsClaimed     { get; }

        public bool IsClaimable => IsReached && !IsClaimed;

        public TournamentMilestoneStatus(int index, int scoredMatches, RewardBundle reward, bool isReached, bool isClaimed)
        {
            Index         = index;
            ScoredMatches = scoredMatches;
            Reward        = reward;
            IsReached     = isReached;
            IsClaimed     = isClaimed;
        }
    }

    /// <summary>
    /// The player's progress in the current tournament season, and the records that persist across seasons
    /// (<c>docs/seasonal-tournament.md</c>). It is an <see cref="IMatchCompletionObserver"/>, so it follows the
    /// observer rules in <c>docs/player.md</c>: it never grants rewards, and the player claims them with a
    /// separate action.
    /// <para>
    /// Joining stores the season, division and season end here, because the observer cannot read the league's
    /// state. A match completed after <see cref="SeasonEndsAt"/> does not count, so a player who has not joined
    /// the next season cannot score into the previous one.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class PlayerTournamentState : IMatchCompletionObserver
    {
        /// <summary>The season this progress belongs to, or <see cref="NoSeason"/> if the player has never joined.</summary>
        [MetaMember(1)] public int Season { get; private set; } = NoSeason;

        /// <summary>The division the player was placed in, or <see cref="EntityId.None"/>.</summary>
        [MetaMember(2)] public EntityId DivisionId { get; private set; }

        /// <summary>When the joined season ends. A match completed at or after this time does not count.</summary>
        [MetaMember(3)] public MetaTime SeasonEndsAt { get; private set; }

        /// <summary>When the player joined the season.</summary>
        [MetaMember(4)] public MetaTime JoinedAt { get; private set; }

        [MetaMember(5)] public int Wins { get; private set; }

        /// <summary>Completed matches that counted, at most <see cref="TournamentRules.ScoredMatchCap"/>.</summary>
        [MetaMember(6)] public int ScoredMatches { get; private set; }

        /// <summary>When the latest counted win completed. The standings use it to break ties.</summary>
        [MetaMember(7)] public MetaTime LastWinAt { get; private set; }

        /// <summary>
        /// The matches already counted this season. It never exceeds <see cref="TournamentRules.ScoredMatchCap"/>
        /// entries, and <see cref="Join"/> clears it.
        /// </summary>
        [MetaMember(8)] List<EntityId> _countedMatches = new List<EntityId>();

        /// <summary>Indices of the season's reward table milestones that have been paid.</summary>
        [MetaMember(9)] List<int> _claimedMilestoneIndices = new List<int>();

        /// <summary>
        /// The concluded divisions whose placement reward has been paid. Keyed by division, so an unclaimed
        /// season stays claimable after later seasons.
        /// </summary>
        [MetaMember(10)] List<EntityId> _claimedPlacements = new List<EntityId>();

        /// <summary>How many seasons this player has finished in first place.</summary>
        [MetaMember(11)] public int SeasonsWon { get; private set; }

        /// <summary>
        /// The cosmetics that tournament placements have paid, each listed once. It records the tournament's
        /// payouts. Ownership is in <see cref="PlayerModel.Cosmetics"/>.
        /// </summary>
        [MetaMember(12)] List<CosmeticId> _earnedCosmetics = new List<CosmeticId>();

        /// <summary>How many seasons this player has joined.</summary>
        [MetaMember(13)] public int SeasonsEntered { get; private set; }

        /// <summary>
        /// The concluded divisions whose result has been written to the player's event log, so
        /// <c>tournament_resolved</c> is written once per season however often the player reconnects.
        /// </summary>
        [MetaMember(14)] List<EntityId> _reportedResults = new List<EntityId>();

        /// <summary>
        /// The reward table of the joined season. It is stored rather than read from the active config, so a
        /// config update during the season cannot change a milestone the player is working towards or renumber
        /// a claimed one.
        /// </summary>
        [MetaMember(15)] public TournamentRewardTableId RewardTable { get; private set; }

        public const int NoSeason = -1;

        public IReadOnlyList<EntityId>   CountedMatches   => _countedMatches ??= new List<EntityId>();
        public IReadOnlyList<int>        ClaimedMilestoneIndices => _claimedMilestoneIndices ??= new List<int>();
        public IReadOnlyList<EntityId>   ClaimedPlacements => _claimedPlacements ??= new List<EntityId>();
        public IReadOnlyList<CosmeticId> EarnedCosmetics  => _earnedCosmetics ??= new List<CosmeticId>();
        public IReadOnlyList<EntityId>   ReportedResults  => _reportedResults ??= new List<EntityId>();

        /// <summary>Whether the player has joined a season. It stays true after that season has ended.</summary>
        public bool HasJoined => Season != NoSeason && DivisionId.IsValid;

        /// <summary>
        /// Whether the player is in a season that has not ended at <paramref name="now"/>. Screens use this
        /// rather than <see cref="HasJoined"/>, which stays true after the season ends and would hide the offer
        /// to join the next season.
        /// </summary>
        public bool IsRunningAt(MetaTime now) => HasJoined && now < SeasonEndsAt;

        /// <summary>Whether the player has used every scored match this season.</summary>
        public bool IsCapReached => ScoredMatches >= TournamentRules.ScoredMatchCap;

        public int Losses => ScoredMatches - Wins;

        public bool HasClaimedMilestone(int index) => ClaimedMilestoneIndices.Contains(index);

        public bool HasClaimedPlacement(EntityId divisionId) => ClaimedPlacements.Contains(divisionId);

        public bool HasReported(EntityId divisionId) => ReportedResults.Contains(divisionId);

        /// <summary>Record that a concluded division's result has been written to the event log.</summary>
        public void MarkReported(EntityId divisionId)
        {
            Lists.AddOnce(ref _reportedResults, divisionId);
        }

        public PlayerTournamentState() { }

        /// <summary>
        /// Join a season. This resets the per-season progress and claimed milestones. Records that persist across
        /// seasons, such as <see cref="SeasonsWon"/>, earned cosmetics and claimed placements, are kept.
        /// <para>
        /// Only <see cref="PlayerTournamentJoined"/> calls this, on its commit pass. That action refuses to join
        /// the season the player is already in, so this cannot reset a season in progress.
        /// </para>
        /// </summary>
        public void Join(int season, EntityId divisionId, MetaTime seasonEndsAt, TournamentRewardTableId rewardTable, MetaTime joinedAt)
        {
            Season         = season;
            DivisionId     = divisionId;
            SeasonEndsAt   = seasonEndsAt;
            RewardTable    = rewardTable;
            JoinedAt       = joinedAt;
            Wins           = 0;
            ScoredMatches  = 0;
            LastWinAt      = MetaTime.Epoch;
            SeasonsEntered += 1;

            _countedMatches          = new List<EntityId>();
            _claimedMilestoneIndices = new List<int>();
        }

        /// <summary>
        /// Whether a finished game would count towards the season: the player has joined, the game completed
        /// between <see cref="JoinedAt"/> and <see cref="SeasonEndsAt"/>, the cap is not reached, and the match has
        /// not been counted already. The lower bound matters for a result whose delivery was retried: it can arrive
        /// after the player joined the next season, and it belongs to the season it was played in.
        /// </summary>
        public bool WouldCount(EntityId matchId, MetaTime completedAt) =>
            HasJoined
            && matchId.IsValid
            && completedAt >= JoinedAt
            && completedAt < SeasonEndsAt
            && !IsCapReached
            && !CountedMatches.Contains(matchId);

        /// <summary>
        /// Count one finished game if <see cref="WouldCount"/> allows it. A win adds one to <see cref="Wins"/>.
        /// Any counted game adds one to <see cref="ScoredMatches"/>.
        /// </summary>
        /// <returns>Whether it counted.</returns>
        bool TryCountMatch(EntityId matchId, MetaTime completedAt, bool isWin)
        {
            if (!WouldCount(matchId, completedAt))
                return false;

            _countedMatches ??= new List<EntityId>();
            _countedMatches.Add(matchId);
            ScoredMatches += 1;

            if (isWin)
            {
                // Store wins, not points. The division converts wins to points when it ranks participants
                // (see TournamentRules.PointsPerWin).
                Wins     += 1;
                LastWinAt = completedAt;
            }

            return true;
        }

        /// <summary>
        /// Count a finished game and emit <see cref="PlayerEventTournamentMatchCounted"/> if it counted. It uses
        /// <see cref="MatchCompletion.CompletedAt"/>, not a clock, and changes no state outside this object.
        /// </summary>
        public void OnMatchCompleted(in MatchCompletionContext context)
        {
            MatchCompletion completion = context.Completion;

            if (!TryCountMatch(completion.MatchId, completion.CompletedAt, completion.IsWin))
                return;

            context.Emit(new PlayerEventTournamentMatchCounted(
                Season, completion.MatchId, completion.IsWin, ScoredMatches, Wins));
        }

        /// <summary>
        /// The milestones of <paramref name="table"/>, with whether the player has reached and claimed each. Empty
        /// when the table has no milestones. Before the player joins, no milestone is reached.
        /// </summary>
        public List<TournamentMilestoneStatus> Milestones(TournamentRewardTableInfo table)
        {
            List<TournamentMilestoneStatus> statuses = new List<TournamentMilestoneStatus>();
            if (table?.Milestones == null)
                return statuses;

            for (int index = 0; index < table.Milestones.Count; index++)
            {
                TournamentMilestoneInfo milestone = table.Milestones[index];
                statuses.Add(new TournamentMilestoneStatus(
                    index,
                    milestone.ScoredMatches,
                    milestone.Reward,
                    isReached: HasJoined && ScoredMatches >= milestone.ScoredMatches,
                    isClaimed: HasClaimedMilestone(index)));
            }

            return statuses;
        }

        /// <summary>
        /// Whether milestone <paramref name="index"/> of <paramref name="table"/> may be claimed: the player has
        /// joined, the milestone exists, the player has reached it, and it has not been paid.
        /// </summary>
        public MetaActionResult CanClaimMilestone(TournamentRewardTableInfo table, int index)
        {
            if (!HasJoined)
                return ActionResults.NotInTournament;
            if (table?.Milestones == null || index < 0 || index >= table.Milestones.Count)
                return ActionResults.NoSuchTournamentMilestone;
            if (ScoredMatches < table.Milestones[index].ScoredMatches)
                return ActionResults.TournamentMilestoneNotReached;
            if (HasClaimedMilestone(index))
                return ActionResults.TournamentRewardAlreadyClaimed;

            return MetaActionResult.Success;
        }

        /// <summary>Mark a milestone as paid. Call only after its wallet settlement has been committed.</summary>
        public void MarkMilestoneClaimed(int index)
        {
            Lists.AddOnce(ref _claimedMilestoneIndices, index);
        }

        /// <summary>
        /// Mark a concluded division's placement reward as paid, count a first place in
        /// <see cref="SeasonsWon"/>, and record the reward cosmetic, if any.
        /// </summary>
        public void MarkPlacementClaimed(TournamentHistoryEntry result)
        {
            Lists.AddOnce(ref _claimedPlacements, result.DivisionId);

            if (result.Placement == 1)
                SeasonsWon += 1;

            if (result.RewardCosmetic != null)
                Lists.AddOnce(ref _earnedCosmetics, result.RewardCosmetic);
        }

        public override string ToString() =>
            HasJoined ? $"Season {Season}: {Wins} pts from {ScoredMatches}/{TournamentRules.ScoredMatchCap}" : "(not entered)";
    }
}
