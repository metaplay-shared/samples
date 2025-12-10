// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using UnityEngine;
using TMPro;
using Game.Logic;
using Metaplay.Core.InAppPurchase;
using Metaplay.Unity.IAP;
using Metaplay.Unity.DefaultIntegration;
using Metaplay.Core;

public class ShopStaticItemScript : MonoBehaviour
{
    public InAppProductId       ProductId;

    public TextMeshProUGUI      NameText;
    public GameObject           GemsAmountContainer;
    public TextMeshProUGUI      GemsAmountText;
    public GameObject           GoldAmountContainer;
    public TextMeshProUGUI      GoldAmountText;

    public TextMeshProUGUI      PriceText;

    public GameObject           PurchasingIndicator;

    public GameObject           SubscriptionActiveIndicator;
    public TextMeshProUGUI      SubscriptionExpiresInText;

    void Start()
    {
        IAPManager.StoreProductInfo? storeProductMaybe = MetaplayClient.IAPManager.TryGetStoreProductInfo(ProductId);
        if (storeProductMaybe.HasValue)
        {
            IAPManager.StoreProductInfo storeProduct    = storeProductMaybe.Value;
            InAppProductInfo            productInfo     = MetaplayClient.PlayerModel.GameConfig.InAppProducts[ProductId];
            NameText.text = productInfo.Name;
            GemsAmountContainer.SetActive(productInfo.NumGems != 0);
            GemsAmountText.text = $"{productInfo.NumGems}";
            GoldAmountContainer.SetActive(productInfo.NumGold != 0);
            GoldAmountText.text = $"{productInfo.NumGold}";
            PriceText.text = storeProduct.LocalizedPriceString;
        }
    }

    void Update()
    {
        PurchasingIndicator.SetActive(IsPurchasing());

        UpdateSubscriptionUI();
    }

    public void OnClickBuy()
    {
        if (IsPurchasing())
            return;

        // Note that actual store IAP purchase is not initiated immediately.
        // Instead we go through the context assignment flow: (See also: dynamic-content flow in ShopMetaOfferItemScript.cs)
        // - PlayerPreparePurchaseContext assigns the pending context for the in-app product.
        // - PlayerPreparePurchaseContext calls IPlayerModelClientListenerCore.PendingStaticInAppPurchaseContextAssigned,
        //   which on the client is implemented in ApplicationStateManager and calls IAPManager.RegisterPendingStaticPurchase
        //   to start tracking the context assignment status.
        // - When the server has confirmed the context assignment, IAPManager will initiate the IAP purchase.
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerPreparePurchaseContext(ProductId, gameProductAnalyticsId: ProductId.ToString(), new IdlerPurchaseAnalyticsContext(placement: "ShopTab", group: "Basic")));
    }

    bool IsPurchasing()
    {
        IAPFlowTracker.FlowStep? step = MetaplayClient.IAPFlowTracker.GetBestEffortLastKnownFlowStepInfo(ProductId)?.Step;
        return step.HasValue && !step.Value.IsTerminalStep();
    }

    void UpdateSubscriptionUI()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        SubscriptionModel subscription = TryGetSubscriptionModel();
        if (subscription == null)
        {
            SubscriptionActiveIndicator.SetActive(false);
            return;
        }

        MetaTime expirationTime = subscription.GetExpirationTime();
        if (player.CurrentTime >= expirationTime)
        {
            SubscriptionActiveIndicator.SetActive(false);
            return;
        }

        SubscriptionActiveIndicator.SetActive(true);
        SubscriptionExpiresInText.text = $"Expires in {(expirationTime - player.CurrentTime).ToSimplifiedString()}";
    }

    SubscriptionModel TryGetSubscriptionModel()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        if (player.GameConfig.InAppProducts[ProductId].Type != InAppProductType.Subscription)
            return null;

        player.IAPSubscriptions.Subscriptions.TryGetValue(ProductId, out SubscriptionModel subscription);
        return subscription;
    }
}
