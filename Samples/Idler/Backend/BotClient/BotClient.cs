// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Game.Logic.League;
using Game.Logic.Matchmaking;
using Game.Logic.TypeCodes;
using Metaplay.BotClient;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Guild;
using Metaplay.Core.Guild.Actions;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.League;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.BotClient
{
    public enum BotClientState
    {
        Connecting,
        Main,
    }

    // BotClient

    [EntityConfig]
    internal class BotClientConfig : BotClientConfigBase
    {
        public override Type EntityActorType => typeof(BotClient);
    }

    public class BotClient : BotClientBase, IPlayerModelClientListener
    {
        BotClientState State { get; set; } = BotClientState.Connecting;

        PlayerModel _playerModel => (PlayerModel)PlayerContext.Model;
        GuildModel GuildModel => (GuildModel)GuildContext?.CommittedModel;

        bool _leagueJoinRequestSent = false;
        IdlerLeagueClient _idlerLeagueClient;
        LeagueClient<IdlerPvPDivisionModel> _idlerPvPLeagueClient;
        
        protected override IMetaplaySubClient[] AdditionalSubClients => new IMetaplaySubClient[]
        {
            _idlerLeagueClient = new IdlerLeagueClient(ClientSlotGame.IdlerLeague),
            _idlerPvPLeagueClient = new LeagueClient<IdlerPvPDivisionModel>(ClientSlotGame.IdlerPvPLeague),
        };

        protected override void PreStart()
        {
            base.PreStart();
        }

        protected override void RegisterHandlers()
        {
            base.RegisterHandlers();
        }

        protected override string GetCurrentStateLabel() => State.ToString();

        protected override async Task OnUpdate()
        {
            // Tick current state (when connected)
            switch (State)
            {
                case BotClientState.Main:
                    await TickMainState();
                    break;
            }

            // Random guild ops
            await TickGuildLogic();
        }

        protected override Task OnNetworkMessage(MetaMessage message)
        {
            //_log.Debug("OnNetworkMessage: {Message}", PrettyPrint.Compact(message));
            switch (message)
            {
                case PlayerJoinIdleLeagueResponse _:
                    // Handled by IdlerLeagueClient
                    break;
                default:
                    _log.Warning("Unknown message received: {Message}", PrettyPrint.Compact(message));
                    break;
            }

            return Task.CompletedTask;
        }

        protected override Task OnSessionStartedAsync(BotSessionStartedArgs args)
        {
            State = BotClientState.Main;
            PlayerModel playerModel = (PlayerModel)args.PlayerModel;
            playerModel.ClientListener = this;
            return Task.CompletedTask;
        }

        async Task TickMainState()
        {
            RandomPCG rnd = RandomPCG.CreateNew();

            _log.Debug("Update player (currentTick={CurrentTick})..", _playerModel.CurrentTick);

            List<HappyHourModel> activeHappyHours = _playerModel.HappyHours.GetActiveStates(_playerModel).ToList();

            // Try to unlock any producer
            foreach (ProducerInfo producerInfo in _playerModel.GameConfig.Producers.Values)
            {
                // Check that producer not yet unlocked & category is Normal & has enough gold to unlock
                if (!_playerModel.Producers.ContainsKey(producerInfo.Id) && producerInfo.Category == ProducerCategory.Normal && _playerModel.Wallet.NumGold >= producerInfo.GetUnlockCost(activeHappyHours, _playerModel.GameConfig))
                    PlayerContext.ExecuteAction(new PlayerUnlockProducer(producerInfo.Id));
            }

            // Randomly try to upgrade a random producer
            if (rnd.NextInt(100) < 50)
            {
                // \note assumes that there's always at least one producer unlocked
                ProducerModel producer = rnd.Choice(_playerModel.Producers.Values);
                if (_playerModel.Wallet.NumGold >= producer.GetUpgradeCost(activeHappyHours, _playerModel.GameConfig))
                    PlayerContext.ExecuteAction(new PlayerUpgradeProducer(producer.Info.Id));
            }

            // Refresh MetaOffers every now and then
            if (rnd.NextInt(100) < 5)
            {
                MetaOfferGroupsRefreshInfo refreshInfo = _playerModel.GetMetaOfferGroupsRefreshInfo();
                if (refreshInfo.HasAny())
                    PlayerContext.ExecuteAction(new PlayerRefreshMetaOffers(refreshInfo));
            }

            // Try to make (fake) in-app purchases every now and then
            if (rnd.NextInt(100) < 5)
                TryStartInAppPurchase(rnd);

            // Send a matchmaking request and wait for the response
            if (rnd.NextInt(100) < 1)
            {
                IdleMatchingResponse matchingResponse = await MessageDispatcher.SendRequestAsync<IdleMatchingResponse>(
                    new IdleMatchingRequest());
                _log.Debug("Received matching response: success={IsSuccess}, won={DidWinBattle}.", matchingResponse.IsSuccess, matchingResponse.DidWinBattle);
            }

            TickLeaguesLogic();
        }

        protected override bool EnableInvalidPurchases => true;
        protected override bool EnableDuplicatePurchases => true;
        protected override float MaxPurchaseValidationDelaySeconds => 10f;

        void TryStartInAppPurchase(RandomPCG rnd)
        {
            // \note IAP history in PlayerModel is unbounded (for successful purchases), so for Idler's bot testing have have a limit for it.
            if (_playerModel.InAppPurchaseHistorySummary.TotalNumPurchases + _playerModel.PendingInAppPurchases.Count >= 20)
                return;

            // Idler has both static IAPs and MetaOffers. Use both, randomly.

            if (rnd.NextInt(100) < 50)
            {
                // Static IAP

                IEnumerable<InAppProductInfoBase> staticProductInfos = _playerModel.GameConfig.InAppProducts.Values.Where(iap => !iap.HasDynamicContent);
                if (!staticProductInfos.Any())
                    return;

                InAppProductInfoBase productInfo = rnd.Choice(staticProductInfos);
                StartFakeInAppPurchase(productInfo.ProductId);
            }
            else
            {
                // MetaOffer

                IEnumerable<MetaOfferGroupInfoBase> activeOfferGroupInfos = _playerModel.GameConfig.OfferGroups.Values.Where(groupInfo => _playerModel.MetaOfferGroups.IsActive(groupInfo, _playerModel));
                if (!activeOfferGroupInfos.Any())
                    return;

                MetaOfferGroupInfoBase offerGroupInfo = rnd.Choice(activeOfferGroupInfos);
                MetaOfferGroupModelBase offerGroupModel = _playerModel.MetaOfferGroups.TryGetState(offerGroupInfo.GroupId);
                IEnumerable<MetaOfferStatus> purchasableOffers =
                    _playerModel.MetaOfferGroups
                    .GetOffersInGroup(offerGroupInfo, _playerModel)
                    .Where(offerStatus => _playerModel.MetaOfferGroups.OfferIsPurchasable(offerStatus)
                                          && offerStatus.Info.InAppProduct != null /* Skip in-game currency offers */);

                if (!purchasableOffers.Any())
                    return;

                MetaOfferInfoBase offerInfo = rnd.Choice(purchasableOffers).Info;

                // If the purchase preparation is already pending, don't do it again.
                // \note Without this, there can be checksum mismatches because the confirmation of dynamic purchase contents is non-synchronized.
                //       That can trigger when there's overlapping multiple `PlayerPreparePurchaseMetaOffer`s and then a PlayerInAppPurchased action.
                // \todo Should be handled in SDK side in CanSetPendingDynamicInAppPurchase?
                //       Unlikely to happen in real clients but should still be handled on SDK side.
                if (_playerModel.PendingDynamicPurchaseContents.TryGetValue(offerInfo.InAppProduct.Ref.ProductId, out PendingDynamicPurchaseContent pendingContent)
                    && pendingContent.Status == PendingDynamicPurchaseContentStatus.RequestedByClient)
                {
                    return;
                }

                // If a purchase of this offer's IAP is currently pending, don't start a new one.
                if (_playerModel.PendingInAppPurchases.Values.Any(purchase => purchase.ProductId == offerInfo.InAppProduct.Ref.ProductId))
                    return;

                // \note BotClientBase will automatically start the fake purchase after the MetaOffer purchase preparation has completed.
                PlayerContext.ExecuteAction(new PlayerPreparePurchaseMetaOffer(offerGroupInfo, offerInfo, analyticsContext: null));
            }
        }

        async Task TickGuildLogic()
        {
            Random random = Random.Shared;

            // discovery every now and then
            if (!GuildClient.HasOngoingGuildDiscovery && random.NextDouble() < 0.005)
            {
                GuildClient.DiscoverGuilds(OnGuildDiscoveryResponse);
            }

            // search every now and then
            if (!GuildClient.HasOngoingGuildSearch && random.NextDouble() < 0.001)
            {
                GuildSearchParams searchParams = new GuildSearchParams(
                    searchString: GetRandomSubstring(random, GenerateRandomGuildName())
                    );
                if (!string.IsNullOrWhiteSpace(searchParams.SearchString))
                    GuildClient.SearchGuilds(searchParams, OnGuildSearchResponse);
            }

            // leave every now and then
            bool canLeave = (GuildClient.Phase == GuildClientPhase.GuildActive);
            if (canLeave && random.NextDouble() < 0.001 && (GuildClient.GuildContext.CommittedModel as GuildModel).MemberCount > 1)
            {
                _log.Debug("Leaving guild");
                GuildClient.LeaveGuild();
            }

            // create guilds every now and then
            if (GuildClient.Phase == GuildClientPhase.NoGuild && random.NextDouble() < 0.002)
            {
                GuildCreationRequestParams creationParams = new GuildCreationRequestParams();
                creationParams.DisplayName = GenerateRandomGuildName();
                creationParams.Description = GenerateRandomGuildDescription();
                GuildRequirementsValidator guildRequirements = IntegrationRegistry.Get<GuildRequirementsValidator>();
                if (await guildRequirements.ValidateDisplayNameAsync(creationParams.DisplayName)
                    && await guildRequirements.ValidateDescriptionAsync(creationParams.Description))
                {
                    GuildClient.BeginCreateGuild(creationParams, onCompletion: null);
                }
            }

            // use invite links every now and then
            if (GuildClient.Phase == GuildClientPhase.NoGuild && random.NextDouble() < 0.002)
            {
                GuildInviteSample invitationToUse = RandomPCG.CreateFromSeed((ulong)random.NextInt64()).Choice(GuildInviteSamples);
                if (invitationToUse != null)
                    GuildClient.BeginInspectGuildInviteCode(invitationToUse.InviteCode, DefaultHandleGuildInspectInvitationResult);
            }

            // actions every now and then
            if (GuildClient.Phase == GuildClientPhase.GuildActive && random.NextDouble() < 0.002)
            {
                static bool IsInBucket(ref int remainingPercent, int bucketSize)
                {
                    if (remainingPercent <= 0)
                        return false;
                    remainingPercent -= bucketSize;
                    if (remainingPercent <= 0)
                        return true;
                    return false;
                }
                void TryEnqueueGuildAction(GuildActionBase action)
                {
                    action.InvokingPlayerId = _actualPlayerId;
                    if (ModelUtil.DryRunAction(GuildContext.CommittedModel, action).IsSuccess)
                        GuildContext.EnqueueAction(action);
                }

                int remainingPercent = random.Next(0, 100);

                if (IsInBucket(ref remainingPercent, 10))
                {
                    TryEnqueueGuildAction(new GuildPokeMember(targetPlayerId: RandomPCG.CreateNew().Choice(GuildModel.Members.Keys)));
                }
                else if (IsInBucket(ref remainingPercent, 3))
                {
                    TryEnqueueGuildAction(new GuildMemberKick(targetPlayerId: RandomPCG.CreateNew().Choice(GuildModel.Members.Keys), kickReasonOrNull: null));
                }
                else if (IsInBucket(ref remainingPercent, 3))
                {
                    // try some random promotion/demotion until we find something that causes effects
                    for (int i = 0; i < 30; ++i)
                    {
                        EntityId target = RandomPCG.CreateNew().Choice(GuildModel.Members.Keys);
                        GuildMemberRole role  = RandomPCG.CreateNew().Choice((GuildMemberRole[])System.Enum.GetValues(typeof(GuildMemberRole)));
                        if (!((IGuildModelBase)GuildContext.CommittedModel).HasPermissionToChangeRoleTo(_actualPlayerId, target, role))
                            continue;
                        MetaDictionary<EntityId, GuildMemberRole> changes = ((IGuildModelBase)GuildContext.CommittedModel).ComputeRoleChangesForRoleEvent(GuildMemberRoleEvent.MemberEdit, target, role);
                        if (changes.Count == 0)
                            continue;
                        TryEnqueueGuildAction(new GuildMemberEditRole(target, role, changes));
                        break;
                    }
                }
                else if (IsInBucket(ref remainingPercent, 3))
                {
                    GuildClient.ExecuteGuildTransaction(new GuildSellPokes.Transaction(numPokesAttemptingToSell: 1), logOnAbort: false);
                }
                else if (IsInBucket(ref remainingPercent, 3))
                {
                    GuildClient.ExecuteGuildTransaction(new GuildBuyVanity.Transaction(numVanityAttemptingToBuy: 1), logOnAbort: false);
                }
                else if (IsInBucket(ref remainingPercent, 3))
                {
                    GuildClient.ExecuteGuildTransaction(new GuildClaimVanityRankReward.Transaction(), logOnAbort: false);
                }
                else if (IsInBucket(ref remainingPercent, 3))
                {
                    GuildClient.BeginCreateGuildInviteCode(expirationDuration: MetaDuration.FromMinutes(30), usageLimit: 0, onResponse: DefaultHandleGuildCreateInvitationResult);
                }
            }

            // close views every now and then
            if (GuildClient.GuildViews.Count > 0 && random.NextDouble() < 0.02)
            {
                ForeignGuildContext chosenView = RandomPCG.CreateNew().Choice(GuildClient.GuildViews);
                chosenView.Dispose();
            }
        }

        void TickLeaguesLogic()
        {
            // Try to join leagues if we're not in one.
            if ((_playerModel.IdlerDivisionClientState?.CurrentDivision.IsValid ?? true) || _leagueJoinRequestSent)
                return;

            _log.Info("Sending idle league join request.");

            _idlerLeagueClient.TryJoinLeagues();
            _leagueJoinRequestSent = true;
        }

        void OnGuildDiscoveryResponse(GuildDiscoveryResponse response)
        {
            base.DefaultHandleGuildDiscoveryResult(response);
            OnGuildsDiscovered(response.GuildInfos);
        }

        void OnGuildSearchResponse(GuildSearchResponse response)
        {
            base.DefaultHandleGuildSearchResult(response);
            if (!response.IsError)
                OnGuildsDiscovered(response.GuildInfos);
        }

        void OnGuildsDiscovered(List<GuildDiscoveryInfoBase> discovered)
        {
            // open random views
            if (discovered.Count == 0)
                return;

            Random random = Random.Shared;
            if (random.NextDouble() > 0.10)
                return;

            int ndx = random.Next(discovered.Count);
            EntityId guildId = discovered[ndx].GuildId;

            if (guildId == GuildContext?.CommittedModel?.GuildId)
                return;
            foreach (ForeignGuildContext view in GuildClient.GuildViews)
            {
                if (view.Model.GuildId == guildId)
                    return;
            }
            if (GuildClient.HasPendingGuildView(guildId))
                return;
            GuildClient.BeginViewGuild(guildId, onResponse: null);
        }

        [MessageHandler]
        void HandleInitializeBot(BotCoordinator.InitializeBot _)
        {
            _log.Debug("Initializing bot");
        }

        #region IPlayerModelClientListener

        void IPlayerModelClientListener.OnProducerUnlocked(ProducerModel producer) { }
        void IPlayerModelClientListener.OnProducerCollected(ProducerModel producer) { }

        #endregion
    }
}
