// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;

namespace Game.Logic
{
    // Example extensions of MetaOfferInfo and MetaOfferGroupInfo.

    [MetaSerializableDerived(1)]
    public class IdlerOfferInfo : MetaOfferInfoBase
    {
        [MetaMember(1)] public string BackgroundColor;
        [MetaMember(2)] public string TextColor;
        [MetaMember(3)] public int GemCost;

        public override bool HasInGameCurrencyCost => GemCost > 0;

        public override bool CanAffordInGameCurrencyCost(IPlayerModelBase playerBase, MetaOfferGroupInfoBase offerGroupInfo)
        {
            PlayerModel player = (PlayerModel)playerBase;
            return player.Wallet.NumGems >= GemCost;
        }

        public override void PayInGameCurrencyCost(IPlayerModelBase playerBase, MetaOfferGroupInfoBase offerGroupInfo)
        {
            PlayerModel player = (PlayerModel)playerBase;
            player.Wallet.NumGems -= GemCost;
        }

        public override string GetInGameCurrencyCostForDashboard() => $"{GemCost} gems";

        public IdlerOfferInfo(){ }
        public IdlerOfferInfo(IdlerOfferSourceConfigItem source)
            : base(source)
        {
            BackgroundColor = source.BackgroundColor;
            TextColor = source.TextColor;
            GemCost = source.GemCost;
        }
    }

    public class IdlerOfferSourceConfigItem : MetaOfferSourceConfigItemBase<IdlerOfferInfo>
    {
        public string BackgroundColor;
        public string TextColor;
        public int GemCost;

        public override IdlerOfferInfo ToConfigData(GameConfigBuildLog buildLog)
        {
            return new IdlerOfferInfo(this);
        }
    }

    [MetaSerializableDerived(1)]
    [MetaActivableConfigData("OfferGroup")]
    public class IdlerOfferGroupInfo : MetaOfferGroupInfoBase
    {
        [MetaMember(1)] public string BackgroundColor;

        public IdlerOfferGroupInfo(){ }
        public IdlerOfferGroupInfo(IdlerOfferGroupSourceConfigItem source)
            : base(source)
        {
            BackgroundColor = source.BackgroundColor;
        }
    }

    public class IdlerOfferGroupSourceConfigItem : MetaOfferGroupSourceConfigItemBase<IdlerOfferGroupInfo>
    {
        public string BackgroundColor;

        public override IdlerOfferGroupInfo ToConfigData(GameConfigBuildLog buildLog)
        {
            return new IdlerOfferGroupInfo(this);
        }
    }
}
