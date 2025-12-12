using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Logic
{
    #region snippet2
    [MetaSerializable]
    public class SolutionInfo : IGameConfigData<int>, IGameConfigPostLoad
    {
        [MetaMember(1)] public int Id { get; private set; } // Unique id of the solution
        [MetaMember(2)] public string Word { get; private set; } // Word for solution

        public int ConfigKey => Id;

        void IGameConfigPostLoad.PostLoad()
        {
            // Validate the Word to avoid bad data leaking into the game
            MetaDebug.Assert(!string.IsNullOrEmpty(Word), "SolutionInfo {0} has empty Word", Id);
            MetaDebug.Assert(Word.Length == 5, "SolutionInfo {0} Word is not 5 characters", Id);

            // Convert to uppercase as that it what the game logic expects
            Word = Word.ToUpperInvariant();
        }
    }
    #endregion snippet2
    #region snippet1
    public class SharedGameConfig : SharedGameConfigBase
    {
        [GameConfigEntry("Solutions", configBuildSource: nameof(GameConfigBuildParameters.DefaultSource))]
        public GameConfigLibrary<int, SolutionInfo> Solutions { get; protected set; }

        public string GetSolutionForRound(int roundIndex)
        {
            return Solutions[roundIndex % Solutions.Count].Word;
        }
    }
    #endregion snippet1
}
