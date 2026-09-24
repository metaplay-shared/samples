using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;
using System.Text;
using static System.FormattableString;

namespace Game.Logic
{
    /// <summary>
    /// One demo in-app purchase product in the Shop: a bundle of wallet currency bought through the SDK's in-app
    /// purchase flow on the <b>Development</b> platform, the SDK's fake store (<c>docs/offers.md</c>). The server
    /// accepts its receipts only when <c>Environment:EnableDevelopmentFeatures</c> is on. The product is
    /// <see cref="InAppProductType.Consumable"/>, so the SDK refuses a reused receipt. The limit of one purchase
    /// per player is enforced by <see cref="PlayerModel.OnClaimedInAppProduct"/> for a standalone
    /// product, and by the offer's <see cref="MetaOfferInfoBase.MaxPurchasesPerPlayer"/> for a product used by
    /// <see cref="OfferInfo.Demo"/> (<see cref="HasDynamicContent"/> true).
    /// </summary>
    [MetaSerializableDerived(101)]
    public class DemoInAppProductInfo : InAppProductInfoBase, IValidatedConfigItem
    {
        /// <summary>
        /// What a standalone product grants. Null for a product used by <see cref="OfferInfo.Demo"/>, whose
        /// <see cref="DemoOfferReward"/> grants the reward through the SDK's dynamic-content claim path.
        /// </summary>
        [MetaMember(1)] public RewardBundle Contents { get; private set; }

        /// <summary>The line shown under the title on a standalone product's Shop tile.</summary>
        [MetaMember(2)] public string Tagline { get; private set; }

        public DemoInAppProductInfo() { }

        /// <summary>Creates a standalone product with fixed contents, granted by <see cref="PlayerModel.OnClaimedInAppProduct"/>.</summary>
        public DemoInAppProductInfo(InAppProductId productId, string name, string tagline, double referencePriceUsd, RewardBundle contents)
            : this(productId, name, tagline, referencePriceUsd, contents, hasDynamicContent: false)
        {
        }

        /// <summary>Creates a product for <see cref="OfferInfo.Demo"/>, with no contents. The offer's reward supplies them.</summary>
        public static DemoInAppProductInfo ForOffer(InAppProductId productId, string name, string tagline, double referencePriceUsd) =>
            new DemoInAppProductInfo(productId, name, tagline, referencePriceUsd, contents: null, hasDynamicContent: true);

        /// <summary>
        /// Builds a product from a <c>GameConfigSource/InAppProducts.csv</c> row. The sheet sets the ID, name,
        /// tagline and reference price. The rest is fixed here: the development store ID is the product ID with a
        /// <c>dev.</c> prefix, the type is always consumable, and no real store ID is set, so the product can
        /// never be bought with real money (<c>docs/offers.md</c>).
        /// </summary>
        [MetaGameConfigBuildConstructor]
        DemoInAppProductInfo(InAppProductId productId, string name, string tagline, double referencePriceUsd, RewardBundle contents = null, bool hasDynamicContent = false)
            : base(
                productId:                     productId,
                name:                          name,
                type:                          InAppProductType.Consumable,
                price:                         F64.FromDouble(referencePriceUsd),
                hasDynamicContent:             hasDynamicContent,
                developmentId:                 productId == null ? null : "dev." + productId.Value,
                googleId:                      null,
                appleId:                       null,
                steamId:                       null,
                steamPaymentInterval:          default,
                steamPricePoint:               0,
                storeDescriptionTranslationId: null,
                storeDescription:              name)
        {
            Contents = contents;
            Tagline  = tagline;
        }

        /// <summary>
        /// The demo price shown on the button. It is the SDK's USD reference price, not a store price, because
        /// no store is connected. Every screen that shows it also shows the word <i>Demo</i>.
        /// </summary>
        public string DemoPriceText => Invariant($"${Price.Double:0.00}");

        public void Validate(ConfigItemValidation validation)
        {
            validation.Require(!string.IsNullOrWhiteSpace(Name), "has no display name", nameof(Name));
            validation.Require(!string.IsNullOrWhiteSpace(DevelopmentId), "has no development product id", nameof(DevelopmentId));

            // A zero price would show as "Demo $0.00", which looks broken. The SDK's purchase analytics also
            // record the reference price as revenue.
            validation.Require(Price > F64.Zero, "has no reference price", nameof(Price));

            // Only the fake store is used. A product with a real store ID would look real in the Dashboard and
            // would charge real money as soon as a store was connected.
            validation.Require(GoogleId == null && AppleId == null && SteamId == null,
                "names a real store id; the sample connects no store", nameof(GoogleId));

            validation.Require(Type == InAppProductType.Consumable,
                $"is a {Type} product; demo bundles are consumable so a receipt cannot be reused", nameof(Type));

            if (HasDynamicContent)
                validation.Require(Contents == null, "has dynamic content but also fixed Contents; a MetaOffer supplies the reward for a dynamic-content product", nameof(Contents));
            else if (Contents == null)
                validation.Error("has no contents", nameof(Contents));
            else
                Contents.Validate(validation, nameof(Contents));
        }

        public override string ToString() => ProductId?.Value ?? "(no product)";
    }

    /// <summary>
    /// The contents a validated demo purchase granted, recorded on the purchase for customer support. The SDK
    /// stores it in the player's purchase history and shows it in the LiveOps Dashboard, so it still shows what
    /// was granted after the bundle's config changes.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class ResolvedWalletBundle : ResolvedPurchaseContentBase
    {
        [MetaMember(1)] public RewardBundle Contents { get; private set; }

        public ResolvedWalletBundle() { }
        public ResolvedWalletBundle(RewardBundle contents) { Contents = contents; }
    }

    /// <summary>
    /// Builds the receipt that the SDK's <b>Development</b> purchase platform validates (<c>docs/offers.md</c>,
    /// "Purchase flow"). The fake store gives the client no signed receipt, so the client creates one. The signature
    /// proves only that the receipt is well formed, which is why the server accepts this platform only in development
    /// environments. After validation the purchase takes the same path as a real one. It is in shared code so unit
    /// tests can check the receipt format and signature. The server never calls it.
    /// </summary>
    public static class DemoPurchaseReceipt
    {
        /// <summary>
        /// Returns a transaction ID for one purchase attempt of <paramref name="productId"/>. It must be unique
        /// per attempt, because the SDK refuses any purchase whose transaction ID was already used by any player.
        /// </summary>
        public static string NewTransactionId(InAppProductId productId, Guid attemptId) =>
            Invariant($"demo_{productId.Value}_{attemptId:N}");

        /// <summary>
        /// Returns the receipt body as the JSON that the server's development validator parses. The SDK defines
        /// the field names.
        /// </summary>
        public static string BuildReceiptJson(string platformProductId, string transactionId)
        {
            if (string.IsNullOrEmpty(platformProductId))
                throw new ArgumentException("A development purchase needs the product's DevelopmentId", nameof(platformProductId));
            if (string.IsNullOrEmpty(transactionId))
                throw new ArgumentException("A development purchase needs a transaction id", nameof(transactionId));

            StringBuilder json = new StringBuilder();
            json.Append('{');
            AppendString(json, "productId", platformProductId);
            json.Append(',');
            AppendString(json, "transactionId", transactionId);
            json.Append(',');
            AppendString(json, "originalTransactionId", transactionId);
            json.Append(',');
            AppendString(json, "paymentType", nameof(InAppPurchasePaymentType.Normal));
            json.Append(",\"validationDelaySeconds\":0,\"validationTransientErrorProbability\":0}");
            return json.ToString();
        }

        /// <summary>
        /// Returns the purchase event to pass to <see cref="PlayerInAppPurchased"/>. The receipt is
        /// base64-encoded, and the signature is the SHA-1 of the JSON before encoding, which the server
        /// recomputes.
        /// </summary>
        /// <param name="tamperSignature">
        /// Signs the receipt incorrectly so the server refuses it. Tests use it to check that the server really
        /// validates the signature. The client sets it only from a test query-string parameter.
        /// </param>
        public static InAppPurchaseEvent CreatePurchaseEvent(InAppProductInfoBase product, string transactionId, bool tamperSignature = false)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            string receiptJson = BuildReceiptJson(product.DevelopmentId, transactionId);

            return InAppPurchaseEvent.CreatePending(
                InAppPurchasePlatformDevelopment.Development,
                transactionId:         transactionId,
                productId:             product.ProductId,
                platformProductId:     product.DevelopmentId,
                receipt:               Convert.ToBase64String(Encoding.UTF8.GetBytes(receiptJson)),
                signature:             tamperSignature ? Util.ComputeSHA1(receiptJson + " ") : Util.ComputeSHA1(receiptJson),
                alternativePurchaseId: null,
                platformState:         null);
        }

        static void AppendString(StringBuilder json, string name, string value)
        {
            json.Append('"').Append(name).Append("\":\"");
            foreach (char c in value)
            {
                // Product and transaction IDs come from config and a GUID, so they currently need no escaping.
                // Escape anyway, because an unescaped quote would fail validation with an error that does not
                // point at the ID.
                if (c == '"' || c == '\\')
                    json.Append('\\').Append(c);
                else if (c < ' ')
                    json.Append(Invariant($"\\u{(int)c:x4}"));
                else
                    json.Append(c);
            }
            json.Append('"');
        }
    }
}
