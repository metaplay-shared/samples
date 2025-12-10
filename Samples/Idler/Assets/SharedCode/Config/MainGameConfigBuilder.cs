// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Forms;
using Metaplay.Core.Json;
using Metaplay.Core.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Game.Logic
{
    [MetaSerializableDerived(1)]
    public class IdlerGameConfigBuildParameters : GameConfigBuildParameters
    {
        [MetaSerializable]
        public enum SynthesizeDataMode
        {
            None = 0,
            DuplicateData = 1,
        }

        [MetaMember(2), MetaFormNotEditable , MetaFormLayoutOrderHint(2)]
        public SynthesizeDataMode SynthesizeData { get; set; }
        [MetaMember(3), MetaFormNotEditable, JsonIgnore]
        public List<string> LibrariesToDuplicate { get; set; } = new List<string>()
            { "ProducerData", "ProducerKinds", "HappyHours", "SpecialProducerEvents" };
        [MetaMember(4), MetaFormLayoutOrderHint(1), MetaFormNotEditable] public FileSystemBuildSource OpaqueDataSource;
        [MetaMember(5), MetaFormLayoutOrderHint(0), MetaValidateRequired] public GameConfigBuildSource LiveOpsSource;

        [MetaMember(6)] public bool BuildLanguages { get; set; } = true;
        [MetaMember(7)] public bool BuildProducerKinds { get; set; } = true;
        [MetaMember(8)] public bool BuildProducers { get; set; } = true;
        [MetaMember(9)] public bool BuildInAppProducts { get; set; } = true;
        [MetaMember(10)] public bool BuildGlobal { get; set; } = true;
        [MetaMember(11)] public bool BuildPlayerSegments { get; set; } = true;
        [MetaMember(12)] public bool BuildHappyHours { get; set; } = true;
        [MetaMember(13)] public bool BuildSpecialProducerEvents { get; set; } = true;
        [MetaMember(14)] public bool BuildPlayerExperiments { get; set; } = true;
        [MetaMember(15)] public bool BuildOffers { get; set; } = true;
        [MetaMember(16)] public bool BuildOfferGroups { get; set; } = true;

        public override bool IsIncremental => (!BuildLanguages ||
                                               !BuildProducerKinds ||
                                               !BuildProducers ||
                                               !BuildInAppProducts ||
                                               !BuildGlobal ||
                                               !BuildPlayerSegments ||
                                               !BuildHappyHours ||
                                               !BuildSpecialProducerEvents ||
                                               !BuildPlayerExperiments ||
                                               !BuildOffers ||
                                               !BuildOfferGroups) || 
                                              !HasLocalFileSource;
        public bool HasLocalFileSource => OpaqueDataSource != null;
    }

    public class IdlerGameConfigBuildIntegration : GameConfigBuildIntegration
    {
        private static GoogleSheetBuildSource[] _sheetSources =
        {
            new GoogleSheetBuildSource("Master sheet", "1wXKv4SPD7BZBwTRc7YfAU7oVjuWFDZZFbHZ8PKyCatM"),
        };

        public override IEnumerable<GameConfigBuildSource> GetAvailableGameConfigBuildSources(string sourceProperty)
        {
            if (sourceProperty is nameof(IdlerGameConfigBuildParameters.DefaultSource) or nameof(IdlerGameConfigBuildParameters.LiveOpsSource))
            {
                return _sheetSources;
            }

            return base.GetAvailableGameConfigBuildSources(sourceProperty);
        }

        public override IEnumerable<GameConfigBuildSource> GetAvailableLocalizationsBuildSources(string sourceProperty)
        {
            return _sheetSources;
        }
    }

    public class IdlerGameConfigBuild : GameConfigBuildTemplate<SharedGameConfig, ServerGameConfig, IdlerGameConfigBuildParameters>
    {
        bool ShouldBuildEntry(Type configType, string entryName)
        {
            if (!BuildParameters.IsIncremental)
                return true;

            // Map GameConfigEntry name to C# member name
            // (often they're the same but not always)
            string memberName = GameConfigRepository.Instance.GetGameConfigTypeInfo(configType).Entries[entryName].MemberName;

            if(configType == typeof(SharedGameConfig) && memberName == nameof(SharedGameConfig.Languages))
                return BuildParameters.BuildLanguages;
            if(configType == typeof(SharedGameConfig) && memberName == nameof(SharedGameConfig.ProducerKinds))
                return BuildParameters.BuildProducerKinds;
            if(configType == typeof(SharedGameConfig) && memberName == nameof(SharedGameConfig.Producers))
                return BuildParameters.BuildProducers;
            if(configType == typeof(SharedGameConfig) && memberName == nameof(SharedGameConfig.InAppProducts))
                return BuildParameters.BuildInAppProducts;
            if(configType == typeof(SharedGameConfig) && memberName == nameof(SharedGameConfig.GlobalConfig))
                return BuildParameters.BuildGlobal;
            if(configType == typeof(SharedGameConfig) && memberName == nameof(SharedGameConfig.PlayerSegments))
                return BuildParameters.BuildPlayerSegments;
            if(configType == typeof(SharedGameConfig) && memberName == nameof(SharedGameConfig.HappyHours))
                return BuildParameters.BuildHappyHours;
            if(configType == typeof(SharedGameConfig) && memberName == nameof(SharedGameConfig.SpecialProducerEvents))
                return BuildParameters.BuildSpecialProducerEvents;
            if(configType == typeof(ServerGameConfig) && memberName == nameof(ServerGameConfig.PlayerExperiments))
                return BuildParameters.BuildPlayerExperiments;
            if(configType == typeof(SharedGameConfig) && memberName == nameof(SharedGameConfig.Offers))
                return BuildParameters.BuildOffers;
            if(configType == typeof(SharedGameConfig) && memberName == nameof(SharedGameConfig.OfferGroups))
                return BuildParameters.BuildOfferGroups;

            return false;
        }

        protected override ConfigEntryBuilder? GetEntryBuilder(Type configType, string entryName)
        {
            if (!ShouldBuildEntry(configType, entryName))
                return null;
            if (configType == typeof(SharedGameConfig))
            {
                if (entryName == "OpaqueSourceTest")
                {
                    if (!BuildParameters.HasLocalFileSource)
                        return null;
                    
                    return CustomEntryBuildSingleSource<byte[]>(
                        // Fetch data from the OpaqueSourceTest defined in the build parameters
                        GetGenericFetchFunc("OpaqueSourceTest.json", BuildParameters.OpaqueDataSource),
                        (builder, data) =>
                        {
                            // Deserialize from json to the target type.
                            JsonSerializer serializer = JsonSerialization.CreateSerializer(
                                new JsonSerialization.Options(JsonSerialization.Options.DefaultOptions)
                                {
                                    TraverseGameConfigDataType = typeof(OpaqueSourceTestInfo),
                                });
                            List<OpaqueSourceTestInfo> list = JsonSerialization.Deserialize<List<OpaqueSourceTestInfo>>(data, serializer);

                            // Convert to a type that the SDK can consume, if your data type supports variants, they can also be passed here.
                            List<VariantConfigItem<OpaqueSourceTestId,OpaqueSourceTestInfo>> items = list.Select(x => new VariantConfigItem<OpaqueSourceTestId, OpaqueSourceTestInfo>(x, null, null, null)).ToList();

                            // Finally, assign the result to the game config builder
                            builder.AssignLibraryBuildResult(nameof(SharedGameConfig.OpaqueSourceTest),
                                items,
                                null);
                        });
                }
                
                if (BuildParameters.SynthesizeData == IdlerGameConfigBuildParameters.SynthesizeDataMode.DuplicateData && BuildParameters.LibrariesToDuplicate.Contains(entryName))
                {
                    return CustomEntryBuildSingleSource<SpreadsheetContent>(entryName,
                        (builder, data) =>
                        {
                            DuplicateSpreadsheetContent(data);
                            builder.BuildGameConfigEntry(entryName, data);
                        });
                }
            }
            return base.GetEntryBuilder(configType, entryName);
        }

        void DuplicateSpreadsheetContent(SpreadsheetContent content)
        {
            var header = content.Cells[0];
            var idIndex = header.FindIndex(x => x.Value.Contains("#key"));
            if (idIndex == -1)
                return;

            List<List<SpreadsheetCell>> newRows = new List<List<SpreadsheetCell>>();
            int row = 0;
            for (int duplicationFactor = 0; duplicationFactor < 3000; duplicationFactor++)
            {
                foreach (var spreadsheetCells in content.Cells.Skip(1))
                {
                    List<SpreadsheetCell> newRow = new List<SpreadsheetCell>();
                    for (var i = 0; i < spreadsheetCells.Count; i++)
                    {
                        var spreadsheetCell = spreadsheetCells[i];
                        string newValue = spreadsheetCell.Value;
                        if (i == idIndex && !string.IsNullOrWhiteSpace(newValue))
                        {
                            newValue += $"{row}";
                        }
                        newRow.Add(new SpreadsheetCell(newValue, spreadsheetCell.Row, spreadsheetCell.Column));
                    }
                    newRows.Add(newRow);
                    row++;
                }
            }
                        
            content.Cells.AddRange(newRows);
        }
    }
}
