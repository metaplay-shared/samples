// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Server.AdminApi;

namespace Game.Server.AdminApi
{
    [AdminApiPermissionGroup("Game-specific permissions")]
    public static class GamePermissions
    {
        [MetaDescription("Example dashboard action: Unlock producer.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiPlayersUnlockProducer = "api.players.unlock_producer";

        [MetaDescription("Example dashboard action: Set player's wallet.")]
        [Permission(DefaultRole.GameAdmin)]
        public const string ApiPlayersSetWallet = "api.players.set_wallet";
    }
}
