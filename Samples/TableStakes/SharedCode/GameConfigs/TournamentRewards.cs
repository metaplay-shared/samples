using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// Identifies one published version of a season's reward table. A resolved season keeps the table it was
    /// played under, so a pending claim pays the rewards shown during the season.
    /// </summary>
    [MetaSerializable]
    public class TournamentRewardTableId : StringId<TournamentRewardTableId> { }

    /// <summary>
    /// A participation milestone: complete <see cref="ScoredMatches"/> scored matches during the season to earn
    /// <see cref="Reward"/>.
    /// <para>
    /// Milestones count completed matches rather than points, so a player who loses every match still earns
    /// them. Placement is rewarded separately (<c>docs/seasonal-tournament.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class TournamentMilestoneInfo
    {
        [MetaMember(1)] public int          ScoredMatches { get; private set; }
        [MetaMember(2)] public RewardBundle Reward        { get; private set; }

        public TournamentMilestoneInfo() { }

        public TournamentMilestoneInfo(int scoredMatches, RewardBundle reward)
        {
            ScoredMatches = scoredMatches;
            Reward        = reward;
        }
    }

    /// <summary>
    /// A placement band: finish at rank <see cref="MaxRank"/> or better in the group to earn the band's reward.
    /// A band may also award one achievement cosmetic. A player who already owns it does not get a duplicate.
    /// </summary>
    [MetaSerializable]
    public class TournamentPlacementInfo
    {
        [MetaMember(1)] public int          MaxRank { get; private set; }

        /// <summary>The currency reward of the band, or null for a band that awards only a cosmetic.</summary>
        [MetaMember(2)] public RewardBundle Reward  { get; private set; }

        /// <summary>The achievement cosmetic this band awards, or null.</summary>
        [MetaMember(3)] public MetaRef<CosmeticInfo> Cosmetic { get; private set; }

        public TournamentPlacementInfo() { }

        public TournamentPlacementInfo(int maxRank, RewardBundle reward, CosmeticId cosmetic = null)
        {
            MaxRank  = maxRank;
            Reward   = reward;
            Cosmetic = cosmetic == null ? null : MetaRef<CosmeticInfo>.FromKey(cosmetic);
        }
    }

    /// <summary>
    /// One version of the seasonal tournament's rewards (<c>docs/seasonal-tournament.md</c>). Milestones reward
    /// participation and placement bands reward rank, so a player who rarely wins still earns rewards.
    /// <para>
    /// The season schedule is in runtime options rather than here, because it is an environment setting, not a
    /// LiveOps action.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class TournamentRewardTableInfo : IGameConfigData<TournamentRewardTableId>, IValidatedConfigItem
    {
        [MetaMember(1)] public TournamentRewardTableId Id { get; private set; }
        [MetaMember(2)] List<TournamentMilestoneInfo>  _milestones;
        [MetaMember(3)] List<TournamentPlacementInfo>  _placements;

        /// <summary>Participation milestones, in ascending <see cref="TournamentMilestoneInfo.ScoredMatches"/> order.</summary>
        public IReadOnlyList<TournamentMilestoneInfo> Milestones => _milestones;

        /// <summary>
        /// Placement bands, in ascending <see cref="TournamentPlacementInfo.MaxRank"/> order. A rank past the last
        /// band earns no placement reward.
        /// </summary>
        public IReadOnlyList<TournamentPlacementInfo> Placements => _placements;

        /// <summary>The first band whose <see cref="TournamentPlacementInfo.MaxRank"/> covers <paramref name="placement"/>, or null if none does.</summary>
        public TournamentPlacementInfo BandFor(int placement)
        {
            if (_placements == null || placement <= 0)
                return null;

            foreach (TournamentPlacementInfo band in _placements)
            {
                if (placement <= band.MaxRank)
                    return band;
            }

            return null;
        }

        /// <summary>The milestone at <paramref name="index"/>, or null if the index is out of range.</summary>
        public TournamentMilestoneInfo MilestoneAt(int index) =>
            _milestones != null && index >= 0 && index < _milestones.Count ? _milestones[index] : null;

        public TournamentRewardTableId ConfigKey => Id;

        public TournamentRewardTableInfo() { }

        /// <summary>
        /// Built from <c>GameConfigSource/TournamentRewards.csv</c>. Milestones and placement bands are in
        /// separate columns on the same rows, so the shorter list leaves its cells blank on the remaining rows.
        /// </summary>
        [MetaGameConfigBuildConstructor]
        public TournamentRewardTableInfo(TournamentRewardTableId id, List<TournamentMilestoneInfo> milestones, List<TournamentPlacementInfo> placements)
        {
            Id          = id;
            _milestones = milestones.ToList();
            _placements = placements.ToList();
        }

        public void Validate(ConfigItemValidation validation)
        {
            validation.RequireNotEmpty(_milestones, nameof(Milestones));
            validation.RequireNotEmpty(_placements, nameof(Placements));

            if (_milestones != null)
            {
                int previousScoredMatches = 0;
                for (int index = 0; index < _milestones.Count; index++)
                {
                    string hint = $"{nameof(Milestones)}[{index}]";
                    validation.Require(_milestones[index].ScoredMatches > previousScoredMatches, $"is at {_milestones[index].ScoredMatches} matches, which does not come after {previousScoredMatches}", hint);
                    validation.Require(_milestones[index].ScoredMatches <= TournamentRules.ScoredMatchCap, $"is at {_milestones[index].ScoredMatches} matches, past the cap of {TournamentRules.ScoredMatchCap}", hint);
                    previousScoredMatches = _milestones[index].ScoredMatches;

                    if (_milestones[index].Reward == null)
                        validation.Error("has no reward", hint);
                    else
                        _milestones[index].Reward.Validate(validation, hint);
                }
            }

            if (_placements != null)
            {
                int previousRank = 0;
                for (int index = 0; index < _placements.Count; index++)
                {
                    string hint = $"{nameof(Placements)}[{index}]";
                    validation.Require(_placements[index].MaxRank > previousRank, $"covers rank {_placements[index].MaxRank}, which does not come after {previousRank}", hint);
                    previousRank = _placements[index].MaxRank;

                    validation.Require(_placements[index].MaxRank <= TournamentRules.GroupSize, $"covers rank {_placements[index].MaxRank}, past the {TournamentRules.GroupSize} seats a group has", hint);

                    if (_placements[index].Reward == null && _placements[index].Cosmetic == null)
                        validation.Error("has no reward", hint);
                    else
                        _placements[index].Reward?.Validate(validation, hint);
                }
            }
        }

        public override string ToString() => Id?.Value ?? "(no tournament reward table)";
    }
}
