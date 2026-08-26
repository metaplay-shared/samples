// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Rewards;
using Metaplay.Unity.DefaultIntegration;
using Metaplay.Unity.IAP;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopMetaOfferItemScript : MonoBehaviour
{
    public MetaOfferGroupId     OfferGroupId;
    public MetaOfferId          OfferId;

    public Image                BackgroundImage;

    public TextMeshProUGUI      RemainingCountText;

    public TextMeshProUGUI      NameText;
    public GameObject           GemsAmountContainer;
    public TextMeshProUGUI      GemsAmountText;
    public GameObject           GoldAmountContainer;
    public TextMeshProUGUI      GoldAmountText;
    public GameObject           ProducersContainer;
    public TextMeshProUGUI      ProducersText;

    public TextMeshProUGUI      PriceText;

    public GameObject           PurchasingIndicator;
    public GameObject           SoldOutIndicator;
    public GameObject           CannotAffordIndicator;

    bool _isPurchasing = false;

    void Start()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        if (!player.MetaOfferGroups.IsActive(OfferGroupId, player))
            return;

        IdlerOfferInfo offerInfo = player.GameConfig.Offers[OfferId];

        if (ColorUtility.TryParseHtmlString(offerInfo.BackgroundColor, out Color backgroundColor))
            BackgroundImage.color = backgroundColor;

        if (ColorUtility.TryParseHtmlString(offerInfo.TextColor, out Color textColor))
        {
            NameText.color = textColor;
            RemainingCountText.color = textColor;
        }

        NameText.text = offerInfo.DisplayName;

        if (offerInfo.HasInGameCurrencyCost)
        {
            PriceText.text = $"{offerInfo.GemCost} gems";
        }
        else
        {
            IAPManager.StoreProductInfo? storeProduct = MetaplayClient.IAPManager.TryGetStoreProductInfo(offerInfo.InAppProduct.Ref.ProductId);
            if (storeProduct.HasValue)
                PriceText.text = storeProduct.Value.LocalizedPriceString;
        }

        string producersText = null;

        foreach (MetaPlayerRewardBase reward in offerInfo.Rewards)
        {
            if (reward is RewardGems rewardGems)
            {
                GemsAmountContainer.SetActive(true);
                GemsAmountText.text = rewardGems.Amount.ToString();
            }
            else if (reward is RewardGold rewardGold)
            {
                GoldAmountContainer.SetActive(true);
                GoldAmountText.text = rewardGold.Amount.ToString();
            }
            else if (reward is RewardProducer rewardProducer)
            {
                if (producersText == null)
                    producersText = "";
                else
                    producersText += ", ";

                ProducerInfo producerInfo = player.GameConfig.Producers[rewardProducer.ProducerId];
                producersText += rewardProducer.Amount + " " + producerInfo.Name;
            }
            else
                Debug.LogWarning($"Unhandled reward type {reward.GetType()} in offer {OfferId} attachments!");
        }

        if (producersText != null)
        {
            ProducersContainer.SetActive(true);
            ProducersText.text = producersText;
        }
    }

    public void OnDestroy()
    {
        if (MetaplayClient.IsInitialized)
            UnregisterIAPFlowListener();
    }

    public void RegisterIAPFlowListener()
    {
        MetaplayClient.IAPFlowTracker.OnBestEffortKnownFlowStep += OnIAPFlowStep;
    }

    public void UnregisterIAPFlowListener()
    {
        MetaplayClient.IAPFlowTracker.OnBestEffortKnownFlowStep -= OnIAPFlowStep;
    }

    void Update()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        if (!player.MetaOfferGroups.IsActive(OfferGroupId, player))
            return;

        MetaOfferGroupInfoBase  offerGroupInfo  = player.GameConfig.OfferGroups[OfferGroupId];
        MetaOfferInfoBase       offerInfo       = player.GameConfig.Offers[OfferId];
        MetaOfferStatus         offerStatus     = player.MetaOfferGroups.GetOfferStatus(player, offerGroupInfo, offerInfo);

        // Update remaining count display

        int? purchasesRemaining = offerStatus.PurchasesRemainingInThisActivation;

        if (purchasesRemaining.HasValue)
        {
            RemainingCountText.gameObject.SetActive(true);
            RemainingCountText.text = $"{purchasesRemaining.Value} remaining";
        }
        else
            RemainingCountText.gameObject.SetActive(false);

        SoldOutIndicator.SetActive(purchasesRemaining == 0);

        // Set/unset purchasing indicator

        PurchasingIndicator.SetActive(_isPurchasing);

        // For in-game cost offers, set/unset "cannot afford" indicator

        if (offerInfo.HasInGameCurrencyCost)
            CannotAffordIndicator.SetActive(!offerInfo.CanAffordInGameCurrencyCost(player, offerGroupInfo));
    }

    public void OnClickBuy()
    {
        if (_isPurchasing)
            return;

        PlayerModel             player          = MetaplayClient.PlayerModel;
        MetaOfferGroupInfoBase  offerGroupInfo  = player.GameConfig.OfferGroups[OfferGroupId];
        MetaOfferInfoBase       offerInfo       = player.GameConfig.Offers[OfferId];

        PurchaseAnalyticsContext analyticsContext = new IdlerPurchaseAnalyticsContext(
            placement: offerGroupInfo.Placement.ToString(),
            group: OfferGroupId.ToString());

        if (offerInfo.HasInGameCurrencyCost)
        {
            MetaplayClient.PlayerContext.ExecuteAction(new PlayerPurchaseInGameCurrencyMetaOffer(
                offerGroupInfo,
                offerInfo,
                analyticsContext));
        }
        else
        {
            // Note that actual store IAP purchase is not initiated immediately.
            // Instead we go through the dynamic-content assignment flow:
            // - PlayerPreparePurchaseMetaOffer assigns the offer as the pending dynamic content for the in-app product.
            // - PlayerPreparePurchaseMetaOffer calls IPlayerModelClientListenerCore.PendingDynamicPurchaseContentAssigned,
            //   which on the client is implemented in ApplicationStateManager and calls IAPManager.RegisterPendingDynamicPurchase
            //   to start tracking the dynamic content assignment status.
            // - When the server has confirmed the dynamic content assignment, IAPManager will initiate the IAP purchase.
            MetaActionResult prepareResult = MetaplayClient.PlayerContext.ExecuteAction(new PlayerPreparePurchaseMetaOffer(
                offerGroupInfo,
                offerInfo,
                analyticsContext));

            if (prepareResult.IsSuccess)
            {
                _isPurchasing = true;
                RegisterIAPFlowListener();
            }
        }
    }

    void OnIAPFlowStep(InAppProductId productId, IAPFlowTracker.FlowStepInfo stepInfo)
    {
        PlayerModel player = MetaplayClient.PlayerModel;
        if (player == null)
            return;

        MetaOfferInfoBase offerInfo = player.GameConfig.Offers[OfferId];
        if (productId == offerInfo.InAppProduct.Ref.ProductId)
        {
            if (stepInfo.Step.IsTerminalStep())
            {
                UnregisterIAPFlowListener();
                _isPurchasing = false;
            }
        }
    }
}
