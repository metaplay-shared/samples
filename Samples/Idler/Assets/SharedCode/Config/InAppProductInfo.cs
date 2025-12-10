// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Class describing the game-specific parts of an in-app purchase product.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class InAppProductInfo : InAppProductInfoBase
    {
        [MetaMember(1)] public int  NumGems { get; private set; }   // Amount of gems gained when bought
        [MetaMember(2)] public int  NumGold { get; private set; }   // Amount of gold gained when bought
    }

    /// <summary>
    /// Resolved contents of a claimed IAP, stored in purchase history
    /// </summary>
    [MetaSerializableDerived(1)]
    public class ResolvedPurchaseGameContent : ResolvedPurchaseContentBase
    {
        [MetaMember(1)] public int NumGems { get; private set; }
        [MetaMember(2)] public int NumGold { get; private set; }

        public ResolvedPurchaseGameContent(){ }
        public ResolvedPurchaseGameContent(int numGems, int numGold)
        {
            NumGems = numGems;
            NumGold = numGold;
        }
    }

    /// <summary>
    /// Records the resources revoked when a purchase is refunded.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class PurchaseRefundGameResult : InAppPurchaseRefundResult
    {
        [MetaMember(1)] public int NumGems { get; set; }
        [MetaMember(2)] public int NumGold { get; set; }
    }
}
