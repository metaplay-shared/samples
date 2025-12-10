// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;

namespace Game.Logic
{
    [MetaSerializableDerived(1)]
    [MetaActivableSet("OfferGroup")]
    public class PlayerIdlerOfferGroupsModel : PlayerMetaOfferGroupsModelBase<IdlerOfferGroupInfo>
    {
        protected override MetaOfferGroupModelBase CreateActivableState(IdlerOfferGroupInfo info, IPlayerModelBase player)
        {
            return new DefaultMetaOfferGroupModel(info);
        }

        protected override MetaOfferPerPlayerStateBase CreateOfferState(MetaOfferInfoBase offerInfo, IPlayerModelBase player)
        {
            return new DefaultMetaOfferPerPlayerState();
        }
    }
}
