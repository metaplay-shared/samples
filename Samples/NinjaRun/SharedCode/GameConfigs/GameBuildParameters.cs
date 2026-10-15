using Metaplay.Core.Config;
using Metaplay.Core.Forms;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// The game's config build parameters: the SDK's <see cref="GameConfigBuildParameters.DefaultSource"/>,
    /// which every sheet-backed entry is read from, and one more source for the entries that are not a sheet.
    /// <para>
    /// An entry names the member it is read from in its attribute
    /// (<c>[GameConfigEntry("CourseParts", configBuildSource: nameof(CoursePartsSource))]</c>), and the SDK
    /// fetches the entry's item through whatever source that member holds. <c>tools/GameConfigGen</c> and the
    /// tests set <see cref="GameConfigBuildParameters.DefaultSource"/> to the CSV files and
    /// <see cref="CoursePartsSource"/> to the raw files of the same <c>GameConfigSource/</c> directory, so
    /// <see cref="GameConfigBuild"/> is handed the bytes of <c>CourseParts.json</c> and only parses them.
    /// </para>
    /// <para>
    /// Being the one concrete parameters type outside the SDK, the SDK's integration registry picks this as the
    /// default over <see cref="DefaultGameConfigBuildParameters"/> wherever a build is configured.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(1)]
    public class GameBuildParameters : GameConfigBuildParameters
    {
        /// <summary>
        /// Where the JSON-authored entries (<see cref="GameConfigBuild.JsonSourcedEntries"/>) are read from:
        /// a file-system source in binary format, which hands the file over as bytes under its own name.
        /// </summary>
        [MetaMember(1), MetaValidateRequired]
        public GameConfigBuildSource CoursePartsSource;

        /// <summary>
        /// The parameters every build in this repo runs with: the sheets as CSV and the JSON files as raw
        /// bytes, both out of the one directory the fetcher is pointed at.
        /// </summary>
        public static GameBuildParameters FromSourceFiles()
            => new GameBuildParameters
            {
                DefaultSource     = new FileSystemBuildSource(FileSystemBuildSource.Format.Csv),
                CoursePartsSource = new FileSystemBuildSource(FileSystemBuildSource.Format.Binary),
            };
    }
}
