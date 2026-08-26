// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.TypeCodes;
using Metaplay.Core;
using Metaplay.Core.Forms;
using Metaplay.Core.Guild;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// Game-specific results returned from <see cref="PlayerActionCore{TModel}.Execute"/>.
    /// </summary>
    public static class ActionResult
    {
        // Shadow success system results
        public static readonly MetaActionResult Success = MetaActionResult.Success;

        // Game-specific results
        public static readonly MetaActionResult InvalidProducerCategory = new MetaActionResult(nameof(InvalidProducerCategory));
        public static readonly MetaActionResult InvalidProducerId       = new MetaActionResult(nameof(InvalidProducerId));
        public static readonly MetaActionResult NotEnoughResources      = new MetaActionResult(nameof(NotEnoughResources));
        public static readonly MetaActionResult AlreadyUnlocked         = new MetaActionResult(nameof(AlreadyUnlocked));
        public static readonly MetaActionResult NoEventState            = new MetaActionResult(nameof(NoEventState));
        public static readonly MetaActionResult NoEventResult           = new MetaActionResult(nameof(NoEventResult));
        public static readonly MetaActionResult NoEventClaimPending     = new MetaActionResult(nameof(NoEventClaimPending));
        public static readonly MetaActionResult InvalidPartyId          = new MetaActionResult(nameof(InvalidPartyId));
    }

    /// <summary>
    /// Game-specific player action class, which attaches all game-specific actions to <see cref="PlayerModel"/>.
    /// </summary>
    public abstract class PlayerAction : PlayerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Game-specific <see cref="PlayerUnsynchronizedServerActionCore{TModel}"/>
    /// </summary>
    public abstract class PlayerUnsynchronizedServerAction : PlayerUnsynchronizedServerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Game-specific <see cref="PlayerSynchronizedServerActionCore{TModel}"/>
    /// </summary>
    public abstract class PlayerSynchronizedServerAction : PlayerSynchronizedServerActionCore<PlayerModel>
    {
    }

    public abstract class PlayerTransactionFinalizingAction : PlayerTransactionFinalizingActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Unlock a new producer for the player
    /// </summary>
    [ModelAction(ActionCodes.PlayerUnlockProducer)]
    public class PlayerUnlockProducer : PlayerAction
    {
        public ProducerTypeId    ProducerType  { get; private set; }

        PlayerUnlockProducer() { }
        public PlayerUnlockProducer(ProducerTypeId producerType) { ProducerType = producerType; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            ProducerInfo info = player.GameConfig.Producers[ProducerType];

            // Must be a Normal-category producer
            if (info.Category != ProducerCategory.Normal)
                return ActionResult.InvalidProducerCategory;

            // Must not be unlocked
            if (player.Producers.ContainsKey(ProducerType))
                return ActionResult.AlreadyUnlocked;

            // Must have enough gold to unlock
            int unlockCost = info.GetUnlockCost(player.HappyHours.GetActiveStates(player), player.GameConfig);
            if (player.Wallet.NumGold < unlockCost)
                return ActionResult.NotEnoughResources;

            if (commit)
            {
                // Update state
                ProducerModel producer = player.UnlockProducer(ProducerType);
                player.Wallet.NumGold -= unlockCost;
                player.EventStream.Event(new PlayerEventProducerUnlocked(ProducerType, unlockCost));
                player.Log.Debug("Unlocked {ProducerType} ({UnlockCost} gold)", ProducerType, unlockCost);

                // Callbacks
                player.ClientListener.OnProducerUnlocked(producer);
            }

            return ActionResult.Success;
        }
    }
    
    /// <summary>
    /// Upgrade a single producer owned by the player.
    /// </summary>
    [ModelAction(ActionCodes.PlayerUpgradeProducer)]
    public class PlayerUpgradeProducer : PlayerAction
    {
        public ProducerTypeId ProducerType { get; private set; }

        PlayerUpgradeProducer() { }
        public PlayerUpgradeProducer(ProducerTypeId producerType) { ProducerType = producerType; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            // Producer must be unlocked
            if (!player.Producers.TryGetValue(ProducerType, out ProducerModel producer))
                return ActionResult.InvalidProducerId;

            // Must have enough gold to upgrade
            int upgradeCost = producer.GetUpgradeCost(player.HappyHours.GetActiveStates(player), player.GameConfig);
            if (player.Wallet.NumGold < upgradeCost)
                return ActionResult.NotEnoughResources;

            if (commit)
            {
                // Apply change
                producer.Level += 1;
                player.Wallet.NumGold -= upgradeCost;
                player.EventStream.Event(new PlayerEventProducerUpgraded(ProducerType, toLevel: producer.Level, upgradeCost));

                // Callbacks
                player.ServerListener.OnProducerUpgraded(ProducerType, producer.Level);
            }

            return ActionResult.Success;
        }
    }

    /// <summary>
    /// Admin action to unlock a producer. This does not incur any cost to the player
    /// </summary>
    [ModelAction(ActionCodes.AdminUnlockProducer)]
    [PlayerDashboardAction("Unlock Producer", "Unlocks a producer for the player and sets it to level 1.", AdminActionPlacement.Gentle, requireInputBeforeAllowingConfirm: false, permission: "api.players.unlock_producer", layoutTargetId: "SendMail")]
    public class PlayerAdminUnlockProducer : PlayerSynchronizedServerAction, IPlayerDashboardAction<PlayerModel>
    {
        [MetaFormDisplayProps("Producer Type", DisplayHint = "The type of the producer. Only Normal Type producers are allowed.")]
        public ProducerTypeId ProducerType { get; private set; }

        PlayerAdminUnlockProducer() { }
        public PlayerAdminUnlockProducer(ProducerTypeId producerType) { ProducerType = producerType; }

        public void InitializeDefaultStateForDashboard(PlayerModel model)
        {
            ProducerType = model.GameConfig.Producers.FirstOrDefault(x => !model.Producers.ContainsKey(x.Key)).Key;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            ProducerInfo info = player.GameConfig.Producers[ProducerType];

            // Must be a Normal-category producer
            if (info.Category != ProducerCategory.Normal)
                return ActionResult.InvalidProducerCategory;

            // Must not be unlocked
            if (player.Producers.ContainsKey(ProducerType))
                return ActionResult.AlreadyUnlocked;

            if (commit)
            {
                // Update state
                ProducerModel   producer = player.UnlockProducer(ProducerType);
                PlayerEventBase newEvent = new PlayerEventProducerAdminUnlocked(ProducerType);
                player.EventStream.Event(newEvent);
                player.Log.Debug(newEvent.EventDescription);

                // Callbacks
                player.ClientListener.OnProducerUnlocked(producer);
            }

            return ActionResult.Success;
        }
    }

    /// <summary>
    /// Admin action to set a player's gold and gems to specific values
    /// </summary>
    [ModelAction(ActionCodes.AdminSetWallet)]
    public class PlayerAdminSetWallet : PlayerSynchronizedServerAction
    {
        public int? NewGold { get; private set; }
        public int? NewGems { get; private set; }

        PlayerAdminSetWallet() { }
        public PlayerAdminSetWallet(int? newGold, int? newGems)
        {
            NewGold = newGold;
            NewGems = newGems;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                // Update state
                player.SetWallet(NewGold, NewGems);
                PlayerEventBase newEvent  = new PlayerEventAdminSetWallet(NewGold, NewGems);
                player.EventStream.Event(newEvent);
                player.Log.Debug(newEvent.EventDescription);
            }

            return ActionResult.Success;
        }
    }

    /// <summary>
    /// Creates a guild, deducting <c>GlobalConfig.GuildCreationGemCost</c> gems from the player's wallet.
    /// If guild creation fails (player already in a guild, or creation params are invalid),
    /// the gems are refunded via <see cref="PlayerRefundGuildCreationGems"/>.
    /// </summary>
    [ModelAction(ActionCodes.PlayerCreateGuildWithCost)]
    public class PlayerCreateGuildWithCost : PlayerAction
    {
        public GuildCreationRequestParamsBase CreationParams { get; private set; }
        public int QueryId { get; private set; }

        PlayerCreateGuildWithCost() { }
        public PlayerCreateGuildWithCost(GuildCreationRequestParamsBase creationParams, int queryId)
        {
            CreationParams = creationParams;
            QueryId = queryId;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            // Check if player can afford the creation.
            int gemCost = player.GameConfig.GlobalConfig.GuildCreationGemCost;
            if (player.Wallet.NumGems < gemCost)
                return ActionResult.NotEnoughResources;

            // We could have other checks here too. For example, if we had level limit
            // for guild creation or a throttling cooldown, we would check them here.
            // For the hypothetical cooldown, this action would check the timer and then
            // bump it in the commit-branch. The refund action would restore the cooldown.

            if (commit)
            {
                player.Wallet.NumGems -= gemCost;
                player.ServerListenerCore.TryCreateNewGuild(
                    this,
                    QueryId,
                    CreationParams,
                    refundAction: new PlayerRefundGuildCreationGems(gemCost));
            }

            return ActionResult.Success;
        }
    }

    /// <summary>
    /// Refunds gems that were consumed by <see cref="PlayerCreateGuildWithCost"/> when guild creation fails.
    /// The amount is captured at charge time so a config change between charge and refund cannot diverge.
    /// </summary>
    [ModelAction(ActionCodes.PlayerRefundGuildCreationGems)]
    public class PlayerRefundGuildCreationGems : PlayerSynchronizedServerAction
    {
        public int NumGems { get; private set; }

        PlayerRefundGuildCreationGems() { }
        public PlayerRefundGuildCreationGems(int numGems)
        {
            NumGems = numGems;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
                player.Wallet.NumGems += NumGems;

            return ActionResult.Success;
        }
    }

    [ModelAction(ActionCodes.PlayerCreateParty)]
    public class PlayerCreateParty : PlayerAction
    {
        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
                player.ServerListener.CreateNewParty();

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodes.PlayerJoinParty)]
    public class PlayerJoinParty : PlayerAction
    {
        public EntityId PartyToJoin { get; set; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (!PartyToJoin.IsOfKind(EntityKindGame.Party))
                return ActionResult.InvalidPartyId;

            if (commit)
                player.ServerListener.JoinParty(PartyToJoin);

            return MetaActionResult.Success;
        }
    }
}
