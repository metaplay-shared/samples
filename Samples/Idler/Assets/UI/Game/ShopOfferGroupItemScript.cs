// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Core.Activables;
using Metaplay.Core.Offers;
using Metaplay.Unity.DefaultIntegration;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopOfferGroupItemScript : MonoBehaviour
{
    public MetaOfferGroupId         OfferGroupId;

    public Image                    BackgroundImage;

    public TextMeshProUGUI          NameText;
    public TextMeshProUGUI          PlacementText;
    public TextMeshProUGUI          ExpirationText;

    public Transform                OfferList;
    public ShopMetaOfferItemScript  OfferItem;

    public List<ShopMetaOfferItemScript> _offerItems = new List<ShopMetaOfferItemScript>();

    void Start()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        if (!player.MetaOfferGroups.IsActive(OfferGroupId, player))
            return;

        IdlerOfferGroupInfo offerGroupInfo = player.GameConfig.OfferGroups[OfferGroupId];

        if (ColorUtility.TryParseHtmlString(offerGroupInfo.BackgroundColor, out Color backgroundColor))
            BackgroundImage.color = backgroundColor;

        NameText.text = offerGroupInfo.DisplayName;
        PlacementText.text = $"(placement: {offerGroupInfo.Placement})";

        foreach (MetaOfferStatus offer in player.MetaOfferGroups.GetOffersInGroup(offerGroupInfo, player))
        {
            ShopMetaOfferItemScript item = Instantiate(OfferItem, OfferList);
            item.OfferGroupId = OfferGroupId;
            item.OfferId = offer.Info.OfferId;
            _offerItems.Add(item);
        }
    }

    void Update()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        if (!player.MetaOfferGroups.IsActive(OfferGroupId, player))
            return;

        MetaOfferGroupModelBase         offerGroup      = player.MetaOfferGroups.TryGetState(OfferGroupId);
        MetaActivableState.Activation   activation      = offerGroup.LatestActivation.Value;

        // Update expiration text

        if (activation.EndAt.HasValue)
        {
            ExpirationText.gameObject.SetActive(true);
            ExpirationText.text = $"Expires in {activation.EndAt.Value - player.CurrentTime}";
        }
        else
            ExpirationText.gameObject.SetActive(false);

        // Hide/show each offer based on whether the offer is active

        foreach (ShopMetaOfferItemScript item in _offerItems)
            item.gameObject.SetActive(offerGroup.OfferIsActive(item.OfferId, player));
    }
}
