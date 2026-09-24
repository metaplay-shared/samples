using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary> Identifier for a <see cref="RankTrackInfo"/> row. </summary>
    [MetaSerializable]
    public class RankTrackId : StringId<RankTrackId>
    {
        /// <summary> The flat track: tokens and cards whose numbers do not move with rank. </summary>
        public static readonly RankTrackId None = FromString("None");
    }

    /// <summary>
    /// What one milestone rank adds. Always deltas, never absolute stats — the config build rejects any other
    /// form, so a track can be read as growth without knowing the card it applies to.
    /// </summary>
    [MetaSerializable]
    public class RankTrackStep
    {
        [MetaMember(1)] public int AttackDelta { get; private set; }
        [MetaMember(2)] public int HealthDelta { get; private set; }
        [MetaMember(3)] public int CostDelta   { get; private set; }
        /// <summary> Applies to the literal base of the amount of the card's first Hello step. </summary>
        [MetaMember(4)] public int AmountDelta { get; private set; }

        public RankTrackStep() { }

        public RankTrackStep(int attackDelta, int healthDelta, int costDelta, int amountDelta)
        {
            AttackDelta = attackDelta;
            HealthDelta = healthDelta;
            CostDelta   = costDelta;
            AmountDelta = amountDelta;
        }

        public bool IsEmpty => AttackDelta == 0 && HealthDelta == 0 && CostDelta == 0 && AmountDelta == 0;
    }

    /// <summary>
    /// How a card grows with its rank. Ranks 2–5 can grant cumulative deltas, so a rank-5
    /// card carries all four steps. Tracks are applied when the match resolves each deck against its owner's
    /// collection at the deal — inside the engine a card's numbers are simply what the track made them.
    /// </summary>
    [MetaSerializable]
    public class RankTrackInfo : IGameConfigData<RankTrackId>
    {
        [MetaMember(1)] public RankTrackId   TrackId { get; private set; }
        [MetaMember(2)] public RankTrackStep Rank3   { get; private set; } = new RankTrackStep();
        [MetaMember(3)] public RankTrackStep Rank5   { get; private set; } = new RankTrackStep();
        [MetaMember(4)] public string        Notes   { get; private set; }

        [MetaMember(5)] public RankTrackStep Rank2 { get; private set; } = new RankTrackStep();
        [MetaMember(6)] public RankTrackStep Rank4 { get; private set; } = new RankTrackStep();

        public RankTrackId ConfigKey => TrackId;

        public RankTrackInfo() { }

        public RankTrackInfo(RankTrackId trackId, RankTrackStep rank3 = null, RankTrackStep rank5 = null, string notes = null, RankTrackStep rank2 = null, RankTrackStep rank4 = null)
        {
            TrackId = trackId;
            Rank3   = rank3 ?? new RankTrackStep();
            Rank5   = rank5 ?? new RankTrackStep();
            Notes   = notes;
            Rank2   = rank2 ?? new RankTrackStep();
            Rank4   = rank4 ?? new RankTrackStep();
        }

        public const int FirstGrowthRank = 2;
        public const int LastGrowthRank = 5;

        /// <summary> The authored increment at this rank, before accumulation. </summary>
        public RankTrackStep GetStep(int rank) => rank switch
        {
            2 => Rank2,
            3 => Rank3,
            4 => Rank4,
            5 => Rank5,
            _ => null,
        };

        /// <summary> The accumulated growth a card at <paramref name="rank"/> has earned. </summary>
        public RankTrackStep GetCumulativeDeltas(int rank)
        {
            int attack = 0;
            int health = 0;
            int cost   = 0;
            int amount = 0;

            for (int current = FirstGrowthRank; current <= LastGrowthRank && current <= rank; current++)
            {
                RankTrackStep step = GetStep(current);
                if (step == null)
                    continue;
                attack += step.AttackDelta;
                health += step.HealthDelta;
                cost   += step.CostDelta;
                amount += step.AmountDelta;
            }

            return new RankTrackStep(attack, health, cost, amount);
        }

        /// <summary> True when the track moves the magnitude of the card's effect rather than its body. </summary>
        public bool ScalesEffectAmount =>
            (Rank2 != null && Rank2.AmountDelta != 0) || (Rank3 != null && Rank3.AmountDelta != 0) ||
            (Rank4 != null && Rank4.AmountDelta != 0) || (Rank5 != null && Rank5.AmountDelta != 0);

        /// <summary> True when nothing changes at any rank. </summary>
        public bool IsFlat =>
            (Rank2 == null || Rank2.IsEmpty) && (Rank3 == null || Rank3.IsEmpty) &&
            (Rank4 == null || Rank4.IsEmpty) && (Rank5 == null || Rank5.IsEmpty);
    }
}
