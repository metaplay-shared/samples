// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.TypeCodes;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    [ModelAction(ActionCodes.PlayerForceDesync)]
    [DevelopmentOnlyAction]
    public class PlayerForceDesync : PlayerAction
    {
        public PlayerForceDesync() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
#if !NETCOREAPP
            // Only execute on client
            if (commit)
                player.Random.NextInt();
#endif

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodes.PlayerGainGemsDebug)]
    [DevelopmentOnlyAction]
    public class PlayerGainGemsDebug : PlayerAction
    {
        public int Amount { get; set; }

        public PlayerGainGemsDebug() { }
        public PlayerGainGemsDebug(int amount) { Amount = amount; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                player.Wallet.NumGems += Amount;
                player.EventStream.Event(new PlayerEventDebugGainGems(Amount));
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodes.PlayerGainGoldDebug)]
    [DevelopmentOnlyAction]
    public class PlayerGainGoldDebug : PlayerAction
    {
        public int Amount { get; set; }

        public PlayerGainGoldDebug() { }
        public PlayerGainGoldDebug(int amount) { Amount = amount; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                player.Wallet.NumGold += Amount;
                player.EventStream.Event(new PlayerEventDebugGainGold(Amount));
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Add events to the player's event log, by duplicating the
    /// <see cref="NumEventsToDuplicate"/> oldest available events.
    /// This is intended to help with populating a player's event
    /// log with lots of somewhat realistic events.
    /// </summary>
    [ModelAction(ActionCodes.PlayerDuplicateEventLogEvents)]
    [DevelopmentOnlyAction]
    public class PlayerDuplicateEventLogEventsDebug : PlayerAction
    {
        public int NumEventsToDuplicate { get; private set; }
        public int NumDuplicates { get; private set; }

        PlayerDuplicateEventLogEventsDebug() { }
        public PlayerDuplicateEventLogEventsDebug(int numEventsToDuplicate, int numDuplicates)
        {
            NumEventsToDuplicate = numEventsToDuplicate;
            NumDuplicates = numDuplicates;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                player.ServerListener.DuplicateEventLogEventsDebug(NumEventsToDuplicate, NumDuplicates);
            }

            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodes.AdminActionSetBloatTestSize)]
    [DevelopmentOnlyAction]
    [PlayerDashboardAction("Bloat model", "Bloats the model with x bytes.", AdminActionPlacement.Dangerous)]
    public class AdminActionSetBloatTest : PlayerSynchronizedServerAction
    {
        [MetaDeserializationConstructor]
        public AdminActionSetBloatTest(int bloatTestSize)
        {
            BloatTestSize = bloatTestSize;
        }

        public int BloatTestSize { get; private set; }
        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                new PlayerSetBloatTestSize(BloatTestSize).Execute(player, true);
            }
            
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Set the size of <see cref="PlayerModel.BloatTest"/>.
    /// </summary>
    [ModelAction(ActionCodes.PlayerSetBloatTestSize)]
    [DevelopmentOnlyAction]
    public class PlayerSetBloatTestSize : PlayerAction
    {
        public int BloatTestSize { get; private set; }

        PlayerSetBloatTestSize() { }
        public PlayerSetBloatTestSize(int bloatTestSize)
        {
            BloatTestSize = bloatTestSize;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                RandomPCG rnd = RandomPCG.CreateFromSeed(1);

                player.BloatTest = new byte[BloatTestSize];
                for (int i = 0; i < BloatTestSize; i++)
                {
                    // Pick a random value for every 10-sized chunk;
                    // all values in a chunk are the same. Intended to
                    // get a a somewhat realistic compression ratio
                    // (more realistic than randomizing each value
                    // individually).
                    player.BloatTest[i] = i % 10 == 0
                                          ? (byte)rnd.NextInt(256)
                                          : player.BloatTest[i-1];
                }
            }

            return MetaActionResult.Success;
        }
    }
}
