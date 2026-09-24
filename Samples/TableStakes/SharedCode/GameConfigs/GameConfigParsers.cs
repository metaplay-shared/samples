using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Registers single-cell parsers for this game's value types in game config sheets.
    /// <para>
    /// Without a registered parse function, the SDK needs one column per field. A single cell lets a designer
    /// write a reward as <c>1500 Coins, 50 Gems</c>. The SDK's source generator finds every concrete
    /// <see cref="ConfigParserProvider"/>, so this class needs no registration.
    /// </para>
    /// </summary>
    public class GameConfigParsers : ConfigParserProvider
    {
        public override void RegisterParsers(ConfigParser parser)
        {
            parser.RegisterCustomParseFunc<CurrencyAmount>(ParseCurrencyAmount);
            parser.RegisterCustomParseFunc<RewardBundle>(ParseRewardBundle);
            parser.RegisterCustomParseFunc<PlayerPropertyId>(ParsePlayerPropertyId);
        }

        /// <summary>The cell value for a reward that grants nothing. A blank cell means the value is missing.</summary>
        public const string NothingLiteral = "none";

        /// <summary>
        /// Parses one amount of one currency, written amount first: <c>300 Coins</c>. The game's reward reveals
        /// use the same order.
        /// </summary>
        static object ParseCurrencyAmount(ConfigLexer lexer) => ParseAmount(lexer);

        /// <summary>
        /// Parses a reward bundle: <c>1500 Coins, 50 Gems</c>, or <see cref="NothingLiteral"/> for an empty bundle.
        /// <para>
        /// A blank cell does not reach this parser. It leaves the member unset, and each reward's validation
        /// refuses an unset reward. A forgotten cell therefore fails the config build, and an intentionally empty
        /// reward must be written as <see cref="NothingLiteral"/>.
        /// </para>
        /// </summary>
        static object ParseRewardBundle(ConfigLexer lexer)
        {
            if (lexer.CurrentToken.Type == ConfigLexer.TokenType.Identifier
                && lexer.GetTokenString(lexer.CurrentToken) == NothingLiteral)
            {
                lexer.Advance();
                return new RewardBundle();
            }

            List<CurrencyAmount> amounts = new List<CurrencyAmount>();
            while (true)
            {
                amounts.Add(ParseAmount(lexer));

                // The comma between amounts is optional. Each amount ends with a currency name, so the amounts
                // parse the same with or without it.
                _ = lexer.TryParseToken(ConfigLexer.TokenType.Comma);

                if (lexer.IsAtEnd)
                    break;
            }

            return new RewardBundle(amounts.ToArray());
        }

        static CurrencyAmount ParseAmount(ConfigLexer lexer)
        {
            int    amount   = lexer.ParseIntegerLiteral();
            string currency = lexer.ParseIdentifier();

            if (!Enum.TryParse(currency, out CurrencyType parsed) || parsed == CurrencyType.None)
                throw new ParseError($"'{currency}' is not a currency this game has. The currencies are {string.Join(", ", EnumUtil.GetValues<CurrencyType>())}.");

            return new CurrencyAmount(parsed, amount);
        }

        /// <summary>
        /// Parses a player property name from the <c>PropId</c> column of a segment sheet (<c>docs/offers.md</c>,
        /// "Player properties").
        /// <para>
        /// SDK properties are parsed by <see cref="ConfigParser.TryParseCorePlayerPropertyId"/>. Each game property
        /// needs a case here that maps its name to the class that reads the value. An unknown name throws, which
        /// fails the config build instead of producing a segment that matches no player.
        /// </para>
        /// </summary>
        static object ParsePlayerPropertyId(ConfigLexer lexer)
        {
            if (ConfigParser.TryParseCorePlayerPropertyId(lexer, out PlayerPropertyId corePropertyId))
                return corePropertyId;

            string name = lexer.ParseIdentifier();
            switch (name)
            {
                case "Coins":                     return new PlayerPropertyCoins();
                case "Gems":                      return new PlayerPropertyGems();
                case "SpinTokens":                return new PlayerPropertySpinTokens();
                case "GamesPlayed":               return new PlayerPropertyGamesPlayed();
                case "GamesWon":                  return new PlayerPropertyGamesWon();
                case "HasCustomizedName":         return new PlayerPropertyHasCustomizedName();
                case "AccountAgeDays":            return new PlayerPropertyAccountAgeDays();
                case "ValidatedPurchases":        return new PlayerPropertyValidatedPurchases();
                case "PersonalizedOffersEnabled": return new PlayerPropertyPersonalizedOffersEnabled();

                default:
                    throw new ParseError($"'{name}' is not a player property this game can segment on. See {nameof(GameConfigParsers)}.");
            }
        }
    }
}
