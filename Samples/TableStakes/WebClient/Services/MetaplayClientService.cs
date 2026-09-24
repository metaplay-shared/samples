using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Logic;
using Metaplay.Client;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.League;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Session;
using Metaplay.Unity;
using WebClient.Integration;
using WebClient.Meta;
using WebClientBase.Services;

namespace WebClient.Services;

/// <summary>
/// The game's Metaplay client service. It inherits connection lifecycle management from
/// <see cref="MetaplayClientServiceBase{TModel}"/>, and implements the player and match model client listeners so
/// that model changes re-render the UI.
/// </summary>
public class MetaplayClientService : MetaplayClientServiceBase<PlayerModel>, IPlayerModelClientListener, IPlayerModelClientListenerCore, IMatchModelClientListener
{
    public MetaplayClientService()
    {
        // OnStateChanged is raised on every client update, so the demo offer purchase's wait for server
        // confirmation (see StartDemoOfferPurchase) is checked here instead of on a separate timer.
        OnStateChanged += ContinueOfferPurchaseIfConfirmed;
    }

    /// <summary>
    /// The client side of the match protocol. It lives for the whole app instead of one session, and the SDK
    /// attaches and detaches it as the session's match entity association changes.
    /// </summary>
    public MatchClient MatchClient { get; } = new MatchClient();

    /// <summary>
    /// The client side of the seasonal tournament: the SDK's League sub-client on the tournament slot. The server
    /// associates the player's division with that slot, and this client attaches to it, as
    /// <see cref="MatchClient"/> does for a match (<c>docs/seasonal-tournament.md</c>).
    /// </summary>
    public LeagueClient<TournamentDivisionModel> TournamentClient { get; } = new LeagueClient<TournamentDivisionModel>(ClientSlotGame.Tournament);

    /// <summary>The last move refusal from the host, or null if the last move was accepted.</summary>
    public MatchMoveRefused? LastMoveRefusal { get; private set; }

    /// <summary>
    /// The server's response to the last rename request, or null if there is none in this session. It includes the
    /// refusal reason, which is why renaming uses a request and response instead of the SDK's rename action.
    /// </summary>
    public PlayerRenameResponse? LastRenameResponse { get; private set; }

    /// <summary>
    /// Creates the Metaplay client with the match and tournament sub-clients, and registers the message
    /// handlers.
    /// </summary>
    protected override IMetaplayClient CreateClient()
    {
        // Use SetClientListeners instead of assigning the listener once, because the SDK applies it to the
        // current model and to every later model, including after an entity switch or a reconnect.
        MatchClient.SetClientListeners(model => ((MatchModel)model).ClientListener = this);

        // CreateClient runs again after a Reset with the same sub-client instances, so remove each handler before
        // adding it to avoid duplicates.
        MatchClient.ModelUpdated          -= NotifyStateChanged;
        MatchClient.PhaseChanged          -= OnMatchPhaseChanged;
        MatchClient.HandChanged           -= NotifyStateChanged;
        MatchClient.MoveRefused           -= OnMoveRefused;
        MatchClient.TimelineUpdateFailed  -= OnMatchTimelineUpdateFailed;

        MatchClient.ModelUpdated          += NotifyStateChanged;
        MatchClient.PhaseChanged          += OnMatchPhaseChanged;
        MatchClient.HandChanged           += NotifyStateChanged;
        MatchClient.MoveRefused           += OnMoveRefused;
        MatchClient.TimelineUpdateFailed  += OnMatchTimelineUpdateFailed;

        // A Reset ends the current match, so clear the per-match and per-session state.
        LastMoveRefusal = null;
        LastRenameResponse = null;
        SetMatchmaking(MatchmakingStatus.NotSearching, MetaTime.Epoch);

        TournamentClient.ModelUpdated -= NotifyStateChanged;
        TournamentClient.PhaseChanged  -= NotifyStateChanged;
        TournamentClient.ModelUpdated += NotifyStateChanged;
        TournamentClient.PhaseChanged  += NotifyStateChanged;

        _tournamentJoinAnswer.Abandon();
        _tournamentClaimAnswer.Abandon();

        IMetaplayClient client = MetaplayClient.Create(additionalClients: new IMetaplaySubClient[] { MatchClient, TournamentClient });

        // These messages arrive through the connection-level message dispatcher, because the player actor sends
        // them and the client has no matchmaker entity. The dispatcher exists only after MetaplayClient.Create and
        // lives across sessions, so ListenOnce removes each listener before adding it to avoid duplicates after a
        // Reset.
        ListenOnce<MatchmakingStatusUpdate>(OnMatchmakingStatusUpdate);
        ListenOnce<PlayerRenameResponse>(OnRenameResponse);
        ListenOnce<TournamentJoinResponse>(OnTournamentJoinResponse);
        ListenOnce<TournamentClaimResponse>(OnTournamentClaimResponse);
        ListenOnce<PlayerDailyRewardClaimResponse>(OnDailyRewardClaimResponse);
        ListenOnce<PlayerWheelSpinResponse>(OnWheelSpinResponse);

        return client;
    }

    /// <summary>Adds a message listener, first removing it if it is already registered.</summary>
    static void ListenOnce<TMessage>(MessageHandler<TMessage> handler) where TMessage : MetaMessage
    {
        MetaplaySDK.MessageDispatcher.RemoveListener<TMessage>(handler);
        MetaplaySDK.MessageDispatcher.AddListener<TMessage>(handler);
    }

    /// <summary>Sends <paramref name="message"/> to the server from the SDK's main thread.</summary>
    static void SendOnMainThread(MetaMessage message) =>
        MetaplaySDK.RunOnMainThreadAsync(() => MetaplaySDK.MessageDispatcher.SendMessage(message));

    /// <summary>Executes <paramref name="action"/> on the session's player context, or returns null if there is no session.</summary>
    MetaActionResult? ExecuteInSession(PlayerActionBase action) => Session?.PlayerContext?.ExecuteAction(action);

    /// <summary>Sets the matchmaking status and the seat deadline together.</summary>
    void SetMatchmaking(MatchmakingStatus status, MetaTime seatDeadlineAt)
    {
        MatchmakingStatus         = status;
        MatchmakingSeatDeadlineAt = seatDeadlineAt;
    }

    /// <summary>
    /// Called when the match timeline diverged from the server's, detected by the match's per-operation checksums.
    /// Closes the connection with an <see cref="EntityTimelineDesyncConnectionError"/>, so the shell shows a
    /// re-sync message and reconnects to get a fresh model. The SDK default closes with a generic terminal error,
    /// which the shell shows as an unreachable server without retrying.
    /// </summary>
    private void OnMatchTimelineUpdateFailed(EntityId matchId)
    {
        Console.WriteLine($"[{GetType().Name}] The timeline of table {matchId} diverged; closing the session to re-sync");
        MetaplaySDK.Connection?.CloseWithError(new EntityTimelineDesyncConnectionError(matchId));
    }

    #region The profile

    /// <summary>The player's lifetime statistics, or an empty record before the session has loaded.</summary>
    public PlayerRecord Record => PlayerModel?.Record ?? EmptyRecord;

    static readonly PlayerRecord EmptyRecord = new PlayerRecord();

    /// <summary>The player's display name, or an empty string before the session has loaded.</summary>
    public string DisplayName => PlayerModel?.PlayerName ?? string.Empty;

    /// <summary>
    /// The player's public identity from <see cref="PlayerModel.BuildPublicIdentity"/>, or null before the session
    /// has loaded. The standings use the same method, so the Profile preview matches them (<c>docs/player.md</c>).
    /// </summary>
    public PlayerPublicIdentity? PublicIdentity => PlayerModel?.BuildPublicIdentity();

    /// <summary>
    /// Requests a new display name.
    /// <para>
    /// The server validates the name and responds with the result and any refusal reason in
    /// <see cref="LastRenameResponse"/>. The name is not changed locally before the response
    /// (<c>docs/player.md</c>, "Renaming").
    /// </para>
    /// </summary>
    public void Rename(string newName)
    {
        LastRenameResponse = null;
        NotifyStateChanged();
        SendOnMainThread(new PlayerRenameRequest(newName));
    }

    void OnRenameResponse(PlayerRenameResponse response)
    {
        LastRenameResponse = response;
        NotifyStateChanged();
    }

    #endregion

    #region The seasonal tournament

    /// <summary>
    /// The player's tournament division, or null if the player is not in a season or the client is still attaching.
    /// </summary>
    public TournamentDivisionModel? TournamentDivision =>
        TournamentClient.Phase == LeagueClientPhase.DivisionActive ? TournamentClient.Division : null;

    /// <summary>
    /// The division of the concluded season whose reward is unclaimed, or <see cref="EntityId.None"/>. It is read
    /// from the player's tournament history, which a new season does not erase, so it survives reconnects and
    /// season changes.
    /// </summary>
    public EntityId PendingTournamentDivision => PlayerModel?.PendingTournamentReward()?.DivisionId ?? EntityId.None;

    /// <summary>The concluded season whose reward is unclaimed, or null.</summary>
    public TournamentHistoryEntry? PendingTournamentResult => PlayerModel?.PendingTournamentReward();

    /// <summary>
    /// How long a tournament join or claim waits for the server's response. The server responds immediately, so
    /// the timeout only applies when a response is lost. Both requests are safe to repeat, so the fallback
    /// responses ask the player to try again.
    /// </summary>
    static readonly TimeSpan TournamentAnswerTimeout = TimeSpan.FromSeconds(30);

    readonly AwaitedAnswer<TournamentJoinResponse>  _tournamentJoinAnswer  = new AwaitedAnswer<TournamentJoinResponse>(TournamentAnswerTimeout);
    readonly AwaitedAnswer<TournamentClaimResponse> _tournamentClaimAnswer = new AwaitedAnswer<TournamentClaimResponse>(TournamentAnswerTimeout);

    /// <summary>
    /// Requests to join the running season and returns the server's response.
    /// <para>
    /// Nothing changes locally before the response. The league manager chooses the division and can refuse.
    /// </para>
    /// </summary>
    public Task<TournamentJoinResponse> JoinTournamentAsync()
    {
        Task<TournamentJoinResponse> answer = _tournamentJoinAnswer.Start(TournamentJoinResponse.Refused(0, TournamentJoinRefusal.Unavailable), out int requestId);
        SendOnMainThread(new TournamentJoinRequest(requestId));
        return answer;
    }

    /// <summary>Requests a milestone or placement reward and returns the server's response.</summary>
    public Task<TournamentClaimResponse> ClaimTournamentAsync(TournamentClaimRequest request)
    {
        // The fallback refusal is not an ActionResults name, because it does not come from the server.
        Task<TournamentClaimResponse> answer = _tournamentClaimAnswer.Start(new TournamentClaimResponse(0, request.Kind, paid: false, refusal: "NoAnswer"), out int requestId);
        TournamentClaimRequest        sent   = request.WithRequestId(requestId);
        SendOnMainThread(sent);
        return answer;
    }

    void OnTournamentJoinResponse(TournamentJoinResponse response)
    {
        _tournamentJoinAnswer.Answer(response.RequestId, response);
        NotifyStateChanged();
    }

    void OnTournamentClaimResponse(TournamentClaimResponse response)
    {
        _tournamentClaimAnswer.Answer(response.RequestId, response);
        NotifyStateChanged();
    }

    /// <summary>
    /// Completes pending tournament join and claim requests with their fallback responses, because no response will
    /// arrive.
    /// </summary>
    protected override void OnSessionEnded()
    {
        _tournamentJoinAnswer.Abandon();
        _tournamentClaimAnswer.Abandon();
    }

    /// <summary>
    /// Called when the player's tournament state changes, such as joining, a paid milestone or a claimed placement.
    /// </summary>
    public void OnTournamentChanged() => NotifyModelChanged();

    #endregion

    #region The daily reward

    /// <summary>
    /// Whether the SDK's client-side journal consistency checks are enabled for this session.
    /// <para>
    /// The checks report errors only by logging, so a test that asserts no errors were logged also checks this
    /// flag to confirm the checks ran. The server enables them in local and development environments only.
    /// </para>
    /// </summary>
    public bool ConsistencyChecksEnabled =>
        (Session?.PlayerContext as Metaplay.Core.Client.DefaultPlayerClientContext)?.Journal?.ConsistencyChecksEnabled ?? false;

    /// <summary>
    /// Raised when the daily reward claim action runs on this client's player model, which is when the balance and
    /// streak change. A reveal started from this event therefore always shows a granted reward.
    /// </summary>
    public event Action? DailyRewardClaimed;

    /// <summary>Raised when the server refuses a daily reward claim, with the reason.</summary>
    public event Action<DailyRewardRefusal>? DailyRewardRefused;

    /// <summary>
    /// Requests today's daily reward.
    /// <para>
    /// The request has no parameters. The server decides the day, the reward and the new streak
    /// (<c>docs/daily-rewards.md</c>). The wallet changes when the server's action reaches this client's
    /// timeline, not when the request is sent.
    /// </para>
    /// </summary>
    /// <returns>Whether the request was sent. False if there is no session.</returns>
    public bool ClaimDailyReward()
    {
        if (PlayerModel == null)
            return false;

        SendOnMainThread(PlayerDailyRewardClaimRequest.Instance);
        return true;
    }

    void OnDailyRewardClaimResponse(PlayerDailyRewardClaimResponse response)
    {
        if (!response.IsAccepted())
            DailyRewardRefused?.Invoke(response.Refusal);

        // An accepted claim is handled when the grant action runs, in IPlayerModelClientListener.OnDailyRewardClaimed.
        NotifyStateChanged();
    }

    #endregion

    #region Missions

    /// <summary>
    /// Claims a finished mission's reward. The action contains only the instance id. The reward and whether it
    /// is claimable are read from the player state when the action runs. Returns null if the id is empty or there
    /// is no session.
    /// </summary>
    public MetaActionResult? ClaimMissionReward(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return null;

        return ExecuteInSession(new PlayerClaimMissionReward(MissionInstanceId.FromString(instanceId)));
    }

    void IPlayerModelClientListener.OnMissionsChanged() => NotifyModelChanged();

    #endregion

    #region The first-week event

    /// <summary>
    /// Claims a completed first-week day's reward. The action contains only the day id. The reward and whether the
    /// day is complete are read from the player state and schedule when the action runs. Returns null if the id is
    /// empty or there is no session.
    /// </summary>
    public MetaActionResult? ClaimFirstWeekReward(string dayId)
    {
        if (string.IsNullOrEmpty(dayId))
            return null;

        return ExecuteInSession(new PlayerClaimFirstWeekReward(FirstWeekDayId.FromString(dayId)));
    }

    void IPlayerModelClientListener.OnFirstWeekChanged() => NotifyModelChanged();

    #endregion

    #region The weekly themed event

    /// <summary>
    /// Claims the reward of a weekly event whose target the player has reached. Returns null if the id is empty or
    /// not a valid <see cref="MetaGuid"/>, or there is no session.
    /// </summary>
    public MetaActionResult? ClaimWeeklyEventReward(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            return null;

        if (!MetaGuid.TryParse(eventId, out MetaGuid parsed, out string _))
            return null;

        return ExecuteInSession(new PlayerClaimWeeklyEventReward(parsed));
    }

    void IPlayerModelClientListener.OnWeeklyEventChanged() => NotifyModelChanged();

    #endregion

    #region Segmented, wallet-priced offers

    /// <summary>
    /// Whether the player has personalized offers enabled. True when there is no session, which is the default value.
    /// </summary>
    public bool PersonalizedOffersEnabled => PlayerModel?.PersonalizedOffersEnabled ?? true;

    /// <summary>
    /// Turns personalized offers on or off, from a control on the Shop screen (<c>docs/offers.md</c>, "Segment
    /// targeting"). Returns null if there is no session.
    /// </summary>
    public MetaActionResult? SetPersonalizedOffersEnabled(bool enabled)
    {
        return ExecuteInSession(new PlayerSetPersonalizedOffersEnabled(enabled));
    }

    /// <summary>
    /// Re-evaluates which offer groups can activate for the player, as the SDK documents for
    /// <c>PlayerRefreshMetaOffers</c> when the player opens the shop. It changes nothing when nothing is due, so
    /// the Shop calls it on every visit.
    /// </summary>
    public void RefreshOffers()
    {
        ExecuteInSession(new PlayerRefreshMetaOffers(null));
    }

    /// <summary>
    /// The first group in <c>MetaOfferContainingGroups</c> in which <paramref name="offerId"/> is purchasable now,
    /// or null if none. An offer can be in several groups, such as a featured group and the catalogue group, and
    /// the first group in the list may not be active.
    /// </summary>
    private OfferGroupInfo? PurchasableGroupFor(MetaOfferId offerId)
    {
        if (PlayerModel == null || !PlayerModel.GameConfig.MetaOfferContainingGroups.TryGetValue(offerId, out List<MetaOfferGroupInfoBase> groups))
            return null;

        foreach (MetaOfferGroupInfoBase groupInfoBase in groups)
        {
            if (groupInfoBase is not OfferGroupInfo groupInfo)
                continue;

            MetaOfferStatus status = PlayerModel.MetaOfferGroups.GetOfferStatus(PlayerModel, groupInfo, PlayerModel.GameConfig.Offers[offerId]);
            if (PlayerModel.MetaOfferGroups.OfferIsPurchasable(status))
                return groupInfo;
        }

        return null;
    }

    /// <summary>
    /// Buys an offer priced in an in-game currency with the SDK's <c>PlayerPurchaseInGameCurrencyMetaOffer</c>
    /// action. Returns null if there is no session, or if the offer does not exist or is not purchasable in any
    /// group.
    /// </summary>
    public MetaActionResult? PurchaseWalletOffer(string offerId)
    {
        if (string.IsNullOrEmpty(offerId) || PlayerModel == null)
            return null;

        MetaOfferId id = MetaOfferId.FromString(offerId);
        if (!PlayerModel.GameConfig.Offers.TryGetValue(id, out OfferInfo offerInfo))
            return null;

        OfferGroupInfo? groupInfo = PurchasableGroupFor(id);
        if (groupInfo == null)
            return null;

        return ExecuteInSession(new PlayerPurchaseInGameCurrencyMetaOffer(groupInfo, offerInfo, analyticsContext: null));
    }

    #endregion

    #region Cosmetics

    /// <summary>
    /// Buys a cosmetic, which also equips it (<c>docs/cosmetics.md</c>). The action contains only the catalogue
    /// id. The price, the slot and whether the item is for sale are read from the game config when the action
    /// runs. Returns null if the id is empty or there is no session.
    /// </summary>
    public MetaActionResult? BuyCosmetic(string cosmeticId)
    {
        if (string.IsNullOrEmpty(cosmeticId))
            return null;

        return ExecuteInSession(new PlayerBuyCosmetic(CosmeticId.FromString(cosmeticId)));
    }

    /// <summary>
    /// Equips an owned cosmetic in its catalogue slot. Returns null if the id is empty or there is no session.
    /// </summary>
    public MetaActionResult? EquipCosmetic(string cosmeticId)
    {
        if (string.IsNullOrEmpty(cosmeticId))
            return null;

        return ExecuteInSession(new PlayerEquipCosmetic(CosmeticId.FromString(cosmeticId)));
    }

    /// <summary>
    /// Marks all unacknowledged cosmetic acquisitions as seen. Returns null without executing an action if there is
    /// no session or nothing to acknowledge.
    /// </summary>
    public MetaActionResult? AcknowledgeCosmetics()
    {
        if (PlayerModel?.Cosmetics.HasUnacknowledged != true)
            return null;

        return ExecuteInSession(new PlayerAcknowledgeCosmetics());
    }

    void IPlayerModelClientListener.OnCosmeticsChanged() => NotifyModelChanged();

    #endregion

    #region The spin wheel

    /// <summary>
    /// Raised when the player's spin wheel state changes on this client's model, such as when a spin's token is
    /// spent and its prize granted. A reveal started from this event therefore always shows a granted result.
    /// </summary>
    public event Action? WheelSpinResolved;

    /// <summary>Raised when the server refuses a spin, with the reason.</summary>
    public event Action<SpinRefusal>? WheelSpinRefused;

    /// <summary>
    /// Requests a wheel spin.
    /// <para>
    /// The request contains only the expected spin ordinal. The server decides the sector, the prize and whether
    /// the spin is allowed (<c>docs/spin-wheel.md</c>). The token and prize change when the server's action
    /// reaches this client's timeline, and the reveal runs after that.
    /// </para>
    /// </summary>
    /// <returns>Whether the request was sent. False if there is no session.</returns>
    public bool RequestWheelSpin()
    {
        PlayerModel? player = PlayerModel;
        if (player == null)
            return false;

        int expectedOrdinal = player.SpinWheel.NextOrdinal;
        SendOnMainThread(new PlayerWheelSpinRequest(expectedOrdinal));
        return true;
    }

    /// <summary>
    /// Marks the pending spin receipt as seen, when the player presses Done or Spin again.
    /// <para>
    /// It is a client action instead of a request because it changes only the player's spin wheel state, with no
    /// balance change. Returns null if there is no session or no pending receipt.
    /// </para>
    /// </summary>
    public MetaActionResult? AcknowledgeWheelSpin()
    {
        if (PlayerModel?.SpinWheel.HasPendingReceipt != true)
            return null;

        return ExecuteInSession(new PlayerAcknowledgeWheelSpin());
    }

    void OnWheelSpinResponse(PlayerWheelSpinResponse response)
    {
        if (!response.IsAccepted())
            WheelSpinRefused?.Invoke(response.Refusal);

        // An accepted spin is handled when the settlement action runs, in IPlayerModelClientListener.OnSpinWheelChanged.
        NotifyStateChanged();
    }

    void IPlayerModelClientListener.OnSpinWheelChanged()
    {
        NotifyModelChanged();
        WheelSpinResolved?.Invoke();
    }

    #endregion

    #region Shell observations

    /// <summary>
    /// Executes a <c>PlayerObserveScreenViewed</c> action (<c>SharedCode/Analytics/ShellObservationActions.cs</c>).
    /// Does nothing if there is no session.
    /// <para>
    /// The action changes no state. It is refused for an unknown screen, but <see cref="ShellObservationPolicy"/>
    /// only reports known screens, so the result is ignored.
    /// </para>
    /// </summary>
    public void ObserveScreenViewed(ShellScreen screen)
    {
        ExecuteInSession(new PlayerObserveScreenViewed(screen));
    }

    /// <summary>
    /// Executes a <c>PlayerObservePromotedEntrySelected</c> action. Does nothing if there is no session.
    /// </summary>
    public void ObservePromotedEntrySelected(PromotedEntryPlacement placement, ShellScreen from, ShellScreen destination)
    {
        ExecuteInSession(new PlayerObservePromotedEntrySelected(placement, from, destination));
    }

    #endregion

    #region The match

    /// <summary>
    /// The player's current match, or null if there is none.
    /// <para>
    /// Virtual, like <see cref="ConnectAsync"/>, so that a host without a session can return a model it built and
    /// render the real table page in a chosen state.
    /// </para>
    /// </summary>
    public virtual MatchModel? Match => MatchClient.Phase == MultiplayerEntityClientPhase.EntityActive ? MatchClient.Model : null;

    /// <summary>
    /// Whether the match slot has no entity. Unlike a null <see cref="Match"/>, it is false while a match is
    /// attaching or there is no session, so UI that offers to find a match does not appear briefly during a
    /// reconnect.
    /// </summary>
    public bool HasNoTable => MatchClient.Phase == MultiplayerEntityClientPhase.NoEntity;

    /// <summary>
    /// The number of times the match channel has activated. The table page uses it instead of the match id to
    /// detect a new activation, because a reconnect to the same match is also a new activation.
    /// </summary>
    public int MatchActivationCount => MatchClient.ActivationCount;

    /// <summary>
    /// The matchmaking status last reported by the player actor. The searching dialog is shown while this is a
    /// waiting status (see <see cref="IsMatchmaking"/>).
    /// <para>
    /// Virtual so that a host without a session can set it. <see cref="IsMatchmaking"/> reads it through this
    /// property.
    /// </para>
    /// </summary>
    public virtual MatchmakingStatus MatchmakingStatus { get; private set; } = MatchmakingStatus.NotSearching;

    /// <summary>Whether the searching dialog is shown.</summary>
    public bool IsMatchmaking => MatchmakingWaitText.IsWaiting(MatchmakingStatus);

    /// <summary>
    /// The latest time by which the server expects the player to be seated, or <see cref="MetaTime.Epoch"/> until
    /// the server's first status update after the request. The searching dialog shows no countdown until then, and
    /// counts down using the client's estimate of the server clock, because the server set the value.
    /// Virtual, like <see cref="MatchmakingStatus"/>, so that a host without a session can set both.
    /// </summary>
    public virtual MetaTime MatchmakingSeatDeadlineAt { get; private set; } = MetaTime.Epoch;

    /// <summary>
    /// How long the searching dialog waits without any status update from the server before this client sets the
    /// status to <c>MatchmakingStatus.Unavailable</c>. Every status update restarts it, so it limits the time
    /// without updates, not the whole search. The server's own search and seating timeouts are shorter and are
    /// reported as updates.
    /// <para>
    /// The dialog is shown before the request is sent. Without this timeout, a request lost on a dead connection
    /// would leave the dialog open indefinitely.
    /// </para>
    /// </summary>
    private static readonly TimeSpan MatchmakingSilenceTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Incremented each time the silence timeout restarts. A timeout that finds a different value does nothing.
    /// The server's matchmaking timeouts use the same pattern.
    /// </summary>
    private int _matchmakingTimeoutGeneration;

    /// <summary>
    /// Requests matchmaking. The request has no parameters and the response contains no match id: the match
    /// arrives as an entity association on the match slot.
    /// <para>
    /// The status is set to <c>MatchmakingStatus.Searching</c> before the request is sent, so the dialog
    /// opens immediately. The server's status update replaces it, including on a refusal.
    /// </para>
    /// </summary>
    public void EnterMatchmaking()
    {
        SetMatchmaking(MatchmakingStatus.Searching, MetaTime.Epoch);
        RestartMatchmakingSilenceTimeout();
        NotifyStateChanged();
        SendOnMainThread(new MatchmakingEnterRequest());
    }

    /// <summary>
    /// Requests to leave matchmaking. The status is not changed here, because the server refuses a cancel that
    /// arrives after the player's seat is committed. The server's status update sets the new status.
    /// </summary>
    public void CancelMatchmaking()
    {
        SendOnMainThread(new MatchmakingCancelRequest());
    }

    void OnMatchmakingStatusUpdate(MatchmakingStatusUpdate update)
    {
        SetMatchmaking(update.Status, update.SeatDeadlineAt);
        RestartMatchmakingSilenceTimeout();
        NotifyStateChanged();
    }

    /// <summary>
    /// Restarts the silence timeout. Earlier timeouts are not cancelled, but do nothing when they find that
    /// <see cref="_matchmakingTimeoutGeneration"/> has changed.
    /// </summary>
    void RestartMatchmakingSilenceTimeout()
    {
        _matchmakingTimeoutGeneration++;
        _ = WaitOutMatchmakingSilenceAsync(_matchmakingTimeoutGeneration);
    }

    async Task WaitOutMatchmakingSilenceAsync(int generation)
    {
        await Task.Delay(MatchmakingSilenceTimeout);

        if (_matchmakingTimeoutGeneration != generation || !IsMatchmaking)
            return;

        // No update arrived within any of the server's own timeouts, so no update is coming. Show matchmaking as
        // unavailable instead of searching indefinitely.
        SetMatchmaking(MatchmakingStatus.Unavailable, MetaTime.Epoch);
        NotifyStateChanged();
    }

    /// <summary>
    /// Sends a move. The board does not change until the host applies the move, because only the host writes the
    /// match timeline.
    /// </summary>
    public void PlayCard(Card card)
    {
        LastMoveRefusal = null;
        MetaplaySDK.RunOnMainThreadAsync(() => MatchClient.PlayCard(card));
    }

    void OnMoveRefused(MatchMoveRefused refusal)
    {
        LastMoveRefusal = refusal;
        NotifyStateChanged();
    }

    /// <summary>
    /// Called when the match channel attaches, detaches or switches matches. Clears <see cref="LastMoveRefusal"/>,
    /// because refusals are matched by play index, which every match starts from the same value, so an old
    /// refusal could match a move at the new match.
    /// </summary>
    void OnMatchPhaseChanged()
    {
        LastMoveRefusal = null;

        // A match on the slot ends matchmaking, whatever the last status update said. The status update and the
        // entity association are separate messages, and the association can arrive first.
        if (Match != null)
        {
            SetMatchmaking(MatchmakingStatus.NotSearching, MetaTime.Epoch);
        }

        NotifyStateChanged();
    }

    #endregion

    /// <summary>
    /// Called when a session starts. Sets this service as the player model's client listeners.
    /// </summary>
    protected override void OnSessionStarted(MetaplaySession session)
    {
        PlayerModel? playerModel = session.PlayerContext.Model as PlayerModel;
        if (playerModel != null)
        {
            playerModel.ClientListener = this;

            // The SDK's listener, separate from the game's. The client learns that a purchase receipt was accepted
            // only through it.
            playerModel.ClientListenerCore = this;
        }

        // The demo purchase state tracks only purchases started in this session. FinishPendingInAppPurchases
        // finishes purchases left pending by an earlier session without showing a reveal.
        DemoPurchase        = DemoPurchaseState.Idle;
        DemoPurchaseProduct = null;

        FinishPendingInAppPurchases();

        // Reset the server clock estimate, because a new session can be against a different host, such as the
        // offline server instead of the game server. The estimator keeps its best sample, so a zero-latency sample
        // from the previous host would be preferred over real measurements until it expires.
        ServerClockEstimate.Shared.Reset();

        // The player actor removes the player from matchmaking when the session ends, so reset the status.
        SetMatchmaking(MatchmakingStatus.NotSearching, MetaTime.Epoch);

        // A new session replaces the fixture state with the player's real state, but no model listener fires
        // because the model was not modified. Raise ModelChanged so that MetaStateService rebuilds.
        NotifyModelChanged();
    }

    #region Demo purchases

    /// <summary>The stage of the current demo purchase. Only one purchase is tracked at a time.</summary>
    public enum DemoPurchaseState
    {
        /// <summary>
        /// No purchase in progress, or the last one has been cleared with <see cref="ClearDemoPurchase"/>.
        /// </summary>
        Idle,

        /// <summary>
        /// Offer purchases only: the offer has been assigned as the product's pending dynamic content, and the
        /// client waits for the server to confirm it before reporting the store purchase
        /// (<see cref="ContinueOfferPurchaseIfConfirmed"/>).
        /// </summary>
        Preparing,

        /// <summary>The purchase has been reported and the server has not validated the receipt yet.</summary>
        Validating,

        /// <summary>Validated and granted. The wallet has already been updated.</summary>
        Granted,

        /// <summary>The server refused the receipt, or a purchase action was refused.</summary>
        Failed,
    }

    /// <summary>The stage of the current or last demo purchase.</summary>
    public DemoPurchaseState DemoPurchase { get; private set; } = DemoPurchaseState.Idle;

    /// <summary>The product that <see cref="DemoPurchase"/> refers to, or null if none.</summary>
    public InAppProductId? DemoPurchaseProduct { get; private set; }

    /// <summary>
    /// Buys an offer with a demo real-money price through the SDK's dynamic-content in-app purchase flow, which
    /// includes the offer's reward, group and precursor state in the purchase (<c>docs/offers.md</c>, "In-app
    /// purchase offers"). This method only executes <c>PlayerPreparePurchaseMetaOffer</c>, which assigns the offer
    /// as the product's pending dynamic content. When the server confirms it,
    /// <see cref="ContinueOfferPurchaseIfConfirmed"/> continues with the SDK's standard client-driven purchase flow,
    /// using the SDK's Development platform instead of a store.
    /// <para>
    /// There is no store to sign a receipt, so the client creates one that the server's development validator
    /// accepts. After that the flow is the same as for a real purchase: <c>PlayerInAppPurchased</c> reports the
    /// purchase, the server validates it, and <c>PlayerClaimPendingInAppPurchase</c> grants the contents. This
    /// method grants nothing itself.
    /// </para>
    /// </summary>
    public void StartDemoOfferPurchase(string offerId)
    {
        MetaOfferId id = MetaOfferId.FromString(offerId);
        IPlayerClientContext? context = Session?.PlayerContext;

        OfferInfo? offerInfo = context != null && PlayerModel != null && PlayerModel.GameConfig.Offers.TryGetValue(id, out OfferInfo? found) ? found : null;
        OfferGroupInfo? groupInfo = offerInfo != null ? PurchasableGroupFor(id) : null;

        if (context == null || offerInfo?.InAppProduct == null || groupInfo == null)
        {
            Console.WriteLine($"[{GetType().Name}] Cannot buy offer {offerId}: no session, no such offer, or not currently purchasable");
            DemoPurchase        = DemoPurchaseState.Failed;
            DemoPurchaseProduct = offerInfo?.InAppProduct?.Ref.ProductId;
            NotifyStateChanged();
            return;
        }

        DemoPurchase        = DemoPurchaseState.Preparing;
        DemoPurchaseProduct = offerInfo.InAppProduct.Ref.ProductId;
        NotifyStateChanged();

        MetaplaySDK.RunOnMainThreadAsync(() =>
            RunPurchaseAction(new PlayerPreparePurchaseMetaOffer(groupInfo, offerInfo, analyticsContext: null)));
    }

    /// <summary>
    /// Continues an offer purchase after the server confirms the pending dynamic content. It is checked on every
    /// client update because the confirmation is a server action without a listener callback, unlike
    /// <c>InAppPurchaseValidated</c> and <c>InAppPurchaseClaimed</c>.
    /// </summary>
    void ContinueOfferPurchaseIfConfirmed()
    {
        if (DemoPurchase != DemoPurchaseState.Preparing || DemoPurchaseProduct == null || PlayerModel == null)
            return;

        if (!PlayerModel.PendingDynamicPurchaseContents.TryGetValue(DemoPurchaseProduct, out PendingDynamicPurchaseContent pending)
         || pending.Status != PendingDynamicPurchaseContentStatus.ConfirmedByServer)
            return;

        InAppProductId productId = DemoPurchaseProduct;
        InAppProductInfoBase? productInfo = PlayerModel.GameConfig.InAppProducts.GetValueOrDefault(productId);
        if (productInfo == null)
        {
            DemoPurchase = DemoPurchaseState.Failed;
            NotifyStateChanged();
            return;
        }

        DemoPurchase = DemoPurchaseState.Validating;
        NotifyStateChanged();

        // Use a new transaction id for each attempt. The server refuses a transaction id it has already seen, which
        // is how it rejects a replayed receipt.
        string transactionId = DemoPurchaseReceipt.NewTransactionId(productId, Guid.NewGuid());

        // Test setting (docs/testing.md, "Live-server tests"): `?demoReceipt=tampered` signs the receipt incorrectly,
        // so a test can check that the server validates receipts and refuses this one.
        bool shouldTamperReceipt = TestQuerySettings.TryGet(TestQuerySettings.ReadLocationSearch(), "demoReceipt") == "tampered";

        MetaplaySDK.RunOnMainThreadAsync(() =>
            RunPurchaseAction(new PlayerInAppPurchased(DemoPurchaseReceipt.CreatePurchaseEvent(productInfo, transactionId, shouldTamperReceipt))));
    }

    /// <summary>
    /// Executes one purchase flow action and sets <see cref="DemoPurchase"/> to
    /// <see cref="DemoPurchaseState.Failed"/> if the action is refused locally.
    /// <para>
    /// An action can be refused locally, for example for a malformed event, a transaction id that is already
    /// pending, or the SDK's limit on pending purchases. No listener fires in that case, so without this the Shop
    /// would wait for the server indefinitely.
    /// </para>
    /// </summary>
    void RunPurchaseAction(PlayerActionBase action)
    {
        IPlayerClientContext? context = Session?.PlayerContext;
        if (context == null)
            return;

        MetaActionResult result = context.ExecuteAction(action);
        if (result.IsSuccess)
            return;

        Console.WriteLine($"[{GetType().Name}] {action.GetType().Name} was refused locally: {result}");

        if (DemoPurchase is DemoPurchaseState.Preparing or DemoPurchaseState.Validating)
        {
            DemoPurchase = DemoPurchaseState.Failed;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Claims or clears every pending purchase that the server has already validated, for example when the tab was
    /// closed between validation and claim. A validated purchase that is never claimed is never granted, keeps its
    /// transaction id used, and counts toward the SDK's limit on pending purchases. The actions are collected
    /// before any runs, because running them modifies the dictionary being iterated.
    /// </summary>
    void FinishPendingInAppPurchases()
    {
        PlayerModel? player = PlayerModel;
        if (player == null)
            return;

        List<PlayerActionBase> pending = new List<PlayerActionBase>();

        foreach (InAppPurchaseEvent purchase in player.PendingInAppPurchases.Values)
        {
            // PendingValidation needs no action, because the server resumes validation at session start.
            if (purchase.Status == InAppPurchaseStatus.Successful)
                pending.Add(new PlayerClaimPendingInAppPurchase(purchase.TransactionId));
            else if (purchase.Status == InAppPurchaseStatus.ReceiptAlreadyUsed)
                pending.Add(new PlayerClearPendingDuplicateInAppPurchase(purchase.TransactionId));
        }

        if (pending.Count == 0)
            return;

        Console.WriteLine($"[{GetType().Name}] Finishing {pending.Count} purchase(s) left pending by an earlier session");

        MetaplaySDK.RunOnMainThreadAsync(() =>
        {
            foreach (PlayerActionBase action in pending)
                RunPurchaseAction(action);
        });
    }

    /// <summary>Resets the demo purchase state after a finished purchase, so the Shop can offer the next one.</summary>
    public void ClearDemoPurchase()
    {
        DemoPurchase        = DemoPurchaseState.Idle;
        DemoPurchaseProduct = null;
        NotifyStateChanged();
    }

    /// <summary>
    /// Called when the server has validated a receipt. A valid purchase is claimed, which grants the contents. A
    /// receipt the server has already seen is cleared instead, because only the server can detect reuse.
    /// <para>
    /// Both actions run on a later main-thread callback, because this callback runs inside the server action that
    /// delivered the result, and the SDK's own clients do not execute actions from inside another action.
    /// </para>
    /// </summary>
    void IPlayerModelClientListenerCore.InAppPurchaseValidated(InAppPurchaseEvent ev)
    {
        IPlayerClientContext? context = Session?.PlayerContext;
        if (context == null)
            return;

        // Test setting: `?demoClaim=skip` leaves a validated purchase unclaimed, as a closed tab would, so a test can
        // check that the next session's FinishPendingInAppPurchases claims it.
        if (TestQuerySettings.TryGet(TestQuerySettings.ReadLocationSearch(), "demoClaim") == "skip")
        {
            Console.WriteLine($"[{GetType().Name}] Leaving purchase {ev.TransactionId} unclaimed (demoClaim=skip)");
            return;
        }

        if (ev.Status == InAppPurchaseStatus.Successful)
            MetaplaySDK.RunOnMainThreadAsync(() => RunPurchaseAction(new PlayerClaimPendingInAppPurchase(ev.TransactionId)));
        else
        {
            Console.WriteLine($"[{GetType().Name}] Purchase {ev.TransactionId} was already used; clearing it");
            MetaplaySDK.RunOnMainThreadAsync(() => RunPurchaseAction(new PlayerClearPendingDuplicateInAppPurchase(ev.TransactionId)));
            FinishDemoPurchase(ev, DemoPurchaseState.Failed);
        }
    }

    void IPlayerModelClientListenerCore.InAppPurchaseValidationFailed(InAppPurchaseEvent ev)
    {
        Console.WriteLine($"[{GetType().Name}] Purchase {ev.TransactionId} of {ev.ProductId} was refused: {ev.Status}");
        FinishDemoPurchase(ev, DemoPurchaseState.Failed);
    }

    /// <summary>Called when the contents have been granted and the wallet updated.</summary>
    void IPlayerModelClientListenerCore.InAppPurchaseClaimed(InAppPurchaseEvent ev) =>
        FinishDemoPurchase(ev, DemoPurchaseState.Granted);

    /// <summary>
    /// Sets the final state of the demo purchase, but only for the purchase this session is tracking. Purchases
    /// finished by <see cref="FinishPendingInAppPurchases"/> do not show a reveal. The wallet still updates through
    /// its own listener.
    /// </summary>
    void FinishDemoPurchase(InAppPurchaseEvent ev, DemoPurchaseState state)
    {
        if (DemoPurchase != DemoPurchaseState.Validating || DemoPurchaseProduct != ev.ProductId)
            return;

        DemoPurchase = state;
        NotifyStateChanged();
    }

    #endregion

    #region Model client listeners (trigger UI re-render)

    /// <summary>
    /// Raised when displayed player model state changes, such as the record, name, a balance or the tournament
    /// state, and when a session starts.
    /// <para>
    /// Unlike <see cref="MetaplayClientServiceBase{T}.OnStateChanged"/>, which is raised on every client update,
    /// it is raised only on these changes. <see cref="MetaStateService"/> uses it to rebuild its snapshot.
    /// </para>
    /// </summary>
    public event Action? ModelChanged;

    /// <summary>Raises <c>OnStateChanged</c> and <see cref="ModelChanged"/>.</summary>
    private void NotifyModelChanged()
    {
        NotifyStateChanged();
        ModelChanged?.Invoke();
    }

    void IPlayerModelClientListener.OnRecordChanged() => NotifyModelChanged();
    void IPlayerModelClientListener.OnNameChanged(string name) => NotifyModelChanged();
    void IPlayerModelClientListener.OnWalletChanged() => NotifyModelChanged();
    void IPlayerModelClientListener.OnOffersChanged() => NotifyModelChanged();

    void IPlayerModelClientListener.OnDailyRewardClaimed()
    {
        NotifyModelChanged();
        DailyRewardClaimed?.Invoke();
    }

    void IMatchModelClientListener.OnCardPlayed(int seat, Card card) => NotifyStateChanged();
    void IMatchModelClientListener.OnTrickResolved(int winnerSeat) => NotifyStateChanged();
    void IMatchModelClientListener.OnMatchEnded() => NotifyStateChanged();

    #endregion
}
