using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using Metaplay.Core.Model;
using Metaplay.Core.Rewards;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Game.Logic
{
    /// <summary>
    /// How a tournament participant appears to the rest of the group. It contains a
    /// <see cref="PlayerPublicIdentity"/> instead of extending it, because the SDK requires a division avatar to
    /// extend <see cref="PlayerDivisionAvatarBase"/>. It is a copy: the SDK sends a new avatar at session start and
    /// when the player rejoins, and <c>IPlayerModelServerListener.OnPublicIdentityChanged</c> makes the player
    /// actor send one when the identity changes in between.
    /// </summary>
    [MetaSerializableDerived(200)]
    public class TournamentAvatar : PlayerDivisionAvatarBase
    {
        [MetaMember(1)] public PlayerPublicIdentity Identity { get; private set; }

        TournamentAvatar() { }

        public TournamentAvatar(PlayerPublicIdentity identity)
        {
            Identity = identity;
        }

        public override string ToString() => Identity?.ToString() ?? "(no avatar)";
    }

    /// <summary>
    /// One participant's season totals: wins, scored matches, and when the latest win completed.
    /// <para>
    /// It holds totals rather than a running sum, and <see cref="TournamentScoreEvent"/> carries totals too. The
    /// player model is the authority on the totals, so sending the same totals again changes nothing, and a score
    /// event lost in a restart is corrected by the next one.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(200)]
    public class TournamentContribution : IDivisionContribution
    {
        [MetaMember(1)] public int      Wins          { get; set; }
        [MetaMember(2)] public int      ScoredMatches { get; set; }
        [MetaMember(3)] public MetaTime LastWinAt     { get; set; }

        public TournamentContribution() { }

        public override string ToString() => $"{Wins} pts from {ScoredMatches} matches";
    }

    /// <summary>
    /// A participant's score and tie-break order.
    /// <para>
    /// The comparison matches <see cref="TournamentStandings.Compare"/> without its final seat-index tie-break,
    /// which the SDK's sort applies. The SDK uses this <see cref="IDivisionScore"/> to rank participants in the
    /// LiveOps Dashboard and in <c>SortOrderIndex</c>. The standings players see come from
    /// <see cref="TournamentStandings"/>, which also includes the bots.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(200)]
    public class TournamentScore : IDivisionScore
    {
        [MetaMember(1)] public int      Wins          { get; private set; }
        [MetaMember(2)] public int      ScoredMatches { get; private set; }
        [MetaMember(3)] public MetaTime LastWinAt     { get; private set; }

        public int Losses => ScoredMatches - Wins;

        public TournamentScore() { }

        public TournamentScore(int wins, int scoredMatches, MetaTime lastWinAt)
        {
            Wins          = wins;
            ScoredMatches = scoredMatches;
            LastWinAt     = lastWinAt;
        }

        public int CompareTo(IDivisionScore other)
        {
            if (other is not TournamentScore rhs)
                return 0;

            if (Wins != rhs.Wins)
                return Wins.CompareTo(rhs.Wins);
            if (Losses != rhs.Losses)
                return rhs.Losses.CompareTo(Losses);

            // An earlier last win ranks higher, so the comparison is reversed.
            return rhs.LastWinAt.CompareTo(LastWinAt);
        }

        public override string ToString() => $"{Wins} pts";
    }

    /// <summary>
    /// The player's season totals, sent to the division. They are totals, not increments (see
    /// <see cref="TournamentContribution"/>).
    /// </summary>
    [MetaSerializableDerived(200)]
    public class TournamentScoreEvent : DivisionScoreEventBase<TournamentContribution>
    {
        [MetaMember(1)] public int      Wins          { get; private set; }
        [MetaMember(2)] public int      ScoredMatches { get; private set; }
        [MetaMember(3)] public MetaTime LastWinAt     { get; private set; }

        TournamentScoreEvent() { }

        public TournamentScoreEvent(int wins, int scoredMatches, MetaTime lastWinAt)
        {
            Wins          = wins;
            ScoredMatches = scoredMatches;
            LastWinAt     = lastWinAt;
        }

        /// <summary>
        /// Replaces the contribution with these totals, unless <see cref="ScoredMatches"/> is lower than the stored
        /// value. Replacing makes a repeated event harmless, and the check stops an older event that arrives late
        /// from undoing a newer one.
        /// </summary>
        public override void AccumulateToContribution(TournamentContribution contribution)
        {
            if (ScoredMatches < contribution.ScoredMatches)
                return;

            contribution.Wins          = Wins;
            contribution.ScoredMatches = ScoredMatches;
            contribution.LastWinAt     = LastWinAt;
        }
    }

    /// <summary>One human seat in a tournament group.</summary>
    [MetaSerializableDerived(200)]
    public class TournamentParticipantState : PlayerDivisionParticipantStateBase<TournamentScore, TournamentContribution, TournamentAvatar>
    {
        public override string ParticipantInfo =>
            $"{PlayerAvatar?.Identity?.DisplayName ?? "?"}: {PlayerContribution?.Wins ?? 0} pts";

        public TournamentParticipantState() { }
    }

    /// <summary>
    /// One tournament group for one season (<c>docs/seasonal-tournament.md</c>). The model stores only the human
    /// participants. <see cref="TournamentBots"/> computes the bots from the division index and the time, so they
    /// cannot be rerolled. The client leaderboard and the server placement both use <see cref="Standings"/>. The
    /// model does not tick: contributions change only through score events.
    /// </summary>
    [MetaSerializableDerived(200)]
    [SupportedSchemaVersions(1, 1)]
    public class TournamentDivisionModel : PlayerDivisionModelBase<TournamentDivisionModel, TournamentParticipantState, TournamentScore, TournamentAvatar>
    {
        public const int TicksPerSecondConst = 1;

        [IgnoreDataMember] public override int TicksPerSecond => TicksPerSecondConst;

        [IgnoreDataMember] public SharedGameConfig SharedGameConfig => (SharedGameConfig)GameConfig;

        public override void OnTick() { }

        public override void OnFastForwardTime(MetaDuration elapsedTime) { }

        public override string GetDisplayNameForDashboard() => $"Tournament group {DivisionIndex}";

        /// <summary>A participant's score: <see cref="TournamentRules.PointsPerWin"/> points per win.</summary>
        public override TournamentScore ComputeScore(int participantIndex)
        {
            if (!Participants.TryGetValue(participantIndex, out TournamentParticipantState participant) || participant.PlayerContribution == null)
                return new TournamentScore(0, 0, MetaTime.Epoch);

            TournamentContribution contribution = participant.PlayerContribution;
            return new TournamentScore(
                contribution.Wins * TournamentRules.PointsPerWin,
                contribution.ScoredMatches,
                contribution.LastWinAt);
        }

        /// <summary>
        /// The group's standings at <paramref name="now"/>, best first: the human participants and the bots that
        /// fill the empty seats.
        /// </summary>
        public List<TournamentEntrant> Standings(MetaTime now)
        {
            int                     groupSize = DesiredParticipantCount > 0 ? DesiredParticipantCount : TournamentRules.GroupSize;
            List<TournamentEntrant> seats     = new List<TournamentEntrant>(groupSize);
            HashSet<int>            occupied  = new HashSet<int>();

            foreach ((int participantIndex, TournamentParticipantState participant) in Participants)
            {
                occupied.Add(participantIndex);

                TournamentContribution contribution = participant.PlayerContribution ?? new TournamentContribution();
                seats.Add(new TournamentEntrant(
                    participantIndex,
                    isBot: false,
                    participant.PlayerAvatar?.Identity,
                    contribution.Wins,
                    contribution.ScoredMatches,
                    contribution.LastWinAt));
            }

            seats.AddRange(TournamentBots.Fill(
                SharedGameConfig,
                DivisionIndex,
                groupSize,
                occupied,
                StartsAt,
                EndsAt,
                now));

            return TournamentStandings.Rank(seats);
        }

        /// <summary>A participant's one-based placement in the standings at the season end.</summary>
        public int PlacementOf(int participantIndex) =>
            TournamentStandings.PlacementOf(Standings(EndsAt), participantIndex);

        public TournamentDivisionModel() { }
    }

    /// <summary>
    /// One participant's season result, decided from the season's reward table when the group concluded, so a
    /// later config change does not alter it.
    /// <para>
    /// <c>Apply</c> does nothing and <c>Rewards</c> is empty, so the SDK's claim path pays nothing. That path cannot
    /// take a <c>commit</c> flag, carry a correlation id or return a refusal, which the wallet requires
    /// (<c>docs/economy.md</c>). <see cref="PlayerTournamentPlacementClaim"/> pays the reward from the history entry.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(200)]
    public class TournamentDivisionRewards : IDivisionRewards
    {
        [MetaMember(1)] public bool IsClaimed { get; set; }

        /// <summary>The participant's one-based placement.</summary>
        [MetaMember(2)] public int Placement { get; private set; }

        [MetaMember(3)] public int Wins          { get; private set; }
        [MetaMember(4)] public int ScoredMatches { get; private set; }

        /// <summary>How many of the group's seats were human players. The rest were bots.</summary>
        [MetaMember(5)] public int HumanCount { get; private set; }

        [MetaMember(6)] public int GroupSize { get; private set; }

        /// <summary>The placement's reward, or null for a placement with no reward.</summary>
        [MetaMember(7)] public RewardBundle Reward { get; private set; }

        /// <summary>The cosmetic the placement earned, or null.</summary>
        [MetaMember(8)] public CosmeticId RewardCosmetic { get; private set; }

        /// <summary>The season's reward table.</summary>
        [MetaMember(9)] public TournamentRewardTableId RewardTable { get; private set; }

        IEnumerable<MetaReward> IDivisionRewards.Rewards => System.Array.Empty<MetaReward>();

        void IDivisionRewards.Apply(IModel model) { }

        TournamentDivisionRewards() { }

        public TournamentDivisionRewards(
            int                     placement,
            int                     wins,
            int                     scoredMatches,
            int                     humanCount,
            int                     groupSize,
            RewardBundle            reward,
            CosmeticId              rewardCosmetic,
            TournamentRewardTableId rewardTable)
        {
            Placement      = placement;
            Wins           = wins;
            ScoredMatches  = scoredMatches;
            HumanCount     = humanCount;
            GroupSize      = groupSize;
            Reward         = reward;
            RewardCosmetic = rewardCosmetic;
            RewardTable    = rewardTable;
        }

        public override string ToString() => $"#{Placement} with {Wins} pts";
    }

    /// <summary>
    /// A concluded season in the player's division history, with the placement, totals and reward decided when
    /// the division concluded. It stays until the player claims it. A new season adds an entry and removes none,
    /// so it cannot remove an unclaimed reward.
    /// </summary>
    [MetaSerializableDerived(200)]
    public class TournamentHistoryEntry : PlayerDivisionHistoryEntryBase
    {
        /// <summary>The player's one-based placement, or zero if they were not in the group.</summary>
        [MetaMember(1)] public int Placement { get; private set; }

        [MetaMember(2)] public int Wins          { get; private set; }
        [MetaMember(3)] public int ScoredMatches { get; private set; }

        /// <summary>How many of the group's seats were human players. The rest were bots.</summary>
        [MetaMember(4)] public int HumanCount { get; private set; }

        [MetaMember(5)] public int GroupSize { get; private set; }

        /// <summary>The placement's reward, or null for a placement with no reward.</summary>
        [MetaMember(6)] public RewardBundle Reward { get; private set; }

        /// <summary>The cosmetic this placement earned, or null.</summary>
        [MetaMember(7)] public CosmeticId RewardCosmetic { get; private set; }

        /// <summary>The season's reward table.</summary>
        [MetaMember(8)] public TournamentRewardTableId RewardTable { get; private set; }

        /// <summary>Whether this placement earned a reward or a cosmetic. It does not say whether the reward has been claimed.</summary>
        public bool HasReward => Reward != null || RewardCosmetic != null;

        public int Season => DivisionIndex.Season;

        TournamentHistoryEntry() : base(default, default, null) { }

        public TournamentHistoryEntry(
            EntityId                divisionId,
            DivisionIndex           divisionIndex,
            int                     placement,
            int                     wins,
            int                     scoredMatches,
            int                     humanCount,
            int                     groupSize,
            RewardBundle            reward,
            CosmeticId              rewardCosmetic,
            TournamentRewardTableId rewardTable)
            : base(divisionId, divisionIndex, rewards: null)
        {
            Placement      = placement;
            Wins           = wins;
            ScoredMatches  = scoredMatches;
            HumanCount     = humanCount;
            GroupSize      = groupSize;
            Reward         = reward;
            RewardCosmetic = rewardCosmetic;
            RewardTable    = rewardTable;
        }

        public override string ToString() => $"Season {Season}: #{Placement} with {Wins} pts";
    }

    /// <summary>
    /// What the league manager receives about a participant when their season ends. The tournament has one rank
    /// and no promotion, so it carries only the avatar.
    /// </summary>
    [MetaSerializableDerived(200)]
    public class TournamentConclusionResult : PlayerDivisionParticipantConclusionResultBase<TournamentAvatar>
    {
        TournamentConclusionResult() : base(EntityId.None, null) { }

        public TournamentConclusionResult(EntityId participantId, TournamentAvatar avatar) : base(participantId, avatar) { }
    }

    /// <summary>
    /// The player's league state: the current division and the history of concluded seasons. The SDK's league
    /// integration owns it and changes it with its server actions.
    /// <para>
    /// The player's season progress (scored matches, wins, claimed milestones) is in
    /// <see cref="PlayerTournamentState"/> instead, because the match-completion observer writes it and may only
    /// change state that is excluded from the checksum (<c>docs/player.md</c>, "Observer rules").
    /// </para>
    /// </summary>
    [MetaSerializableDerived(200)]
    public class TournamentClientState : DivisionClientStateBase<TournamentHistoryEntry>
    {
        public TournamentClientState() { }
    }
}
