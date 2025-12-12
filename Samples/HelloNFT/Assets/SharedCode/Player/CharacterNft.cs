// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Web3;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;

namespace Game.Logic
{
    [MetaSerializable]
    public class CharacterTypeId : StringId<CharacterTypeId> { }

    [MetaSerializable]
    public class CharacterTypeInfo : IGameConfigData<CharacterTypeId>
    {
        [MetaMember(1)] public CharacterTypeId CharacterId;
        [MetaMember(2)] public string Name;
        [MetaMember(3)] public int UpgradeCostFactor = 10;
        [MetaMember(4)] public string ImageUrl;

        public CharacterTypeInfo() { }
        public CharacterTypeInfo(string id, string name, int upgradeCostFactor, string imageUrl)
        {
            CharacterId = CharacterTypeId.FromString(id);
            Name = name;
            UpgradeCostFactor = upgradeCostFactor;
            ImageUrl = imageUrl;
        }

        public CharacterTypeId ConfigKey => CharacterId;
    }

    [MetaNft("Test")]
    [MetaSerializableDerived(1)]
    [SupportedSchemaVersions(1, 1)]
    public class CharacterNft : MetaNft
    {
        [NftMetadataCustomProperty]
        [MetaMember(1)] public MetaRef<CharacterTypeInfo> Type;

        [NftMetadataCustomProperty]
        [MetaMember(2)] public int Level = 1;

        [NftMetadataCustomProperty]
        [MetaMember(3)] public int Strength;

        public int GetUpgradeCost() => Type.Ref.UpgradeCostFactor + Level;

        [NftMetadataCoreProperty]
        public string Name => Type.Ref.Name;

        [NftMetadataCoreProperty]
        public string Description => Invariant($"Level {Level} {Type.Ref.Name} with strength {Strength}");

        [NftMetadataCoreProperty]
        public string ImageUrl => Type.Ref.ImageUrl;
    }

    /// <summary>
    /// Upgrade the given NFTs.
    /// The cost of the upgrade is paid in "clicks" (<see cref="PlayerModel.NumClicks"/>),
    /// and the level of each NFT is incremented.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class PlayerUpgradeCharacterNfts : PlayerNftTransaction
    {
        [MetaMember(1)] public NftId[] CharacterNftIds;

        public override IEnumerable<NftKey> TargetNfts => CharacterNftIds.Select(id => NftTypeRegistry.Instance.GetNftKey(typeof(CharacterNft), id));

        PlayerUpgradeCharacterNfts() { }
        public PlayerUpgradeCharacterNfts(NftId[] characterNftIds)
        {
            CharacterNftIds = characterNftIds;
        }

        [MetaSerializableDerived(1)]
        public class Context : ContextBase
        {
            [MetaMember(1)] public int TotalCost;

            Context() {}
            public Context(int totalCost)
            {
                TotalCost = totalCost;
            }
        }

        public override MetaActionResult Execute(IPlayerModelBase playerBase, MetaDictionary<NftKey, MetaNft> nfts, bool commit, ref ContextBase context)
        {
            PlayerModel player = (PlayerModel)playerBase;

            // Calculate cost and validate

            int totalCost = nfts.Values.Sum(nft => ((CharacterNft)nft).GetUpgradeCost());

            if (player.NumClicks < totalCost)
            {
                // \note When Execute returns non-Success, CancelPlayer and FinalizePlayer
                //       will not be called, and thus we don't need to set `context` here.
                return ActionResult.NotEnoughResources;
            }

            if (commit)
            {
                // Take payment from player, and increment NFT levels

                player.NumClicks -= totalCost;

                foreach (CharacterNft nft in nfts.Values.Cast<CharacterNft>())
                    nft.Level++;
            }

            // Store in the context how much resources the player paid, so that CancelPlayer can refund it.
            context = new Context(totalCost);

            return MetaActionResult.Success;
        }

        public override void CancelPlayer(IPlayerModelBase playerBase, ContextBase context)
        {
            PlayerModel player = (PlayerModel)playerBase;

            // Give back the amount of resources paid by the player.
            int totalCost = ((Context)context).TotalCost;
            player.NumClicks += totalCost;
        }

        public override void FinalizePlayer(IPlayerModelBase player, ContextBase context)
        {
        }
    }
}
