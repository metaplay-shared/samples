// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Unity.DefaultIntegration;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ProducerListScript : MonoBehaviour
{
    public ProducerListItemScript      ProducerListItem;

    Dictionary<ProducerTypeId, GameObject> _producerItems = new Dictionary<ProducerTypeId, GameObject>();

    void Start()
    {
        RefreshProducerItems();
    }

    void Update()
    {
        RefreshProducerItems();
    }

    void RefreshProducerItems()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        IEnumerable<ProducerInfo> producerInfos =
            player.GameConfig.Producers.Values
            .OrderBy(info => (info.Category, info.UnmodifiedUnlockCost));

        foreach (ProducerInfo info in producerInfos)
        {
            // \note Locked producers are only shown if they're of Normal category. Only Normal producers can be unlocked by normal means.
            bool shouldShow = player.Producers.ContainsKey(info.Id) || info.Category == ProducerCategory.Normal;

            if (shouldShow)
            {
                if (!_producerItems.ContainsKey(info.Id))
                {
                    ProducerListItemScript box = Instantiate(ProducerListItem, transform);
                    box.ProducerId = info.Id;
                    _producerItems.Add(info.Id, box.gameObject);

                    // Special event producers at the top
                    if (info.Category == ProducerCategory.Event)
                        box.transform.SetAsFirstSibling();
                }
            }
            else
            {
                if (_producerItems.ContainsKey(info.Id))
                {
                    Destroy(_producerItems[info.Id]);
                    _producerItems.Remove(info.Id);
                }
            }
        }
    }
}
