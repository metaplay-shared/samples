// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Core.Math;
using Metaplay.Unity.DefaultIntegration;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProducerListItemScript : MonoBehaviour
{
    public ProducerTypeId       ProducerId;
    public TextMeshProUGUI      NameText;
    public TextMeshProUGUI      KindText;

    public GameObject           Unlocked;
    public TextMeshProUGUI      LevelText;
    public Button               UpgradeButton;
    public TextMeshProUGUI      UpgradeCostText;
    public TextMeshProUGUI      ProduceValueText;
    public Slider               ProgressSlider;
    public GameObject           UnlockedNormalBackground;
    public GameObject           UnlockedEventBackground;

    public GameObject           Locked;
    public Button               UnlockButton;
    public TextMeshProUGUI      UnlockCost;

    void Start()
    {
        ProducerInfo producerInfo = MetaplayClient.PlayerModel.GameConfig.Producers[ProducerId];
        NameText.text = producerInfo.Name;
        KindText.text = producerInfo.Kind.Ref.Name;
    }

    void Update()
    {
        // Fetch info
        PlayerModel player = MetaplayClient.PlayerModel;
        ProducerInfo producerInfo = player.GameConfig.Producers[ProducerId];

        // Enable/disable
        bool isUnlocked = player.Producers.TryGetValue(ProducerId, out ProducerModel producer);
        Locked.SetActive(!isUnlocked);
        Unlocked.SetActive(isUnlocked);

        IEnumerable<HappyHourModel> activeHappyHours = player.HappyHours.GetActiveStates(player);

        if (isUnlocked)
        {
            bool isEventActive = activeHappyHours.Any(happyHour => happyHour.Info.Producer.GetItem(MetaplayClient.PlayerModel.GameConfig).Id == producerInfo.Id);
            UnlockedNormalBackground.SetActive(!isEventActive);
            UnlockedEventBackground.SetActive(isEventActive);

            LevelText.text = $"Level {producer.Level}";
            ProgressSlider.value = producer.GetProgressAt(player.CurrentTime + MetaplayClient.PlayerContext.GetAccumulatedTimeSinceLastTick());

            // Show upgrade button
            int upgradeCost = producer.GetUpgradeCost(activeHappyHours, player.GameConfig);
            UpgradeButton.interactable = (player.Wallet.NumGold >= upgradeCost);
            UpgradeCostText.text = upgradeCost.ToString();
            ProduceValueText.text = $"+{producer.ProduceValue}";
        }
        else
        {
            int unlockCost = producerInfo.GetUnlockCost(activeHappyHours, player.GameConfig);
            bool canAfford = player.Wallet.NumGold >= unlockCost;
            UnlockButton.interactable = canAfford;
            UnlockCost.text = unlockCost.ToString();
            ProduceValueText.text = $"+{F64.RoundToInt(producerInfo.BaseValue)}";
            ProgressSlider.value = 0;
        }
    }

    public void OnClickUnlock()
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerUnlockProducer(ProducerId));
    }

    public void OnClickUpgrade()
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerUpgradeProducer(ProducerId));
    }
}
