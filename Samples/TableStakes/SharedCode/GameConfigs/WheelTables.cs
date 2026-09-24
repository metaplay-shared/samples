using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// Identifies one published version of the wheel's prize table. A spin receipt names the table it was
    /// resolved against, so an old result can still be explained after a new table is published.
    /// </summary>
    [MetaSerializable]
    public class WheelTableId : StringId<WheelTableId> { }

    /// <summary>
    /// Identifies one sector of a published table, independently of its drawn position.
    /// <para>
    /// A resolved spin is recorded and reported under this id. The drawn position
    /// (<see cref="WheelSectorInfo.Sector"/>) can differ between tables, so analytics joins on this id. The
    /// receipt and the analytics event carry both (<c>docs/spin-wheel.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class WheelSectorId : StringId<WheelSectorId> { }

    /// <summary>
    /// How strongly the client celebrates a sector's result. Presentation only: no game logic reads it to
    /// decide what a spin pays.
    /// <para>
    /// An enum rather than a string because the client switches on it. A string tier with no client handling
    /// would show no celebration and report no error.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum WheelPrizeTier
    {
        None = 0,

        /// <summary>The everyday coin result.</summary>
        Common = 1,

        /// <summary>A better coin result.</summary>
        Uncommon = 2,

        /// <summary>The top coin result.</summary>
        Rare = 3,

        /// <summary>The gem result.</summary>
        Premium = 4,

        /// <summary>One spin token, which replaces the token the spin cost.</summary>
        SpinAgain = 5,

        /// <summary>The blank sector. The spin token is spent and nothing is granted. The odds sheet lists it like any other result.</summary>
        Nothing = 6,
    }

    /// <summary>One sector of a published wheel.</summary>
    [MetaSerializable]
    public class WheelSectorInfo
    {
        [MetaMember(1)] public WheelSectorId Id { get; private set; }

        /// <summary>The drawn position of this sector, 1 to <see cref="WheelTableInfo.NumSectors"/>, clockwise from the marker.</summary>
        [MetaMember(2)] public int Sector { get; private set; }

        /// <summary>The reward for landing on this sector. Exactly one currency, or empty for the blank sector (<see cref="WheelTableInfo.Validate"/>).</summary>
        [MetaMember(3)] public RewardBundle Reward { get; private set; }

        [MetaMember(4)] public WheelPrizeTier Tier { get; private set; }

        public WheelSectorInfo() { }

        public WheelSectorInfo(WheelSectorId id, int sector, RewardBundle reward, WheelPrizeTier tier)
        {
            Id     = id;
            Sector = sector;
            Reward = reward;
            Tier   = tier;
        }

        /// <summary>
        /// The currency this sector grants, or <see cref="CurrencyType.None"/> if <see cref="Reward"/> does not
        /// hold exactly one amount (the blank sector, or a malformed sector).
        /// </summary>
        public CurrencyType Currency =>
            Reward?.Amounts != null && Reward.Amounts.Count == 1 ? Reward.Amounts[0].Currency : CurrencyType.None;

        /// <summary>The amount of <see cref="Currency"/> this sector grants, or zero when <see cref="Currency"/> is <see cref="CurrencyType.None"/>.</summary>
        public int Amount =>
            Reward?.Amounts != null && Reward.Amounts.Count == 1 ? Reward.Amounts[0].Amount : 0;

        /// <summary>Whether this is the blank sector (<see cref="WheelPrizeTier.Nothing"/>).</summary>
        public bool IsBlank => Tier == WheelPrizeTier.Nothing;

        public override string ToString() => $"{Id} (sector {Sector}): {Reward}";
    }

    /// <summary>
    /// One distinct result of the wheel and its probability, combined across all sectors that pay it.
    /// <para>
    /// The odds sheet shows these. They are computed from the sectors rather than written in config, so the
    /// odds shown always match the wheel (<c>docs/spin-wheel.md</c>).
    /// </para>
    /// </summary>
    public readonly struct WheelOddsEntry
    {
        public CurrencyType Currency { get; }
        public int          Amount   { get; }

        /// <summary>The number of sectors that pay exactly this result.</summary>
        public int SectorCount { get; }

        /// <summary>The probability of this result, in whole percent.</summary>
        public int ChancePercent { get; }

        internal WheelOddsEntry(CurrencyType currency, int amount, int sectors, int chancePercent)
        {
            Currency      = currency;
            Amount        = amount;
            SectorCount   = sectors;
            ChancePercent = chancePercent;
        }

        public override string ToString() => $"{Amount} {Currency}: {ChancePercent}%";
    }

    /// <summary>
    /// One version of the spin wheel's prize table (<c>docs/spin-wheel.md</c>).
    /// <para>
    /// All sectors are equally likely. There is no weight column, because weights could disagree with the drawn
    /// wheel. The odds shown to the player come from <see cref="Odds"/>, which counts sectors.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class WheelTableInfo : IGameConfigData<WheelTableId>, IValidatedConfigItem
    {
        /// <summary>The number of sectors. The client's wheel art has this many sectors, so it is not configurable.</summary>
        public const int NumSectors = 10;

        /// <summary>The allowed coin reward range for one sector.</summary>
        public const int MinCoinPrize = 100;
        public const int MaxCoinPrize = 1000;

        /// <summary>The allowed gem reward range for one sector.</summary>
        public const int MinGemPrize = 10;
        public const int MaxGemPrize = 50;

        /// <summary>The allowed range for the expected coins per spin.</summary>
        public const int MinExpectedCoins = 250;
        public const int MaxExpectedCoins = 350;

        /// <summary>The allowed range for the expected gems per spin.</summary>
        public const int MinExpectedGems = 1;
        public const int MaxExpectedGems = 5;

        [MetaMember(1)] public WheelTableId Id { get; private set; }

        // MetaMember id 2 is retired. Do not reuse it.
        [MetaMember(3)] List<WheelSectorInfo> _sectors;

        /// <summary>The sectors, clockwise from the marker.</summary>
        public IReadOnlyList<WheelSectorInfo> Sectors => _sectors;

        public WheelTableId ConfigKey => Id;

        public WheelTableInfo() { }

        /// <summary>Built from <c>GameConfigSource/WheelTables.csv</c>, one row per sector, clockwise from the marker.</summary>
        [MetaGameConfigBuildConstructor]
        public WheelTableInfo(WheelTableId id, List<WheelSectorInfo> sectors)
        {
            Id       = id;
            _sectors = sectors.ToList();
        }

        /// <summary>Whether this table has exactly <see cref="NumSectors"/> sectors. The config build refuses a table that does not.</summary>
        public bool IsComplete => _sectors != null && _sectors.Count == NumSectors;

        /// <summary>
        /// The sector at zero-based <paramref name="index"/> in drawn order, or null if the index is out of range.
        /// The caller must refuse the spin rather than pay a guessed reward.
        /// </summary>
        public WheelSectorInfo SectorAt(int index)
        {
            if (_sectors == null || index < 0 || index >= _sectors.Count)
                return null;
            return _sectors[index];
        }

        /// <summary>The zero-based index of the sector with id <paramref name="id"/>, or -1 if this table has none.</summary>
        public int IndexOf(WheelSectorId id)
        {
            if (_sectors == null || id == null)
                return -1;

            for (int index = 0; index < _sectors.Count; index++)
            {
                if (_sectors[index]?.Id == id)
                    return index;
            }
            return -1;
        }

        /// <summary>
        /// The probability, in whole percent, that a spin grants any amount of <paramref name="currency"/>.
        /// Unlike <see cref="Odds"/>, this combines all amounts of the currency.
        /// </summary>
        public int ChancePercentOf(CurrencyType currency)
        {
            if (_sectors == null || _sectors.Count == 0)
                return 0;
            return _sectors.Count(sector => sector?.Reward != null && sector.Reward.Grants(currency)) * 100 / _sectors.Count;
        }

        /// <summary>
        /// The table's distinct results, in the order the odds sheet lists them: by currency in
        /// <see cref="PlayerWalletModel.Currencies"/> order, by ascending amount within a currency, and the
        /// blank last.
        /// <para>
        /// The order does not depend on sector order, so two tables with the same contents produce the same
        /// sheet. The blank sector is included like any other result.
        /// </para>
        /// </summary>
        public IReadOnlyList<WheelOddsEntry> Odds()
        {
            List<WheelOddsEntry> odds = new List<WheelOddsEntry>();
            if (_sectors == null || _sectors.Count == 0)
                return odds;

            Dictionary<CurrencyType, Dictionary<int, int>> counts = new Dictionary<CurrencyType, Dictionary<int, int>>();
            int blankSectors = 0;
            foreach (WheelSectorInfo sector in _sectors)
            {
                if (sector == null)
                    continue;

                if (sector.IsBlank)
                {
                    blankSectors++;
                    continue;
                }

                if (sector.Currency == CurrencyType.None)
                    continue;

                if (!counts.TryGetValue(sector.Currency, out Dictionary<int, int> byAmount))
                {
                    byAmount = new Dictionary<int, int>();
                    counts[sector.Currency] = byAmount;
                }

                byAmount[sector.Amount] = byAmount.TryGetValue(sector.Amount, out int seen) ? seen + 1 : 1;
            }

            foreach (CurrencyType currency in PlayerWalletModel.Currencies)
            {
                if (!counts.TryGetValue(currency, out Dictionary<int, int> byAmount))
                    continue;

                foreach (int amount in byAmount.Keys.OrderBy(value => value).ToList())
                    odds.Add(new WheelOddsEntry(currency, amount, byAmount[amount], byAmount[amount] * 100 / _sectors.Count));
            }

            if (blankSectors > 0)
                odds.Add(new WheelOddsEntry(CurrencyType.None, 0, blankSectors, blankSectors * 100 / _sectors.Count));

            return odds;
        }

        /// <summary>
        /// The average amount of <paramref name="currency"/> a spin grants, in hundredths of a unit. Hundredths
        /// keep enough precision that the range checks in <see cref="Validate"/> are not passed by rounding.
        /// </summary>
        public int ExpectedHundredthsOf(CurrencyType currency)
        {
            if (_sectors == null || _sectors.Count == 0)
                return 0;

            int total = _sectors.Sum(sector => sector?.Reward?.AmountOf(currency) ?? 0);
            return total * 100 / _sectors.Count;
        }

        public void Validate(ConfigItemValidation validation)
        {
            // Odds are shown in whole percent, so NumSectors must divide 100 for the odds to sum to 100%.
            validation.Require(100 % NumSectors == 0, $"is drawn in {NumSectors} sectors, which cannot be expressed in whole percent", nameof(Sectors));

            validation.RequireCount(_sectors, NumSectors, nameof(Sectors));
            if (_sectors == null)
                return;

            HashSet<WheelSectorId> ids       = new HashSet<WheelSectorId>();
            HashSet<int>            positions = new HashSet<int>();
            int                     tokenSectors = 0;
            int                     blankSectors = 0;
            int                     rareSectors  = 0;
            int                     maxCoinPrize = 0;
            int                     rareCoinPrize = 0;

            for (int index = 0; index < _sectors.Count; index++)
            {
                string           hint   = $"{nameof(Sectors)}[{index}]";
                WheelSectorInfo sector = _sectors[index];

                if (sector == null)
                {
                    validation.Error("is missing", hint);
                    continue;
                }

                // Spins are recorded by sector id, so a duplicate id would make two sectors indistinguishable.
                // Each position from 1 to NumSectors must appear exactly once, in drawn order. A gap would leave a
                // drawn sector with no row in the table.
                validation.RequireNumberedRow(sector.Id, sector.Sector, index, NumSectors, ids, positions, "sector", "the table is authored in drawn order", hint);

                if (sector.Reward == null)
                {
                    validation.Error("grants nothing", hint);
                    continue;
                }

                // Only a Nothing-tier sector may have an empty reward, and it must be empty. For every other tier,
                // RewardBundle.Validate refuses an empty reward.
                if (sector.IsBlank)
                {
                    blankSectors++;
                    validation.Require(sector.Reward.Amounts.Count == 0, "is drawn as the blank but grants a reward; a blank sector grants nothing", hint);
                    continue;
                }

                sector.Reward.Validate(validation, hint);

                // A sector pays exactly one currency. The wheel draws one prize per sector, and Odds groups
                // results by one currency and amount.
                int currencyCount = sector.Reward.Amounts == null ? 0 : sector.Reward.Amounts.Count;
                if (currencyCount != 1)
                {
                    validation.Error($"grants {currencyCount} currencies; a sector pays exactly one", hint);
                    continue;
                }

                int coins  = sector.Reward.AmountOf(CurrencyType.Coins);
                int gems   = sector.Reward.AmountOf(CurrencyType.Gems);
                int tokens = sector.Reward.AmountOf(CurrencyType.SpinTokens);

                if (coins > 0)
                {
                    validation.Require(coins >= MinCoinPrize && coins <= MaxCoinPrize,
                        $"pays {coins} coins, outside the {MinCoinPrize}-{MaxCoinPrize} band the economy is balanced against", hint);

                    if (sector.Tier == WheelPrizeTier.Rare)
                    {
                        rareSectors++;
                        rareCoinPrize = coins;
                    }

                    if (coins > maxCoinPrize)
                        maxCoinPrize = coins;

                    validation.Require(
                        sector.Tier == WheelPrizeTier.Common || sector.Tier == WheelPrizeTier.Uncommon || sector.Tier == WheelPrizeTier.Rare,
                        $"pays coins but is drawn as {sector.Tier}", hint);
                }
                else if (gems > 0)
                {
                    validation.Require(gems >= MinGemPrize && gems <= MaxGemPrize,
                        $"pays {gems} gems, outside the {MinGemPrize}-{MaxGemPrize} band the economy is balanced against", hint);
                    validation.Require(sector.Tier == WheelPrizeTier.Premium, $"pays gems but is drawn as {sector.Tier}", hint);
                }
                else if (tokens > 0)
                {
                    tokenSectors++;

                    // A spin-again sector pays exactly one token, which replaces the token the spin cost. More
                    // than one would let the wheel produce spin tokens.
                    validation.Require(tokens == 1, $"pays {tokens} spin tokens; the replacement sector pays exactly one", hint);
                    validation.Require(sector.Tier == WheelPrizeTier.SpinAgain, $"pays a spin token but is drawn as {sector.Tier}", hint);
                }
                else
                {
                    validation.Error($"pays {sector.Reward}, which is not coins, gems or a spin token", hint);
                }
            }

            // The wheel design fixes the number of spin-again, blank and jackpot (Rare) sectors, and the client's
            // wheel screen describes the wheel with those numbers. The jackpot must be the largest coin prize.
            validation.Require(tokenSectors == 2, $"has {tokenSectors} spin-again sectors; the wheel has exactly two", nameof(Sectors));
            validation.Require(blankSectors == 1, $"has {blankSectors} blank sectors; the wheel has exactly one", nameof(Sectors));
            validation.Require(rareSectors == 1, $"has {rareSectors} jackpot sectors; the wheel has exactly one", nameof(Sectors));
            validation.Require(rareCoinPrize == maxCoinPrize, $"pays its jackpot of {rareCoinPrize} coins beside a larger coin prize of {maxCoinPrize}; the jackpot is the largest coin prize", nameof(Sectors));

            if (_sectors.Count != NumSectors)
                return;

            // A retune may reorder sectors or change prizes, but the expected coins and gems per spin must stay
            // in the ranges the economy is balanced against.
            int expectedCoinHundredths = ExpectedHundredthsOf(CurrencyType.Coins);
            validation.Require(
                expectedCoinHundredths >= MinExpectedCoins * 100 && expectedCoinHundredths <= MaxExpectedCoins * 100,
                $"pays {expectedCoinHundredths} hundredths of a coin a spin on average, outside the {MinExpectedCoins}-{MaxExpectedCoins} coins the economy is balanced against",
                nameof(Sectors));

            int expectedGemHundredths = ExpectedHundredthsOf(CurrencyType.Gems);
            validation.Require(
                expectedGemHundredths >= MinExpectedGems * 100 && expectedGemHundredths <= MaxExpectedGems * 100,
                $"pays {expectedGemHundredths} hundredths of a gem a spin on average, outside the {MinExpectedGems}-{MaxExpectedGems} gems the economy is balanced against",
                nameof(Sectors));

            // The odds shown on the odds sheet must sum to 100%. With valid sectors this cannot fail. It catches
            // a bug in how Odds combines sectors.
            int totalChancePercent = Odds().Sum(entry => entry.ChancePercent);
            validation.Require(totalChancePercent == 100, $"publishes odds summing to {totalChancePercent}%", nameof(Sectors));
        }

        public override string ToString() => Id?.Value ?? "(no wheel table)";
    }
}
