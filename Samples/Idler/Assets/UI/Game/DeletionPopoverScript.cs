// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Player;
using Metaplay.Unity;
using Metaplay.Unity.DefaultIntegration;
using TMPro;
using UnityEngine;

public class DeletionPopoverScript : MonoBehaviour
{
    struct UIState
    {
        public PlayerDeletionStatus Status;
        public MetaTime             ScheduledForDeletionAt;

        public UIState(PlayerDeletionStatus status, MetaTime scheduledForDeletionAt)
        {
            Status = status;
            ScheduledForDeletionAt = scheduledForDeletionAt;
        }

        public override bool Equals(object obj)
        {
            return obj is UIState state &&
                   Status == state.Status &&
                   ScheduledForDeletionAt.Equals(state.ScheduledForDeletionAt);
        }

        public override int GetHashCode()
        {
            int hashCode = -53995429;
            hashCode = hashCode * -1521134295 + Status.GetHashCode();
            hashCode = hashCode * -1521134295 + ScheduledForDeletionAt.GetHashCode();
            return hashCode;
        }
    }

    public TextMeshProUGUI      CountdownLabel;
    UIState                     _lastUIState;

    void Hide()
    {
        gameObject.SetActive(false);
    }

    void Show()
    {
        UIState? uiState = ComputeUIState();
        if (uiState != null)
        {
            UpdateUI(uiState.Value);
        }
        gameObject.SetActive(true);
    }

    public void GameUIUpdate()
    {
        // Show update dialog when deletion status changes
        UIState? uiState = ComputeUIState();
        if (uiState.HasValue && !uiState.Value.Equals(_lastUIState))
            Show();
    }

    void Update()
    {
        UIState? uiState = ComputeUIState();
        if (uiState == null)
        {
            Hide();
            return;
        }

        UpdateUI(uiState.Value);
    }

    UIState? ComputeUIState()
    {
        if (MetaplayClient.PlayerModel.DeletionStatus.IsScheduled())
            return new UIState(MetaplayClient.PlayerModel.DeletionStatus, MetaplayClient.PlayerModel.ScheduledForDeletionAt);
        else
            return null;
    }

    void UpdateUI(UIState uiState)
    {
        MetaDuration duration = uiState.ScheduledForDeletionAt - MetaTime.Now;
        CountdownLabel.SetText(duration < MetaDuration.Zero ? "<deletion pending>" : duration.ToSimplifiedString());

        _lastUIState = uiState;
    }

    public void OnCancelClick()
    {
        // When server acks, the state update will hide the popup
        MetaplaySDK.MessageDispatcher.SendMessage(new PlayerCancelScheduledDeletionRequest());
    }

    public void OnCloseClick()
    {
        Hide();
    }
}
