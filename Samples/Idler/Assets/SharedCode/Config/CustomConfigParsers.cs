// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Player;

namespace Game.Logic
{
    public class ConfigParsers : ConfigParserProvider
    {
        public override void RegisterParsers(ConfigParser parser)
        {
            parser.RegisterCustomParseFunc<PlayerReward>(ParsePlayerReward);
            parser.RegisterCustomParseFunc<PlayerPropertyId>(ParsePlayerPropertyId);
        }

        static PlayerReward ParsePlayerReward(ConfigParser parser, ConfigLexer lexer)
        {
            int     amount      = lexer.ParseIntegerLiteral();
            string  rewardType  = lexer.ParseIdentifier();

            switch (rewardType)
            {
                case "Gems":
                    return new RewardGems(amount);

                case "Gold":
                    return new RewardGold(amount);

                case "Producer":
                {
                    lexer.ParseToken(ConfigLexer.TokenType.ForwardSlash);
                    ProducerTypeId producerTypeId = parser.Parse<ProducerTypeId>(lexer);
                    return new RewardProducer(producerTypeId, amount);
                }

                // \todo [nuutti] Implement others

                default:
                    throw new ParseError($"Unhandled PlayerReward type in config: {rewardType}");
            }
        }

        static PlayerPropertyId ParsePlayerPropertyId(ConfigParser parser, ConfigLexer lexer)
        {
            if (ConfigParser.TryParseCorePlayerPropertyId(lexer, out PlayerPropertyId propertyId))
                return propertyId;

            string type = lexer.ParseIdentifier();

            switch (type)
            {
                case "Gems":
                    return new PlayerPropertyIdGems();

                case "Gold":
                    return new PlayerPropertyIdGold();

                case "Producer":
                {
                    lexer.ParseToken(ConfigLexer.TokenType.ForwardSlash);
                    MetaRef<ProducerInfo> producer = parser.Parse<MetaRef<ProducerInfo>>(lexer);

                    return new PlayerPropertyIdProducerLevel(producer);
                }

                case "LastKnownCountry":
                    return new PlayerPropertyLastKnownCountry();

                case "AccountCreatedAt":
                    return new PlayerPropertyAccountCreatedAt();

                case "AccountAge":
                    return new PlayerPropertyAccountAge();

                case "TimeSinceLastLogin":
                    return new PlayerPropertyTimeSinceLastLogin();
            }

            throw new ParseError($"Invalid PlayerPropertyId in config: {type}");
        }
    }
}
