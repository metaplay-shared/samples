using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// The currencies of the meta economy (<c>docs/economy.md</c>).
    /// <para>
    /// Currencies are an enum rather than config because adding one changes every feature and every stored
    /// player wallet, so it must not happen through a config publish. Config sets the amounts.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum CurrencyType
    {
        None = 0,

        /// <summary>The everyday earned currency: the default reward and the main cosmetic price.</summary>
        Coins = 1,

        /// <summary>The premium currency, spent on premium cosmetics and some offers.</summary>
        Gems = 2,

        /// <summary>One token buys one wheel spin. Spin tokens have no other use.</summary>
        SpinTokens = 3,
    }

    /// <summary>
    /// An amount of one currency. Game config writes every price in this type.
    /// </summary>
    [MetaSerializable]
    public class CurrencyAmount
    {
        [MetaMember(1)] public CurrencyType Currency { get; private set; }
        [MetaMember(2)] public int          Amount   { get; private set; }

        public CurrencyAmount() { }

        public CurrencyAmount(CurrencyType currency, int amount)
        {
            Currency = currency;
            Amount   = amount;
        }

        public static CurrencyAmount Coins(int amount)      => new CurrencyAmount(CurrencyType.Coins, amount);
        public static CurrencyAmount Gems(int amount)       => new CurrencyAmount(CurrencyType.Gems, amount);
        public static CurrencyAmount SpinTokens(int amount) => new CurrencyAmount(CurrencyType.SpinTokens, amount);

        public override string ToString() => $"{Amount} {Currency}";
    }

    /// <summary>
    /// A reward of one or more currencies, granted together. Every feature's config uses this type for its
    /// rewards, so all rewards share one validator and one reward reveal.
    /// <para>
    /// The contents are read-only. Config items are shared by all players on the server and the SDK detects a
    /// mutated config item, so a bundle must not change after the config build.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class RewardBundle
    {
        [MetaMember(1)] List<CurrencyAmount> _amounts;

        /// <summary>The currencies this bundle grants, at most one entry per currency.</summary>
        public IReadOnlyList<CurrencyAmount> Amounts => _amounts;

        public RewardBundle()
        {
            _amounts = new List<CurrencyAmount>();
        }

        public RewardBundle(params CurrencyAmount[] amounts)
        {
            _amounts = amounts.ToList();
        }

        /// <summary>How much of <paramref name="currency"/> this bundle grants, or zero.</summary>
        public int AmountOf(CurrencyType currency)
        {
            foreach (CurrencyAmount amount in _amounts)
            {
                if (amount.Currency == currency)
                    return amount.Amount;
            }
            return 0;
        }

        public bool Grants(CurrencyType currency) => AmountOf(currency) > 0;

        /// <summary>
        /// Validates a reward: it grants at least one currency, names each currency at most once, and every
        /// amount is positive.
        /// </summary>
        public void Validate(ConfigItemValidation validation, string memberHint)
        {
            if (_amounts == null || _amounts.Count == 0)
            {
                validation.Error("must grant something", memberHint);
                return;
            }

            HashSet<CurrencyType> seen = new HashSet<CurrencyType>();
            foreach (CurrencyAmount amount in _amounts)
            {
                if (amount.Currency == CurrencyType.None)
                    validation.Error("names no currency", memberHint);
                else if (!seen.Add(amount.Currency))
                    validation.Error($"names {amount.Currency} twice", memberHint);

                validation.RequirePositive(amount.Amount, memberHint);
            }
        }

        public override string ToString() => _amounts == null ? "(empty)" : string.Join(" + ", _amounts);
    }
}
