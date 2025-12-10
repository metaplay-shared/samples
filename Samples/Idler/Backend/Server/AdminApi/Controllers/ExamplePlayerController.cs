// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Game.Server.AdminApi.AuditLog;
using Metaplay.Core.Model;
using Metaplay.Server.AdminApi;
using Metaplay.Server.AdminApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Metaplay.Core.AuditLog;
using Metaplay.Server.WebApi;
using static System.FormattableString;

namespace Game.Server.AdminApi.Controllers
{
    /// <summary>
    /// Audit log event for producer unlocked from the dashboard.
    /// </summary>
    [MetaSerializableDerived(GameAuditLogEventCodes.ProducerUnlocked)]
    public class PlayerEventProducerUnlocked : PlayerEventPayloadBase
    {
        [MetaMember(1)] public ProducerTypeId ProducerTypeId { get; private set; }

        PlayerEventProducerUnlocked() { }
        public PlayerEventProducerUnlocked(ProducerTypeId producerTypeId) => ProducerTypeId = producerTypeId;

        override public string EventTitle => "Producer unlocked";
        override public string EventDescription => $"Unlocked producer {ProducerTypeId} manually";
    }

    /// <summary>
    /// Audit log event for setting player wallet from the dashboard.
    /// </summary>
    [MetaSerializableDerived(GameAuditLogEventCodes.SetWallet)]
    public class PlayerEventSetWallet : PlayerEventPayloadBase
    {
        [MetaMember(1)] public int? NewGold { get; private set; }
        [MetaMember(2)] public int? NewGems { get; private set; }

        PlayerEventSetWallet() { }

        public PlayerEventSetWallet(int? newGold, int? newGems)
        {
            NewGold = newGold;
            NewGems = newGems;
        }

        override public string EventTitle => "Set wallet";
        override public string EventDescription
        {
            get
            {
                List<string> changes = new List<string>();
                changes.Add(Invariant($"Player wallet set. "));

                if (NewGold.HasValue)
                    changes.Add(Invariant($"Gold = {NewGold.Value}, "));
                else
                    changes.Add(Invariant($"Gold unchanged, "));

                if (NewGems.HasValue)
                    changes.Add(Invariant($"Gems = {NewGems.Value}."));
                else
                    changes.Add(Invariant($"Gems unchanged."));

                return string.Join("", changes);
            }
        }
    }

    /// <summary>
    /// Controller for you game specific routes that deal with an individual player.
    /// Keeping your code in a separate file help us avoid merge conficts in the future!
    /// </summary>
    public class ExamplePlayerController : GameAdminApiController
    {
        // Simple example action to unlock a producer
        [HttpPost("players/{playerIdStr}/unlockProducer/{producerTypeIdStr}")] // This defines the URL of this route
        [RequirePermission(GamePermissions.ApiPlayersUnlockProducer)] // This defines which users are authorized to use the endpoint
        public async Task UnlockProducer(string playerIdStr, string producerTypeIdStr) // Remember to ingest the defined route parameters
        {
            // Get player details
            PlayerDetails    playerDetails = await GetPlayerDetailsAsync(playerIdStr);
            PlayerModel      playerModel   = (PlayerModel)playerDetails.Model;
            SharedGameConfig gameConfig    = playerModel.GameConfig;

            // Error out if the producer type does not exist
            if (!gameConfig.Producers.ContainsKey(ProducerTypeId.FromString(producerTypeIdStr)))
                throw new MetaplayHttpException(404, "Unknown ProducerType.", $"The ProducerType ${producerTypeIdStr} does not exist for this player.");

            // Get producer type
            ProducerTypeId producerTypeId = ProducerTypeId.FromString(producerTypeIdStr);

            // Invoke the action via the PlayerActor
            _logger.LogInformation("Unlocking producer {ProducerTypeId} for {PlayerId}!", producerTypeId, playerDetails.PlayerId);
            await EnqueuePlayerServerActionAsync(playerDetails.PlayerId, new PlayerAdminUnlockProducer(producerTypeId));

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerDetails.PlayerId, new PlayerEventProducerUnlocked(producerTypeId)));
        }

        /// <summary>
        /// HTTP request body for setting a player's wallet
        /// </summary>
        public class PlayerSetWalletBody
        {
            [JsonProperty(Required = Required.AllowNull)] // value must be specified but null is allowed (\todo is this the desired behavior -- the dash code unconditionally sends a number)
            public int? NewGold { get; private set; }

            [JsonProperty(Required = Required.AllowNull)] // value must be specified but null is allowed (\todo is this the desired behavior -- the dash code unconditionally sends a number)
            public int? NewGems { get; private set; }
        }

        // Action to set a player's wallet to the specified values
        [HttpPost("players/{playerIdStr}/setWallet")] // This defines the URL of this route
        [RequirePermission(GamePermissions.ApiPlayersSetWallet)] // This defines which users are authorized to use the endpoint
        public async Task SetWallet(string playerIdStr, [FromBody] PlayerSetWalletBody body) // Remember to ingest the defined route parameters
        {
            // Get player details
            PlayerDetails playerDetails = await GetPlayerDetailsAsync(playerIdStr);

            // Invoke the action via the PlayerActor
            await EnqueuePlayerServerActionAsync(playerDetails.PlayerId, new PlayerAdminSetWallet(body.NewGold, body.NewGems));

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerDetails.PlayerId, new PlayerEventSetWallet(body.NewGold, body.NewGems)));
        }
    }
}
