// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;

namespace Game.Logic
{
    [MetaSerializableDerived(1)]
    public class IdlerPurchaseAnalyticsContext : PurchaseAnalyticsContext
    {
        [MetaMember(1)] public string Placement;
        [MetaMember(2)] public string Group;

        IdlerPurchaseAnalyticsContext() { }
        public IdlerPurchaseAnalyticsContext(string placement, string group)
        {
            Placement = placement;
            Group = group;
        }

        public override string GetDisplayStringForEventLog()
        {
            return $"Placement={Placement}, Group={Group}";
        }
    }
}
