// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Logic
{
    /// <summary>
    /// Game-specific global configuration data, visible to both client and server.
    /// </summary>
    [MetaSerializable]
    public class SharedGlobalConfig : GameConfigKeyValue<SharedGlobalConfig>
    {
        //[MetaMember(1)] public int    SomeInteger { get; set; } = 123;
        //[MetaMember(2)] public string SomeString  { get; set; } = "Default value";
    }

    /// <summary>
    /// Game-specific shared configuration data between the client and the server.
    /// </summary>
    public class SharedGameConfig : SharedGameConfigBase
    {
        [GameConfigEntry("Global")]
        public SharedGlobalConfig Global { get; set; }

        [GameConfigEntry("Characters")]
        public GameConfigLibrary<CharacterTypeId, CharacterTypeInfo> Characters { get; set; }
    }

    /// <summary>
    /// Game-specific build code for Game Configs.
    /// </summary>
    public class GameConfigBuild : GameConfigBuildTemplate<SharedGameConfig>
    {
        protected override ConfigEntryBuilder? GetEntryBuilder(Type configType, string entryName)
        {
            if (configType == typeof(SharedGameConfig) && entryName == "Characters")
            {
                CharacterTypeInfo[] characters = {
                    new("DogWarrior", "Dog Warrior", 10, "https://meta-dev.metaplay.dev/nft/web3-develop/assets/dogwarrior.jpeg"),
                    new("DogMedic",   "Dog Medic",    12, "https://meta-dev.metaplay.dev/nft/web3-develop/assets/dogmedic.jpeg"),
                    new("DogShaman",  "Dog Shaman",   14, "https://meta-dev.metaplay.dev/nft/web3-develop/assets/dogshaman.jpeg"),
                    new("CatWarrior", "Cat Warrior",  10, "https://meta-dev.metaplay.dev/nft/web3-develop/assets/catwarrior.jpeg"),
                    new("CatMedic",   "Cat Medic",    12, "https://meta-dev.metaplay.dev/nft/web3-develop/assets/catmedic.jpeg"),
                    new("CatShaman",  "Cat Shaman",   14, "https://meta-dev.metaplay.dev/nft/web3-develop/assets/catshaman.jpeg")
                };

                return AssignLibraryItemsBuilder<CharacterTypeId, CharacterTypeInfo>(characters);
            }
            return base.GetEntryBuilder(configType, entryName);
        }
    }
}
