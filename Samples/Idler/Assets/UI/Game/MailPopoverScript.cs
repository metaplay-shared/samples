// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using UnityEngine;
using TMPro;
using Game.Logic;
using Metaplay.Core.InGameMail;
using Metaplay.Core.Rewards;
using Metaplay.Core.Player;
using Metaplay.Unity.DefaultIntegration;
using Metaplay.Core;
using Metaplay.Core.Localization;

public class MailPopoverScript : MonoBehaviour
{
    public TextMeshProUGUI      MailTitleLabel;
    public TextMeshProUGUI      MailBodyLabel;
    public TextMeshProUGUI      GemsLabel;
    public TextMeshProUGUI      GoldLabel;
    public GameObject           GemsContainer;
    public GameObject           GoldContainer;
    MetaGuid                    _mailId;

    /// <summary>
    /// Enable the mail popup and populate it with the mail's contents
    /// </summary>
    /// <param name="mail">Admin mail</param>
    public void ShowMail(PlayerMailItem mailItem)
    {
        SimplePlayerMail mail = (SimplePlayerMail)mailItem.Contents;
        gameObject.SetActive(true);
        MailTitleLabel.text = mail.Title.Localize();
        MailBodyLabel.text = mail.Body.Localize();

        GemsContainer.SetActive(false);
        GoldContainer.SetActive(false);

        // Add attached producers to the text field. It would be nicer to create fance graphics and a scrollable list, but this will do for a demo :)
        if (mail.Attachments != null && mail.Attachments.Count > 0)
        {
            MailBodyLabel.text += "\n\nAttachments:\n";
            foreach (MetaPlayerRewardBase attachment in mail.Attachments)
            {
                if (attachment is RewardGems rewardGems)
                {
                    GemsLabel.text = rewardGems.Amount.ToString();
                    MailBodyLabel.text += "\t" + GemsLabel.text + " gems\n";
                    GemsContainer.SetActive(true);
                }
                else if (attachment is RewardGold rewardGold)
                {
                    GoldLabel.text = rewardGold.Amount.ToString();
                    MailBodyLabel.text += "\t" + GoldLabel.text + " gold\n";
                    GoldContainer.SetActive(true);
                }
                else if (attachment is RewardProducer rewardProducer)
                {
                    if (MetaplayClient.PlayerModel.Producers.ContainsKey(rewardProducer.ProducerId))
                        MailBodyLabel.text += "\t" + rewardProducer.ProducerId + " (upgrade x" + rewardProducer.Amount + ")\n";
                    else
                        MailBodyLabel.text += "\t" + rewardProducer.ProducerId + " (unlock at level " + rewardProducer.Amount + ")\n";
                }
                else
                    Debug.LogWarning("Unhandled reward type in mail attachments!");
            }
        }

        _mailId = mail.Id;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void Consume()
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerConsumeMail(_mailId));
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerDeleteMail(_mailId));
    }
}
