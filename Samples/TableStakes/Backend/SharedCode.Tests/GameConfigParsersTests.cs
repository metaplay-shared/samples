using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Player;
using NUnit.Framework;
using System;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests the cell syntaxes this game adds to the config build (<see cref="GameConfigParsers"/>): a currency
    /// amount, a reward bundle and a player property id.
    /// <para>
    /// A parser that misreads a cell still produces a successful build, and the archive comparison test would
    /// agree with the wrong reading because it uses the same parser. These tests check the parsed values
    /// directly.
    /// </para>
    /// </summary>
    [TestFixture]
    public class GameConfigParsersTests
    {
        static T Parse<T>(string cell) => ConfigParser.Instance.ParseExact<T>(cell);

        static string Describe(RewardBundle bundle) =>
            string.Join(" + ", bundle.Amounts.Select(amount => $"{amount.Amount} {amount.Currency}"));

        [Test]
        public void OneCurrencyIsAnAmountAndAName()
        {
            CurrencyAmount price = Parse<CurrencyAmount>("300 Coins");

            Assert.That(price.Currency, Is.EqualTo(CurrencyType.Coins));
            Assert.That(price.Amount, Is.EqualTo(300));
        }

        [TestCase("150 Coins", "150 Coins")]
        [TestCase("1500 Coins, 100 Gems, 2 SpinTokens", "1500 Coins + 100 Gems + 2 SpinTokens")]
        // The parsed order is the order in the cell, because a reward reveal shows currencies in that order.
        [TestCase("2 SpinTokens, 150 Coins", "2 SpinTokens + 150 Coins")]
        // The comma between currencies is optional.
        [TestCase("150 Coins 1 SpinTokens", "150 Coins + 1 SpinTokens")]
        public void ARewardIsEveryCurrencyItGrants(string cell, string expected)
        {
            Assert.That(Describe(Parse<RewardBundle>(cell)), Is.EqualTo(expected));
        }

        /// <summary>
        /// A reward that grants nothing, such as the wheel's blank sector, is written as
        /// <see cref="GameConfigParsers.NothingLiteral"/>. An empty cell means the reward is missing, which reward
        /// validation refuses.
        /// </summary>
        [Test]
        public void GrantingNothingIsWrittenOut()
        {
            RewardBundle nothing = Parse<RewardBundle>(GameConfigParsers.NothingLiteral);

            Assert.That(nothing.Amounts, Is.Empty);
            Assert.That(nothing.AmountOf(CurrencyType.Coins), Is.EqualTo(0));
        }

        [TestCase("100 Rubies")]
        [TestCase("100 None")]
        public void ACurrencyThisGameDoesNotHaveIsRefused(string cell)
        {
            Assert.Throws<ParseError>(() => Parse<RewardBundle>(cell));
        }

        /// <summary>
        /// Checks that each segmentable property name, as written in a sheet's <c>PropId</c> column, parses to
        /// the class that reads it (<c>docs/offers.md</c>, "Player properties").
        /// </summary>
        [TestCase("Coins", typeof(PlayerPropertyCoins))]
        [TestCase("Gems", typeof(PlayerPropertyGems))]
        [TestCase("SpinTokens", typeof(PlayerPropertySpinTokens))]
        [TestCase("GamesPlayed", typeof(PlayerPropertyGamesPlayed))]
        [TestCase("GamesWon", typeof(PlayerPropertyGamesWon))]
        [TestCase("HasCustomizedName", typeof(PlayerPropertyHasCustomizedName))]
        [TestCase("AccountAgeDays", typeof(PlayerPropertyAccountAgeDays))]
        [TestCase("ValidatedPurchases", typeof(PlayerPropertyValidatedPurchases))]
        [TestCase("PersonalizedOffersEnabled", typeof(PlayerPropertyPersonalizedOffersEnabled))]
        public void EverySegmentablePropertyIsNamedInTheSheet(string cell, Type expected)
        {
            Assert.That(Parse<PlayerPropertyId>(cell), Is.TypeOf(expected));
        }

        /// <summary>
        /// Checks that the SDK's own property ids still parse alongside the game's.
        /// </summary>
        [Test]
        public void TheSdksOwnPropertiesStillParse()
        {
            Assert.That(Parse<PlayerPropertyId>("PlayerbaseSubset/10"), Is.Not.Null);
        }

        [Test]
        public void APropertyWithNoClassBehindItIsRefused()
        {
            ParseError error = Assert.Throws<ParseError>(() => Parse<PlayerPropertyId>("FavouriteSuit"));
            Assert.That(error.Message, Does.Contain("FavouriteSuit"));
        }
    }
}
