using Metaplay.Core.Offers;

namespace Game.Logic
{
    /// <summary>
    /// The Shop's offer placement IDs (<c>docs/offers.md</c>): the single Featured slot and the catalogue.
    /// They are defined in shared code, not only in the config sheets, because the client must tell the two
    /// placements apart to show the Featured card and the catalogue. The client reads the SDK's resolved offer
    /// state and never evaluates an offer's targeting itself.
    /// </summary>
    public static class OfferPlacementIds
    {
        public static readonly OfferPlacementId ShopFeatured  = OfferPlacementId.FromString("shop_featured");
        public static readonly OfferPlacementId ShopCatalogue = OfferPlacementId.FromString("shop_catalogue");
    }
}
