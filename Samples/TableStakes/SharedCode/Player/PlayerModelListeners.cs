namespace Game.Logic
{
    /// <summary>
    /// Hooks the server-side PlayerActor can implement to react to model changes, for example to notify other
    /// entities.
    /// </summary>
    public interface IPlayerModelServerListener
    {
        /// <summary>
        /// This player's <see cref="PlayerPublicIdentity"/> changed: they renamed, or an equipped cosmetic changed.
        /// The identity is computed, not stored, but other entities keep copies of it, such as the seasonal
        /// tournament's division standings. The actor uses this hook to refresh those copies (<c>docs/player.md</c>,
        /// "Public identity"). There is one hook for every cause, because a copy holder only needs to re-read
        /// <see cref="PlayerModel.BuildPublicIdentity"/>.
        /// </summary>
        void OnPublicIdentityChanged();
    }

    /// <summary>
    /// Hooks the client can implement to react to model changes (typically to re-render the UI).
    /// </summary>
    public interface IPlayerModelClientListener
    {
        /// <summary>Triggered by <see cref="PlayerRecordMatchResult"/> when a finished game is added to the record.</summary>
        void OnRecordChanged();

        /// <summary>
        /// Triggered by <see cref="PlayerModel.ApplyRename"/> when the display name changes.
        /// </summary>
        void OnNameChanged(string name);

        /// <summary>
        /// Triggered by <see cref="PlayerModel.ApplyWallet"/> after a balance change is applied, so the HUD
        /// redraws from the wallet's actual state.
        /// </summary>
        void OnWalletChanged();

        /// <summary>
        /// Triggered when this player's seasonal tournament state changes: they joined a season, claimed a
        /// milestone or a placement reward, or a concluded season's result was recorded.
        /// </summary>
        void OnTournamentChanged();

        /// <summary>
        /// Triggered by <see cref="PlayerModel.ApplyDailyRewardClaim"/> when a daily reward claim is applied.
        /// <para>
        /// It is triggered by the model change, not by a server reply. A client that reconnects after the claim
        /// sees the reward already in the wallet and the day already claimed, so the reveal plays at most once.
        /// </para>
        /// </summary>
        void OnDailyRewardClaimed();

        /// <summary>
        /// Triggered by <see cref="PlayerClaimMissionReward"/> when a mission's reward has been granted.
        /// </summary>
        void OnMissionsChanged();

        /// <summary>
        /// Triggered by <see cref="PlayerClaimFirstWeekReward"/> when a first-week day's reward has been granted.
        /// </summary>
        void OnFirstWeekChanged();

        /// <summary>
        /// Triggered when this player's spin wheel state changes: a spin was resolved, or its result was
        /// acknowledged.
        /// <para>
        /// It is triggered by the model change, not by a server reply. A client that reconnects after a spin
        /// finds the result still unacknowledged and shows it then, so the result is shown once
        /// (<c>docs/spin-wheel.md</c>).
        /// </para>
        /// </summary>
        void OnSpinWheelChanged();

        /// <summary>
        /// Triggered by <see cref="PlayerClaimWeeklyEventReward"/> when a weekly event's reward has been granted.
        /// </summary>
        void OnWeeklyEventChanged();

        /// <summary>
        /// Triggered when this player's cosmetics change: an item was bought, equipped, granted by a tournament
        /// placement, or acknowledged (<c>docs/cosmetics.md</c>).
        /// </summary>
        void OnCosmeticsChanged();

        /// <summary>
        /// Triggered when this player's offer state changes: the personalized-offers setting was changed, or a
        /// wallet-priced offer was purchased (<c>docs/offers.md</c>).
        /// </summary>
        void OnOffersChanged();
    }

    public class EmptyPlayerModelServerListener : IPlayerModelServerListener
    {
        public static readonly EmptyPlayerModelServerListener Instance = new EmptyPlayerModelServerListener();

        public void OnPublicIdentityChanged() { }
    }

    public class EmptyPlayerModelClientListener : IPlayerModelClientListener
    {
        public static readonly EmptyPlayerModelClientListener Instance = new EmptyPlayerModelClientListener();

        public void OnRecordChanged() { }
        public void OnNameChanged(string name) { }
        public void OnWalletChanged() { }
        public void OnTournamentChanged() { }
        public void OnDailyRewardClaimed() { }
        public void OnMissionsChanged() { }
        public void OnFirstWeekChanged() { }
        public void OnSpinWheelChanged() { }
        public void OnWeeklyEventChanged() { }
        public void OnCosmeticsChanged() { }
        public void OnOffersChanged() { }
    }
}
