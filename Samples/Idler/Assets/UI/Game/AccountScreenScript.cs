// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Player;
using Metaplay.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using EntityId = Metaplay.Core.EntityId;

public class AccountScreenScript : MonoBehaviour
{
    public TextMeshProUGUI NameLabel;
    public TextMeshProUGUI IdLabel;
    public TextMeshProUGUI LanguageLabel;
    public TextMeshProUGUI CreatedAtLabel;
    public TextMeshProUGUI SocialAuthLabel;
    public Button SocialAuthButton;
    public TextMeshProUGUI SocialAuthButtonLabel;
    public TextMeshProUGUI PlayerModelLabel;
    public TextMeshProUGUI CurrentPartyLabel;

    public GameObject GenericPopOver;
    public TMP_InputField PopoverInput;
    public TextMeshProUGUI PopoverHeader;
    public TextMeshProUGUI PopoverBody;
    public TextMeshProUGUI PopoverErrorLabel;
    public Button SaveButton;
    private Action<string> PopoverAction;
    private Func<string, string> PopoverValidateInput;
    public Button SendPartyMessageButton;

    private bool _socialAuthEnabled = false;

    void Start()
    {
#if UNITY_WEBGL
        // Social auth functionality is currently only enabled on WebGL and only if the public web api URL has
        // been explicitly configured.
        if (!string.IsNullOrEmpty(IEnvironmentConfigProvider.Get().ConnectionEndpointConfig.PublicWebApiUrl))
            _socialAuthEnabled = true;
#endif
        SocialAuthButton.gameObject.SetActive(_socialAuthEnabled);
        UpdateContent();
        GenericPopOver.SetActive(false);

    }

    void Update()
    {
        UpdateContent();
    }

    public void SocialAuthButtonClicked()
    {
        if (MetaplayClient.PlayerModel.SessionAuthenticationKey.Platform == AuthenticationPlatform.DeviceId)
            LoginWithGoogle();
        else
            Logout();
    }

    void LoginWithGoogle()
    {
        #if UNITY_WEBGL
        LoginApiBridge.OpenLoginPage(MetaplaySDK.Connection.Endpoint.PublicWebApiUrl, "google", true, false);
        #endif
    }

    void Logout()
    {
        #if UNITY_WEBGL
        LoginApiBridge.Logout(MetaplaySDK.Connection.Endpoint.PublicWebApiUrl);
        if (MetaplayClient.Connection.State.Status == ConnectionStatus.Connected || MetaplayClient.Connection.State.Status == ConnectionStatus.Connecting)
            MetaplayClient.Connection.CloseWithError(false, new ClientTerminatedConnectionConnectionError());
        #endif
    }

    void OnEnable()
    {
        RefreshPlayerModel();
    }

    public void RefreshPlayerModel()
    {
        PlayerModelLabel.text = PrettyPrint.Verbose(MetaplayClient.PlayerModel).ToString();
    }

    void UpdateContent()
    {
        NameLabel.text = $"Name: {MetaplayClient.PlayerModel.PlayerName}";
        IdLabel.text = $"Player ID: {MetaplayClient.PlayerModel.PlayerId}";
        LanguageLabel.text = $"Language: {MetaplayClient.PlayerModel.Language}";
        CreatedAtLabel.text =
            $"Joined: {MetaplayClient.PlayerModel.Stats.CreatedAt.ToDateTime().ToLocalTime().ToLongDateString()}";
        if (MetaplayClient.PartyClient?.Model != null)
        {
            CurrentPartyLabel.text = PrettyPrint.Verbose(MetaplayClient.PartyClient.Model).ToString();
            SendPartyMessageButton.interactable = true;
        }
        else
        {
            CurrentPartyLabel.text = "Not in a party";
            SendPartyMessageButton.interactable = false;
        }

        if (_socialAuthEnabled)
        {
            PlayerModel model = MetaplayClient.PlayerModel;
            if (model.SessionAuthenticationKey.Platform == AuthenticationPlatform.DeviceId)
            {
                // Guest session
                SocialAuthLabel.text = "Not logged in";
                SocialAuthButtonLabel.text = "Log In With Google";
            }
            else
            {
                PlayerAuthEntryBase authEntry = model.AttachedAuthMethods[model.SessionAuthenticationKey];
                SocialAuthLabel.text = authEntry.User != null ? $"Logged in as {authEntry.User.DisplayName}" : "Logged in";
                SocialAuthButtonLabel.text = "Log Out";
            }
        }
        else
        {
            SocialAuthLabel.text = "TBD";
        }
    }

    void ShowPopover(string header, string body, string currentValue, Action<string> saveAction, Func<string, string> validate)
    {
        GenericPopOver.SetActive(true);
        PopoverHeader.text = header;
        PopoverBody.text = body;
        PopoverErrorLabel.enabled = false;
        PopoverAction = saveAction;
        // Set current value without validation
        PopoverValidateInput = null;
        PopoverInput.text = currentValue;
        PopoverValidateInput = validate;
        SaveButton.interactable = false;
    }

    public void ShowChangeNamePopOver()
    {
        ShowPopover("Change Name", "New Name", MetaplayClient.PlayerModel.PlayerName,
            name => MetaplayClient.MessageDispatcher.SendMessage(new PlayerChangeOwnNameRequest(name)),
            ValidateNewName);
    }

    string ValidateChatMessage(string message)
    {
        if (message == "")
            return "Message is empty";
        return null;
    }

    void SendChatMessage(string message)
    {
        MetaplayClient.PartyClient.Context.EnqueueAction(new SendPartyMessage(message));
    }

    public void ShowChatMessagePopOver()
    {
        ShowPopover("Party Chat", "Message", "",
            SendChatMessage,
            ValidateChatMessage);
    }

    void JoinParty(string partyIdInput)
    {
        EntityId partyId = EntityId.ParseFromStringWithKind(EntityKindGame.Party, partyIdInput);
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerJoinParty() { PartyToJoin = partyId });
    }

    string ValidatePartyId(string partyIdInput)
    {
        if (!EntityId.TryParseFromString(partyIdInput, out EntityId partyId, out string err))
        {
            return err;
        }

        if (partyId.Kind != EntityKindGame.Party)
            return "Not a party";

        if (MetaplayClient.PartyClient?.Model != null && partyId == MetaplayClient.PartyClient.Model.EntityId)
            return "Can't join current party";

        return null;
    }


    public void ShowJoinPartyPopOver()
    {
        ShowPopover("Join Party", "Party Identifier", "",
            JoinParty,
            ValidatePartyId);
    }

    public void OnPopoverInputChange()
    {
        if (PopoverValidateInput == null)
        {
            PopoverErrorLabel.enabled = false;
            SaveButton.interactable = true;
            return;
        }

        string validateError = PopoverValidateInput(PopoverInput.text);
        if (validateError == null)
        {
            PopoverErrorLabel.enabled = false;
        }
        else
        {
            PopoverErrorLabel.text = validateError;
            PopoverErrorLabel.enabled = true;
        }

        SaveButton.interactable = validateError == null;
    }

    public void PopoverSave()
    {
        PopoverAction(PopoverInput.text);
        GenericPopOver.SetActive(false);
    }

    string ValidateNewName(string name)
    {
        if (name.Length < 5)
        {
            return "Name must be at least 5 characters";
        }
        return null;
    }

    public void ScheduleDeletion()
    {
        MetaplaySDK.MessageDispatcher.SendMessage(new PlayerScheduleDeletionRequest());
    }
}