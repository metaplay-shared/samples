// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;

namespace Game.Logic
{
    // Example extensions of MetaOfferInfo and MetaOfferGroupInfo.

    [MetaSerializableDerived(1)]
    public class IdlerOfferInfo : MetaOfferInfoBase
    {
        [MetaMember(1)] public string BackgroundColor;
        [MetaMember(2)] public string TextColor;

        public IdlerOfferInfo(){ }
        public IdlerOfferInfo(IdlerOfferSourceConfigItem source)
            : base(source)
        {
            BackgroundColor = source.BackgroundColor;
            TextColor = source.TextColor;
        }
    }

    public class IdlerOfferSourceConfigItem : MetaOfferSourceConfigItemBase<IdlerOfferInfo>
    {
        public string BackgroundColor;
        public string TextColor;

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
