using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>Fixed wallet limits set in code, as opposed to the limits set in config.</summary>
    public static class WalletLimits
    {
        /// <summary>
        /// The highest value a configured cap may have. It is below <see cref="int.MaxValue"/> to leave headroom
        /// for adding a grant to a capped balance: <see cref="PlayerWalletModel.Settle"/> adds in 64 bits and
        /// converts back to <c>int</c> only after the cap check. A cap above this fails the config build.
        /// </summary>
        public const int MaxSafeCap = 2_000_000_000;
    }

    /// <summary>
    /// The maximum balance of each currency, read from the published config.
    /// <para>
    /// The caps are copied out of <see cref="GlobalConfig"/> so that <see cref="PlayerWalletModel.Settle"/>
    /// depends only on a wallet, a transaction and the caps. Tests can then cover every limit without a config
    /// archive, and <see cref="From"/> is the only place that checks the config has caps.
    /// </para>
    /// </summary>
    public readonly struct WalletCaps
    {
        public int Coins      { get; }
        public int Gems       { get; }
        public int SpinTokens { get; }

        public WalletCaps(int coins, int gems, int spinTokens)
        {
            Coins      = coins;
            Gems       = gems;
            SpinTokens = spinTokens;
        }

        /// <summary>
        /// Returns the caps from <paramref name="global"/>.
        /// <para>
        /// Throws when a cap is missing or not positive instead of treating it as "no limit". This happens with
        /// an archive built before the cap entries existed: game config entries are optional, so such an archive
        /// loads with a default <see cref="GlobalConfig"/> whose caps are zero.
        /// <see cref="GameConfigValidation.ThrowIfArchiveIsOutdated"/> already refuses that archive at load, so
        /// this throw is a second check.
        /// </para>
        /// </summary>
        public static WalletCaps From(GlobalConfig global)
        {
            if (global == null)
                throw StaleConfig("has no Global entry");

            RequireCap(global.MaxCoins, nameof(GlobalConfig.MaxCoins));
            RequireCap(global.MaxGems, nameof(GlobalConfig.MaxGems));
            RequireCap(global.MaxSpinTokens, nameof(GlobalConfig.MaxSpinTokens));

            return new WalletCaps(global.MaxCoins, global.MaxGems, global.MaxSpinTokens);
        }

        static void RequireCap(int cap, string name)
        {
            if (cap <= 0)
                throw StaleConfig($"declares no {name}");
        }

        static InvalidOperationException StaleConfig(string what) =>
            new InvalidOperationException(
                $"The active game config {what}, so the wallet has no limits to settle against. "
                + "Rebuild the archive with 'dotnet run --project tools/GameConfigGen' and publish the result.");

        public int CapOf(CurrencyType currency)
        {
            switch (currency)
            {
                case CurrencyType.Coins:      return Coins;
                case CurrencyType.Gems:       return Gems;
                case CurrencyType.SpinTokens: return SpinTokens;
                default:                      return 0;
            }
        }

        public override string ToString() => $"{Coins} coins / {Gems} gems / {SpinTokens} tokens";
    }

    /// <summary>
    /// One player's currency balances (<c>docs/economy.md</c>).
    /// <para>
    /// The type is immutable, which makes every wallet change atomic: <see cref="Settle"/> computes a complete
    /// replacement wallet, and <see cref="PlayerModel.ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId)"/>
    /// applies it with a single assignment. Balances are non-negative, capped by config, and checksummed.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class PlayerWalletModel
    {
        [MetaMember(1)] public int Coins      { get; private set; }
        [MetaMember(2)] public int Gems       { get; private set; }
        [MetaMember(3)] public int SpinTokens { get; private set; }

        /// <summary>The currencies a wallet holds, in the order the HUD shows them.</summary>
        public static readonly IReadOnlyList<CurrencyType> Currencies = new[]
        {
            CurrencyType.Coins, CurrencyType.Gems, CurrencyType.SpinTokens,
        };

        public PlayerWalletModel() { }

        PlayerWalletModel(int coins, int gems, int spinTokens)
        {
            Coins      = coins;
            Gems       = gems;
            SpinTokens = spinTokens;
        }

        /// <summary>
        /// The wallet a new player is created with, from the published starting balances. Changing the starting
        /// balances in config affects only players created afterwards.
        /// </summary>
        public static PlayerWalletModel Starting(GlobalConfig global) => StartingSettlement(global).WalletAfter;

        /// <summary>
        /// Settles the starting balances as a grant into an empty wallet.
        /// <para>
        /// The starting wallet goes through <see cref="Settle"/> like any other grant, so the caps, the
        /// positive-amount rule and the one-entry-per-currency rule apply to it too.
        /// </para>
        /// </summary>
        public static WalletSettlement StartingSettlement(GlobalConfig global)
        {
            if (global?.StartingWallet == null)
                throw new InvalidOperationException(
                    "The active game config declares no starting wallet. "
                    + "Rebuild the archive with 'dotnet run --project tools/GameConfigGen' and publish the result.");

            WalletTransaction grant = WalletTransaction.Grant(EconomyFeature.Economy, EconomyReason.StartingWallet, global.StartingWallet);
            WalletSettlement  settlement = new PlayerWalletModel().Settle(grant, WalletCaps.From(global));

            if (!settlement.IsSuccess)
                throw new InvalidOperationException(
                    $"The published starting wallet cannot be granted ({settlement}). This is a config the build should have refused; "
                    + "rebuild the archive with 'dotnet run --project tools/GameConfigGen'.");

            return settlement;
        }

        /// <summary>
        /// Returns this wallet as a grant into an empty wallet: one source row per non-zero balance.
        /// <para>
        /// Used to record the starting wallet in analytics at the player's first login. It reads the player's
        /// actual balances instead of the published starting balances, because the config may have changed
        /// between account creation and the first login.
        /// </para>
        /// </summary>
        public WalletSettlement AsOpeningSettlement()
        {
            List<CurrencyAmount> openingAmounts = new List<CurrencyAmount>();
            foreach (CurrencyType currency in Currencies)
            {
                int amount = AmountOf(currency);
                if (amount > 0)
                    openingAmounts.Add(new CurrencyAmount(currency, amount));
            }

            WalletTransaction grant = WalletTransaction.Grant(EconomyFeature.Economy, EconomyReason.StartingWallet, new RewardBundle(openingAmounts.ToArray()));

            // Use the balances themselves as the caps, so the grant cannot be refused by a config cap that was
            // lowered below what the player already holds.
            return new PlayerWalletModel().Settle(grant, new WalletCaps(Coins, Gems, SpinTokens));
        }

        public int AmountOf(CurrencyType currency)
        {
            switch (currency)
            {
                case CurrencyType.Coins:      return Coins;
                case CurrencyType.Gems:       return Gems;
                case CurrencyType.SpinTokens: return SpinTokens;
                default:                      return 0;
            }
        }

        /// <summary>Whether the player can pay a single-currency price. Use <see cref="Settle"/> for a multi-currency price.</summary>
        public bool CanAfford(CurrencyAmount price) => price != null && AmountOf(price.Currency) >= price.Amount;

        /// <summary>Returns a copy with one balance replaced. Private because only <see cref="Settle"/> sets balances.</summary>
        PlayerWalletModel With(CurrencyType currency, int amount)
        {
            switch (currency)
            {
                case CurrencyType.Coins:      return new PlayerWalletModel(amount, Gems, SpinTokens);
                case CurrencyType.Gems:       return new PlayerWalletModel(Coins, amount, SpinTokens);
                case CurrencyType.SpinTokens: return new PlayerWalletModel(Coins, Gems, amount);
                default:                      return this;
            }
        }

        /// <summary>
        /// Computes the result of <paramref name="transaction"/> without changing this wallet.
        /// <para>
        /// Spends are applied before grants, so affordability is checked against the player's current balance,
        /// not a balance that includes the grant. A spin that wins back a token therefore records the token
        /// being spent and then granted. If any currency is short or any grant would exceed its cap, the whole
        /// transaction is refused and no wallet is returned. A <see cref="WalletTransaction.PaidGrant"/> ignores the
        /// caps and is refused only when a balance would not fit in an <c>int</c>.
        /// </para>
        /// </summary>
        public WalletSettlement Settle(WalletTransaction transaction, WalletCaps caps)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            if (!transaction.IsWellFormed)
                return WalletSettlement.Refused(transaction, WalletRefusal.Malformed, this, CurrencyType.None, 0, 0);

            PlayerWalletModel          runningWallet = this;
            List<WalletTransactionRow> rows          = new List<WalletTransactionRow>();

            foreach (CurrencyAmount spend in transaction.Spends)
            {
                int balanceBefore = runningWallet.AmountOf(spend.Currency);
                if (balanceBefore < spend.Amount)
                    return WalletSettlement.Refused(transaction, WalletRefusal.InsufficientFunds, this, spend.Currency, spend.Amount, balanceBefore);

                int balanceAfter = balanceBefore - spend.Amount;
                rows.Add(new WalletTransactionRow(spend.Currency, CurrencyFlow.Sink, spend.Amount, balanceBefore, balanceAfter));
                runningWallet = runningWallet.With(spend.Currency, balanceAfter);
            }

            foreach (CurrencyAmount grant in transaction.Grants)
            {
                int  balanceBefore = runningWallet.AmountOf(grant.Currency);
                long balanceAfter  = (long)balanceBefore + grant.Amount;

                long limit = transaction.MayExceedCaps ? int.MaxValue : caps.CapOf(grant.Currency);
                if (balanceAfter > limit)
                    return WalletSettlement.Refused(transaction, WalletRefusal.CapExceeded, this, grant.Currency, grant.Amount, balanceBefore);

                rows.Add(new WalletTransactionRow(grant.Currency, CurrencyFlow.Source, grant.Amount, balanceBefore, (int)balanceAfter));
                runningWallet = runningWallet.With(grant.Currency, (int)balanceAfter);
            }

            return WalletSettlement.Settled(transaction, this, runningWallet, rows);
        }

        public override string ToString() => $"{Coins} coins, {Gems} gems, {SpinTokens} spin tokens";
    }
}
