// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Offers;
using Metaplay.Unity.DefaultIntegration;
using Metaplay.Unity.IAP;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class ShopScreenScript : MonoBehaviour
{
    public RectTransform                ItemList;
    public ShopStaticItemScript         StaticItem;
    public ShopOfferGroupItemScript     ShopOfferGroupItem;
    public TextMeshProUGUI              StatusText;

    bool                                            _hasCreatedStaticProductItems   = false;
    MetaDictionary<MetaOfferGroupId, GameObject> _offerGroupItems                = new MetaDictionary<MetaOfferGroupId, GameObject>();

    InAppProductId _latestPurchasingProduct = null;

    void Start()
    {
        MetaplayClient.IAPManager.OnStoreInitializationSucceeded    += OnStoreInitializationSucceeded;
        MetaplayClient.IAPManager.OnStoreInitializationFailed       += OnStoreInitializationFailed;

        MetaplayClient.IAPFlowTracker.OnBestEffortKnownFlowStep += OnPurchaseFlowStep;

        if (MetaplayClient.IAPManager.StoreIsAvailable)
            OnStoreInitializationSucceeded();
        else if (MetaplayClient.IAPManager.StoreInitFailure.HasValue)
            OnStoreInitializationFailed(MetaplayClient.IAPManager.StoreInitFailure.Value);
        else
            SetStatusText("Store initializing...");
    }

    void OnEnable()
    {
        MetaOfferGroupsRefreshInfo refreshInfo = MetaplayClient.PlayerModel.GetMetaOfferGroupsRefreshInfo();

        if (refreshInfo.HasAny())
        {
            DebugLog.Debug("Entered shop screen, refreshing offers");
            MetaplayClient.PlayerContext.ExecuteAction(new PlayerRefreshMetaOffers(refreshInfo));
        }
        else
            DebugLog.Debug("Entered shop screen, but there are no offers to refresh");
    }

    void Update()
    {
        RefreshItems();
    }

    void OnDestroy()
    {
        if (!MetaplayClient.IsInitialized)
            return;
        MetaplayClient.IAPManager.OnStoreInitializationSucceeded    -= OnStoreInitializationSucceeded;
        MetaplayClient.IAPManager.OnStoreInitializationFailed       -= OnStoreInitializationFailed;

        MetaplayClient.IAPFlowTracker.OnBestEffortKnownFlowStep -= OnPurchaseFlowStep;
    }

    void OnStoreInitializationSucceeded()
    {
        RefreshItems();
        SetStatusText("Store initialized");
        ItemList.anchoredPosition = new Vector2(ItemList.anchoredPosition.x, 0);
    }

    void OnStoreInitializationFailed(IAPManager.StoreInitializationFailure failure)
    {
        string statusText = $"Store initialization failed: {failure.Reason}.";
        if (failure.Message != null)
            statusText += $" {failure.Message}";

        SetStatusText(statusText);
    }

    void OnPurchaseFlowStep(InAppProductId productId, IAPFlowTracker.FlowStepInfo info)
    {
        if (info.Step.IsInitialStep())
            _latestPurchasingProduct = productId;

        if (_latestPurchasingProduct == productId)
            SetStatusText(GetFlowStepStatusText(info));
    }

    void RefreshItems()
    {
        if (!MetaplayClient.IAPManager.StoreIsAvailable)
            return;

        RefreshStaticItems();
        RefreshMetaOfferItems();
    }

    void RefreshStaticItems()
    {
        if (_hasCreatedStaticProductItems)
            return;

        _hasCreatedStaticProductItems = true;

        foreach (InAppProductInfo info in MetaplayClient.PlayerModel.GameConfig.InAppProducts.Values.OrderBy(info => info.NumGems))
        {
            // Dynamic-content IAPs are not normal store products
            if (info.HasDynamicContent)
                continue;

            bool productIsAvailable = MetaplayClient.IAPManager.StoreProductIsAvailable(info.ProductId);
            if (!productIsAvailable)
                continue;

            ShopStaticItemScript item = Instantiate(StaticItem, ItemList);
            item.ProductId = info.ProductId;
        }
    }

    void RefreshMetaOfferItems()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        // Remove from _offerGroupItems the offer groups that are no longer active
        {
            IEnumerable<MetaOfferGroupId> offerGroupsToRemove =
                _offerGroupItems.Keys
                .Where(offerGroupId =>
                {
                    return !player.MetaOfferGroups.IsActive(offerGroupId, player);
                });
            if (offerGroupsToRemove.Any())
            {
                foreach (MetaOfferGroupId offerGroupId in new List<MetaOfferGroupId>(offerGroupsToRemove)) // \note Evaluate and copy offerGroupsToRemove, because _offerGroupItems gets mutated
                {
                    Destroy(_offerGroupItems[offerGroupId]);
                    _offerGroupItems.Remove(offerGroupId);
                }
            }
        }

        // Add missing active offers to _offerGroupItems

        IEnumerable<MetaOfferGroupModelBase> shopOfferGroups =
            player.MetaOfferGroups.GetActiveStates(player)
            .Where(groupState => shopPlacements.Contains(groupState.ActivableInfo.Placement));

        foreach (MetaOfferGroupModelBase offerGroup in shopOfferGroups)
        {
            MetaOfferGroupInfoBase offerGroupInfo = offerGroup.ActivableInfo;

            if (_offerGroupItems.ContainsKey(offerGroupInfo.GroupId))
                continue;

            ShopOfferGroupItemScript item = Instantiate(ShopOfferGroupItem, ItemList);
            item.OfferGroupId = offerGroupInfo.GroupId;
            _offerGroupItems.Add(offerGroupInfo.GroupId, item.gameObject);
        }

        // Sort the offer group gameobjects based on placement.
        //
        // This works by going through the placements and, at each placement,
        // setting the corresponding offer group(s) as the last sibling.
        // Since the placements are processed in topmost-first order, the
        // offer groups end up in the proper order.

        foreach (OfferPlacementId placement in shopPlacements)
        {
            foreach ((MetaOfferGroupId groupId, GameObject item) in _offerGroupItems)
            {
                MetaOfferGroupInfoBase groupInfo = player.GameConfig.OfferGroups[groupId];
                if (groupInfo.Placement == placement)
                    item.transform.SetAsLastSibling();
            }
        }
    }

    // Placements that are shown in the shop UI, in topmost-first order.
    //
    // This is just simple example usage of placements. In a proper game
    // different placements would probably have more meaningful differences
    // about where they're shown in the game UI.
    static OfferPlacementId[] shopPlacements = new OfferPlacementId[]
    {
        OfferPlacementId.FromString("ShopFeatured"),
        OfferPlacementId.FromString("Events"),
        OfferPlacementId.FromString("Shop"),
    };

    string GetFlowStepStatusText(IAPFlowTracker.FlowStepInfo info)
    {
        switch (info.Step)
        {
            case IAPFlowTracker.FlowStep.BeganPurchasePreparation:              return "Preparing purcase...";
            case IAPFlowTracker.FlowStep.AbortedPurchasePreparation:            return "Purchase preparation stopped";
            case IAPFlowTracker.FlowStep.Initiated:                             return "Purchasing...";
            case IAPFlowTracker.FlowStep.PurchaseFailed:                        return "Purchase failed: " + (info.StorePurchaseFailureMaybe?.Reason.ToString() ?? "<unknown>");
            case IAPFlowTracker.FlowStep.FailedToStartValidation:               return "Purchase action failed unexpectedly";
            case IAPFlowTracker.FlowStep.StartedValidation:                     return "Validating...";
            case IAPFlowTracker.FlowStep.FinishedWithSuccessAndClaimed:         return "Purchased!";
            case IAPFlowTracker.FlowStep.FinishedWithDuplicateReceipt:          return "Duplicate transaction";
            case IAPFlowTracker.FlowStep.FinishedWithFailure:                   return "Validation failed";
            case IAPFlowTracker.FlowStep.FinishedWithUserDeclined:              return "User declined the purchase";
            case IAPFlowTracker.FlowStep.FinishedWithRefunded:                  return "Purchase was already refunded";
            case IAPFlowTracker.FlowStep.FinishedWithAbandoned:                 return "Purchase was interrupted";
            case IAPFlowTracker.FlowStep.FinishedWithUnexpectedStatus:          return "Finished with unexpected status";

            default:
                return "<unknown>";
        }
    }

    void SetStatusText(string text)
    {
        StatusText.gameObject.SetActive(true);
        StatusText.text = text;
    }
}
