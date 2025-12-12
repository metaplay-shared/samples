using Game.Logic;
using Metaplay.Core.Web3;
using Metaplay.Core.Tasks;
using Metaplay.Unity;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Represents the in-game application logic. Only gets spawned after a session has been
/// established with the server, so we can assume all the state has been setup already.
/// </summary>
public class GameManager : MonoBehaviour
{
    public Text     NumClicksText;                      // Text to display the number of clicks so far.
    public Button   ClickMeButton;                      // 'Click Me' button that invokes OnClickButton().
    public Text     UnhealthyConnectionIndicator;       // Indicator to display when connection is in an unhealthy state.

    public Text     NftsList;
    public Button   UpgradeNftsButton;

    void Update()
    {
        // Update the number of clicks on UI.
        NumClicksText.text = MetaplayClient.PlayerModel.NumClicks.ToString();

        // Show the unhealthy connection indicator.
        bool connectionIsUnhealthy = MetaplayClient.ConnectionHealth == Metaplay.Unity.DefaultIntegration.ConnectionHealth.Unhealthy;
        UnhealthyConnectionIndicator.gameObject.SetActive(connectionIsUnhealthy);

        StringBuilder nftInfos = new StringBuilder();
        foreach (MetaNft nft in MetaplayClient.PlayerModel.Nft.OwnedNfts.Values)
        {
            string nftInfo = $"{NftTypeRegistry.Instance.GetNftKey(nft)}, isMinted={nft.IsMinted}, class={nft.GetType().Name}";

            if (nft is CharacterNft characterNft)
                nftInfo += $", level={characterNft.Level}";

            nftInfos.AppendLine(nftInfo);
        }
        NftsList.text = nftInfos.ToString();
    }

    public void OnClickButton()
    {
        // Button was clicked, execute the action to bump PlayerModel.NumClicks (on client and server).
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerClickButton());
    }

    public void OnClickImxLogin()
    {
        MetaTask.Run(async () =>
        {
            await ImmutableXLinkSdkHelper.LoginWithImmutableXAsync(forceResetup: false);
        });
    }

    public void OnClickUpgradeNftsButton(int nFirst)
    {
        IEnumerable<NftId> allTestIds = MetaplayClient.PlayerModel.Nft.OwnedNfts.Where(kv => kv.Value is CharacterNft).Select(kv => kv.Key.TokenId);
        IEnumerable<NftId> testIdsToUpgrade = allTestIds.Take(nFirst);

        MetaplayClient.NftClient.ExecuteNftTransaction(new PlayerUpgradeCharacterNfts(testIdsToUpgrade.ToArray()));
    }
}
