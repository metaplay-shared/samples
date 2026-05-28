// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Game.Logic.TypeCodes
{
    public static class ActionCodes
    {
        public const int PlayerForceDesync                                      = 1900;
        public const int PlayerGainGemsDebug                                    = 1901;
        public const int PlayerGainGoldDebug                                    = 1902;
        public const int PlayerDuplicateEventLogEvents                          = 1903;
        public const int PlayerSetBloatTestSize                                 = 1904;
        public const int AdminActionSetBloatTestSize                            = 1905;

        public const int PlayerUnlockProducer                                   = 1100;
        public const int PlayerUpgradeProducer                                  = 1101;

        public const int PlayerClaimSpecialProducerEventRewards                 = 1210;

        public const int AdminUnlockProducer                                    = 1300;
        public const int AdminSetWallet                                         = 1301;

        public const int GuildPokeMember                                        = 2100;

        public const int GuildSellPokesFinalizingPlayerAction                   = 2202;
        public const int GuildSellPokesFinalizingGuildAction                    = 2203;

        public const int GuildBuyVanityInitiatingPlayerAction                   = 2204;
        public const int GuildBuyVanityCancelingPlayerAction                    = 2205;
        public const int GuildBuyVanityFinalizingGuildAction                    = 2206;

        public const int GuildClaimVanityRankRewardInitiatingPlayerAction       = 2207;
        public const int GuildClaimVanityRankRewardFinalizingPlayerAction       = 2208;
        public const int GuildClaimVanityRankRewardFinalizingGuildAction        = 2209;

        public const int GuildSetRequiredPlayerLevel                            = 2210;

        public const int PlayerCreateParty                                      = 2300;
        public const int PlayerJoinParty                                        = 2301;
    }
    public static class TransactionPlanCodes
    {
        // \todo: could be made automatic
        public const int GuildSellPokesGuildPlan                    = 1002;
        public const int GuildSellPokesFinalizingPlan               = 1003;

        public const int GuildBuyVanityPlayerPlan                   = 1011;

        public const int GuildClaimVanityRankRewardGuildPlan        = 1022;
        public const int GuildClaimVanityRankRewardFinalizingPlan   = 1023;
    }
    public static class TransactionCodes
    {
        public const int GuildSellPokes             = 101;
        public const int GuildBuyVanity             = 102;
        public const int GuildClaimVanityRankReward = 103;
    }
}
