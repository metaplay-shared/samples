// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Game.Logic
{
    [MetaSerializable]
    public class PlayerWalletModel
    {
        [MetaMember(1)] public int NumGold { get; set; }
        [MetaMember(2)] public int NumGems { get; set; }

        public PlayerWalletModel()
        {
        }

        public void SetInitialResources(SharedGameConfig gameConfig)
        {
            NumGold = gameConfig.GlobalConfig.InitialGold;
            NumGems = gameConfig.GlobalConfig.InitialGems;
        }
    }
}
