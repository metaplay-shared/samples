using Metaplay.Core.Config;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The game's config build. Every entry is read from the CSV sheet of the same name, except the ones in
    /// <see cref="JsonSourcedEntries"/>, which are read from a JSON file. The SDK's integration registry finds
    /// this class as the single concrete <c>GameConfigBuild</c> outside the SDK, so nothing registers it.
    /// </summary>
    public class GameConfigBuild : GameConfigBuildTemplate<SharedGameConfig>
    {
        /// <summary>
        /// The <see cref="SharedGameConfig"/> entries read from a JSON file in <c>GameConfigSource/</c>
        /// (<see cref="JsonSourceFileName"/>) rather than from a sheet, because their items hold nested lists a
        /// sheet's rows cannot. Each is fetched through the build source its <c>[GameConfigEntry]</c> names in
        /// <see cref="GameBuildParameters"/>. Such an entry has no sheet.
        /// </summary>
        public static readonly IReadOnlyCollection<string> JsonSourcedEntries = new[] { nameof(SharedGameConfig.CourseParts) };

        /// <summary>The file in <c>GameConfigSource/</c> a <see cref="JsonSourcedEntries"/> entry is read from.</summary>
        public static string JsonSourceFileName(string entryName) => entryName + ".json";

        protected override ConfigEntryBuilder? GetEntryBuilder(Type configType, string entryName)
        {
            if (configType == typeof(SharedGameConfig) && entryName == nameof(SharedGameConfig.CourseParts))
            {
                // The item is fetched from the entry's own build source, whose binary format reads the file
                // under exactly this name. A parse failure throws, which fails the build with the message
                // naming the part and the field.
                return CustomEntryBuildSingleSource<byte[]>(
                    JsonSourceFileName(entryName),
                    (builder, bytes) =>
                    {
                        List<VariantConfigItem<CoursePartId, CoursePartInfo>> items = new List<VariantConfigItem<CoursePartId, CoursePartInfo>>();
                        foreach (CoursePartInfo part in CoursePartJson.ParseAll(bytes))
                            items.Add(new VariantConfigItem<CoursePartId, CoursePartInfo>(part, null, null, sourceLocation: null));

                        builder.AssignLibraryBuildResult(entryName, items, null);
                    });
            }

            return base.GetEntryBuilder(configType, entryName);
        }
    }
}
