// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.League;
using Game.Logic.TypeCodes;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.InGameMail;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Game.Logic
{
    /// <summary>
    /// Server-side event listener interface. This is used by the <see cref="PlayerModel"/> to communicate
    /// any events happening inside PlayerActor on the server.
    /// </summary>
    public interface IPlayerModelServerListener
    {
        void OnProducerUpgraded(ProducerTypeId producer, int newLevel);
        void DuplicateEventLogEventsDebug(int numEventsToDuplicate, int numDuplicates);
        void CreateNewParty();
        void JoinParty(EntityId partyId);
    }

    /// <summary>
    /// Empty implementation of <see cref="IPlayerModelServerListener"/>, so that there can always be a listener
    /// object and no null checks are needed.
    /// </summary>
    public class EmptyPlayerModelServerListener : IPlayerModelServerListener
    {
        public static readonly EmptyPlayerModelServerListener Instance = new EmptyPlayerModelServerListener();

        public void OnProducerUpgraded(ProducerTypeId producer, int newLevel) { }
        public void DuplicateEventLogEventsDebug(int numEventsToDuplicate, int numDuplicates) { }
        public void CreateNewParty() {}
        public void JoinParty(EntityId partyId) {}
    }

    /// <summary>
    /// Client-side player event listener interface.
    /// </summary>
    public interface IPlayerModelClientListener
    {
        void OnProducerUnlocked(ProducerModel producer);
        void OnProducerCollected(ProducerModel producer);
    }

    /// <summary>
    /// Empty implementation of <see cref="IPlayerModelClientListener"/>, so that there can always be a listener
    /// object and no null checks are needed.
    /// </summary>
    public class EmptyPlayerModelClientListener : IPlayerModelClientListener
    {
        public static readonly EmptyPlayerModelClientListener Instance = new EmptyPlayerModelClientListener();

        public void OnProducerUnlocked(ProducerModel producer) { }
        public void OnProducerCollected(ProducerModel producer) { }
    }

    /// <summary>
    /// Class for storing the state and updating the logic for a single player.
    /// </summary>
    [MetaSerializableDerived(1)]
    [SupportedSchemaVersions(6, 9)]
    public class PlayerModel :
        PlayerModelBase<
            PlayerModel,
            PlayerStatisticsCore,
            PlayerIdlerOfferGroupsModel
            >
    {
        public const int TicksPerSecond = 10;
        protected override int GetTicksPerSecond() => TicksPerSecond;

        // External services, not serialized or PrettyPrinted
        [IgnoreDataMember] public new SharedGameConfig              GameConfig => GetGameConfig<SharedGameConfig>();
        [IgnoreDataMember] public IPlayerModelServerListener        ServerListener  { get; set; } = EmptyPlayerModelServerListener.Instance;
        [IgnoreDataMember] public IPlayerModelClientListener        ClientListener  { get; set; } = EmptyPlayerModelClientListener.Instance;

        public override IModelRuntimeData<IPlayerModelBase>         GetRuntimeData() => new PlayerModelRuntimeData(this);

        // Player profile
        [MetaMember(100)] public sealed override EntityId           PlayerId    { get; set; }
        [MetaMember(101), NoChecksum] public sealed override string PlayerName  { get; set; }
        [MetaMember(102)] public sealed override int                PlayerLevel { get; set; }

        // Game-specific state
        [MetaMember(110), ExcludeFromGdprExport]  public RandomPCG                                      Random                      { get; set; } // Random number generator for shared game logic.
        [MetaMember(111)]                         public PlayerWalletModel                              Wallet                      { get; private set; } = new PlayerWalletModel();
        [MetaMember(112), MaxCollectionSize(100)] public MetaDictionary<ProducerTypeId, ProducerModel>  Producers                   { get; private set; } = new MetaDictionary<ProducerTypeId, ProducerModel>();
        [MetaMember(113)]                         public PlayerLegacyShopOffersModel                    LegacyShopOffers            { get; private set; } = new PlayerLegacyShopOffersModel();
        [MetaMember(114)]                         public PlayerHappyHoursModel                          HappyHours                  { get; private set; } = new PlayerHappyHoursModel();
        [MetaMember(115)]                         public PlayerSpecialProducerEventsModel               SpecialProducerEvents       { get; private set; } = new PlayerSpecialProducerEventsModel();
        [MetaMember(116)]                         public MetaTime                                       TimeOfLastTimeZoneChange    { get; private set; } = MetaTime.Epoch;
        [MetaMember(117), ServerOnly]             public EntityId                                       CurrentParty                { get; set; } = EntityId.None;

        MetaDuration MinTimeBetweenTimeZoneChanges => MetaDuration.FromHours(12);

        /// <summary>
        /// For testing behavior of large serialized models.
        /// </summary>
        [MetaMember(1001)] public byte[] BloatTest = new byte[] { };

        public IdlerDivisionClientState IdlerDivisionClientState => PlayerSubClientStates.GetValueOrDefault(ClientSlotGame.IdlerLeague) as IdlerDivisionClientState;
        public IdlerPvPDivisionClientState PvPDivisionClientState => PlayerSubClientStates.GetValueOrDefault(ClientSlotGame.IdlerPvPLeague) as IdlerPvPDivisionClientState;

        protected override void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            // Setup initial state for new player
            PlayerId    = playerId;
            PlayerName  = name;
            Random      = RandomPCG.CreateNew();
        }

        protected override void GameOnRestoredFromPersistedState(MetaDuration elapsedTime)
        {
            //throw new NotSupportedException("test crash during init");

            // Ensure that game state is up-to-date
            EnsureProducersUpToDate();
        }


        protected override void GameFastForwardTime(MetaDuration elapsedTime)
        {
            // Ensure that game state is up-to-date
            EnsureProducersUpToDate();
        }

        protected override void GameOnSessionStarted()
        {
        }

        protected override void GameOnInitialLogin()
        {
            Wallet.SetInitialResources(GameConfig);

            // Unlock initial producer
            UnlockProducer(GameConfig.GlobalConfig.InitialProducer.Ref.Id);
        }

        /// <summary>
        /// Ensure that the producers are up-to-date. Also resets any timers in the future to current time
        /// (can happen with debug time skipping). Doesn't trigger any listener events for the gaining resources.
        /// </summary>
        void EnsureProducersUpToDate()
        {
            // Update all producers
            MetaTime curTime = CurrentTime;
            foreach (ProducerModel producer in Producers.Values)
            {
                // Enforce startedAt time cannot be in the future (can happen with debug time skips)
                if (producer.StartedAt > curTime)
                {
                    Log.Info("Reset producer {ProducerId} startedAt to {NewStartedAt} (previously {OldStartedAt})", producer.Info.Id, curTime, producer.StartedAt);
                    producer.StartedAt = curTime;
                }
                else if (curTime >= producer.FinishedAt)
                {
                    // Compute how many production cycles have completed & award all gold in one go
                    MetaDuration elapsed = curTime - producer.StartedAt;
                    MetaDuration cycleDuration = producer.Info.Duration;
                    int numCycles = (int)(elapsed.Milliseconds / cycleDuration.Milliseconds);

                    Wallet.NumGold += numCycles * producer.ProduceValue;
                    producer.StartedAt = producer.StartedAt + numCycles * cycleDuration;

                    Log.Debug("Producer {0} produced {1} cycles for {2} gold, new startAt={3}, now={4}", producer.Info.Id, numCycles, numCycles * producer.ProduceValue, producer.StartedAt, curTime);
                }
            }
        }

        /// <summary>
        /// Handler that is called when the class is deserialized, either when deserializing from database
        /// or when model is sent over the wire to/from client.
        /// </summary>
        [MetaOnDeserialized]
        public void OnDeserialized()
        {
            //DebugLog.Debug("PlayerModel.OnDeserialized()");

            // To support renaming Producer config IDs we need to update our Producers dictionary on deserialize
            Dictionary<ProducerTypeId, ProducerTypeId> renames = new Dictionary<ProducerTypeId, ProducerTypeId>();
            foreach (var producer in Producers)
            {
                if (producer.Key != producer.Value.Info.ConfigKey)
                    renames[producer.Key] = producer.Value.Info.ConfigKey;
            }
            foreach (var rename in renames)
            {
                Producers[rename.Value] = Producers[rename.Key];
                Producers.Remove(rename.Key);
            }
        }

        public override void OnClaimedInAppProduct(InAppPurchaseEvent ev, InAppProductInfoBase productInfoBase, out ResolvedPurchaseContentBase resolvedContent)
        {
            InAppProductInfo productInfo = (InAppProductInfo)productInfoBase;

            bool hasAnyStaticContent = productInfo.NumGold > 0
                                    || productInfo.NumGems > 0;

            if (hasAnyStaticContent)
            {
                Wallet.NumGold += productInfo.NumGold;
                Wallet.NumGems += productInfo.NumGems;

                resolvedContent = new ResolvedPurchaseGameContent(
                    numGold: productInfo.NumGold,
                    numGems: productInfo.NumGems);
            }
            else
                resolvedContent = null;
        }

        public override InAppPurchaseRefundResult OnInAppProductRefunded(InAppPurchaseEvent purchaseEvent, InAppProductInfoBase productInfo, InAppPurchaseRefundReason reason)
        {
            // \todo Revoke also the MetaRewards in purchaseEvent.ResolvedDynamicContent (if any)

            PurchaseRefundGameResult result = new();

            // Revoke the "static" (config-defined) rewards (see OnClaimedInAppProduct above)
            if (purchaseEvent.ResolvedContent is ResolvedPurchaseGameContent resolvedContent)
            {
                // Revoke the resources that had been granted, but don't go negative.
                // This is custom revocation logic that could be more complex depending on the game.
                // You could e.g. add a mail to MailInbox telling the player about the refund.
                int revokedGold = Math.Min(Wallet.NumGold, resolvedContent.NumGold);
                int revokedGems = Math.Min(Wallet.NumGems, resolvedContent.NumGems);
                Wallet.NumGold -= revokedGold;
                Wallet.NumGems -= revokedGems;

                // Record the revoked amounts, for purchase history and analytics.
                result.NumGold = revokedGold;
                result.NumGems = revokedGems;
            }

            return result;
        }

        public ProducerModel UnlockProducer(ProducerTypeId type, ProducerSourceInfo source = null)
        {
            MetaDebug.Assert(!Producers.ContainsKey(type), "Producer {0} already unlocked", type);
            ProducerInfo info = GameConfig.Producers[type];
            ProducerModel producer = new ProducerModel(info, CurrentTime, source);
            Producers.Add(type, producer);
            return producer;
        }

        public void SetWallet(int? newGold, int? newGems)
        {
            if (newGold.HasValue)
                this.Wallet.NumGold = newGold.Value;
            if (newGems.HasValue)
                this.Wallet.NumGems = newGems.Value;
        }

        /// <summary>
        /// Execute a single tick on the PlayerModel. That is, progress time by 1sec/PlayerModel.TicksPerSecond.
        ///
        /// Updates all logic objects (ie, Producers) of the player.
        /// </summary>
        /// <param name="checksumCtx">Context for taking checksum snapshot for each stage of the tick</param>
        /// <remarks><c>PlayerModelBase</c> calls this _before_ increasing <c>CurrentTick</c>.</remarks>
        protected override void GameTick(IChecksumContext checksumCtx)
        {
            //checksumCtx.Checkpoint("Producers");
            MetaTime curTime = CurrentTime;
            foreach (ProducerModel producer in Producers.Values)
                TickProducer(producer, curTime);

            TickEvents();
        }

        void TickProducer(ProducerModel producer, MetaTime curTime)
        {
            // Check whether a producer's timer is completed
            // \note If timers can be faster than a tick, then this should be done in a loop
            if (curTime >= producer.FinishedAt)
            {
                //Log.Debug("Producer {0} produced {1}", producer.Info.Id, producer.ProduceValue);
                Wallet.NumGold += producer.ProduceValue;
                producer.StartedAt = producer.FinishedAt;

                // Invoke the listener on the client (in case it wants to show some gfx effect)
                ClientListener.OnProducerCollected(producer);
            }
        }

        static readonly int EventsActivationTickInterval = 1 * TicksPerSecond;

        /// <summary>
        /// Tick the logic for all the events.
        /// Finalizes events that have finished, and activates new events when possible.
        /// </summary>
        void TickEvents()
        {
            // Attempt event activation only occasionally, to reduce the perf cost in case there's lots of configured events.
            if (CurrentTick % EventsActivationTickInterval == 0)
            {
                // Finalize ended events first, then activate new ones

                HappyHours.TryFinalizeEach(GameConfig.HappyHours.Values, this);
                SpecialProducerEvents.TryFinalizeEach(GameConfig.SpecialProducerEvents.Values, this);

                HappyHours.TryStartActivationForEach(GameConfig.HappyHours.Values, this);
                SpecialProducerEvents.TryStartActivationForEach(GameConfig.SpecialProducerEvents.Values, this);
            }
        }

        protected override void GameImportAfterReset(PlayerModel source)
        {
        }

        // Example override of UpdateTimeZone to restrict time zone changes.
        // Only allows the time zone to change if it's the first login of the player or a minimum time has passed since the last time zone change. 
        public override void UpdateTimeZone(PlayerTimeZoneInfo newTimeZone, bool isFirstLogin)
        {
            // Set initial time zone if this is the first login,
            // else change time zone only if enough time has passed since last time zone change
            MetaDuration timeSinceLastTimeZoneChange = CurrentTime - TimeOfLastTimeZoneChange;
            if (isFirstLogin || timeSinceLastTimeZoneChange >= MinTimeBetweenTimeZoneChanges)
            {
                TimeZoneInfo             = newTimeZone;
                TimeOfLastTimeZoneChange = CurrentTime;
            }
        }

        #region Schema Migrations

        [MigrationFromVersion(fromVersion: 6)]
        void MigrateMetaOffers()
        {
            // Old shop offers were removed, and migrated to new MetaOffers.
            LegacyShopOffers.MigrateToMetaOffers(GameConfig, MetaOfferGroups, this);
        }

        [MigrationFromVersion(fromVersion: 7)]
        void MigrateInbox()
        {
            // Changed mail inbox item type
            foreach (MetaInGameMail mail in LegacyMailInbox)
            {
                MetaInGameMail converted;
                switch (mail)
                {
                    case LegacyPlayerMail legacy:
                        converted = SimplePlayerMail.FromLegacy(legacy, Language);
                        break;

                    default:
                        if (!mail.Id.IsValid)
                            mail.Id = MetaGuid.NewWithTime(mail.CreatedAt.ToDateTime());
                        converted = mail;
                        break;
                }

                // \todo #mail-refactor: populate (game-specific) runtime mail item state here
                MailInbox.Add(IntegrationRegistry.Get<InGameMailIntegration>().MakePlayerMailItem(converted, converted.CreatedAt));
            }
            LegacyMailInbox.Clear();
        }

        [MigrationFromVersion(fromVersion: 8)]
        void MigrateLeagues()
        {
            // Migrate league client state
#pragma warning disable CS0618 // Type or member is obsolete
            if (PlayerSubClientStates.TryGetValue(ClientSlotCore.PlayerDivisionLegacy, out PlayerSubClientStateBase idlerState))
            {
                PlayerSubClientStates.Remove(ClientSlotCore.PlayerDivisionLegacy);
                PlayerSubClientStates[ClientSlotGame.IdlerLeague] = idlerState;
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }

        #endregion
    }
}
