// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Runtime.Serialization;

namespace Game.Logic
{
    /// <summary>
    /// State for a single happy hour event for a player.
    ///
    /// Happy hours do not have complex custom behavior or state
    /// beyond that which is implemented by the MetaActivable utilities,
    /// so this does not significantly extend <see cref="MetaActivableState"/>.
    /// </summary>
    [MetaSerializableDerived(2)]
    public class HappyHourModel : MetaActivableState<HappyHourId, HappyHourInfo>
    {
        [MetaMember(1)] public sealed override HappyHourId ActivableId { get; protected set; }

        /// <summary> Just a shorthand/convenience property. </summary>
        [IgnoreDataMember] public HappyHourInfo Info => ActivableInfo;

        HappyHourModel(){ }
        public HappyHourModel(HappyHourInfo info)
            : base(info)
        {
        }
    }

    /// <summary>
    /// A player's state concerning all happy hours.
    ///
    /// Happy hours do not have complex custom behavior or state
    /// beyond that which is implemented by the MetaActivable utilities,
    /// so this does not significantly extend <see cref="MetaActivableSet{TId, TInfo, TActivableState}"/>.
    /// </summary>
    [MetaSerializableDerived(2)]
    [MetaActivableSet("HappyHour")]
    public class PlayerHappyHoursModel : MetaActivableSet<HappyHourId, HappyHourInfo, HappyHourModel>
    {
        protected override HappyHourModel CreateActivableState(HappyHourInfo info, IPlayerModelBase player)
        {
            return new HappyHourModel(info);
        }
    }
}
