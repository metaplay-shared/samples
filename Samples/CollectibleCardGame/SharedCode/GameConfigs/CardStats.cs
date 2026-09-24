namespace Game.Logic
{
    /// <summary>
    /// A card's numbers at one rank: its printed values with its rank track's cumulative deltas applied.
    /// <para>
    /// This is the one place a rank track meets a card. The collection's card detail reads it to show what a
    /// player's own copy actually does, and the match resolves each deck against its owner's collection at the
    /// deal through the same function — inside the engine a card's numbers are simply what this returned.
    /// </para>
    /// </summary>
    public readonly struct CardStats
    {
        public readonly int Attack;
        public readonly int Health;
        public readonly int Cost;
        /// <summary> What the rank track adds to the literal base of the card's first Hello step's amount. </summary>
        public readonly int EffectAmountDelta;

        public CardStats(int attack, int health, int cost, int effectAmountDelta)
        {
            Attack            = attack;
            Health            = health;
            Cost              = cost;
            EffectAmountDelta = effectAmountDelta;
        }

        public override string ToString() => $"{Attack}/{Health} for {Cost}";
    }

    public static class CardStatsExtensions
    {
        /// <summary>
        /// The card's numbers at <paramref name="rank"/>. Cost never drops below zero and a critter never
        /// drops below one health, so a track that would take them there is clamped rather than producing a
        /// card the engine cannot represent. (Config validation already refuses such a track; the clamp is
        /// what keeps this function total.)
        /// </summary>
        public static CardStats GetStatsAtRank(this CardInfo card, int rank)
        {
            RankTrackInfo track = card.RankTrack?.Ref;
            if (track == null)
                return new CardStats(card.Attack, card.Health, card.Cost, 0);

            RankTrackStep deltas = track.GetCumulativeDeltas(rank);

            int attack = card.Attack + deltas.AttackDelta;
            int health = card.Health + deltas.HealthDelta;
            int cost   = card.Cost + deltas.CostDelta;

            if (attack < 0)
                attack = 0;
            if (card.Type == CardType.Critter && health < 1)
                health = 1;
            if (cost < 0)
                cost = 0;

            return new CardStats(attack, health, cost, deltas.AmountDelta);
        }

        /// <summary> Whether the card's numbers differ from its printed ones at <paramref name="rank"/>. </summary>
        public static bool HasRankGrowth(this CardInfo card, int rank)
        {
            CardStats stats = card.GetStatsAtRank(rank);
            return stats.Attack != card.Attack
                || stats.Health != card.Health
                || stats.Cost != card.Cost
                || stats.EffectAmountDelta != 0;
        }
    }
}
