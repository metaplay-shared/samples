using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary> Identifier for a <see cref="ClanInfo"/> row. </summary>
    [MetaSerializable]
    public class ClanId : StringId<ClanId>
    {
    }

    /// <summary>
    /// One animal clan: a color, a mechanical identity, and whether it counts against a deck's clan limit.
    /// Wanderers are a clan row like the others, distinguished only by
    /// <see cref="CountsTowardClanLimit"/> being false — which is what lets them glue any two clans together.
    /// </summary>
    [MetaSerializable]
    public class ClanInfo : IGameConfigData<ClanId>
    {
        [MetaMember(1)] public ClanId ClanId                { get; private set; }
        [MetaMember(2)] public string DisplayName           { get; private set; }
        /// <summary> The clan's pastel palette color, as authored for the card frame and board accents. </summary>
        [MetaMember(3)] public string ColorHex              { get; private set; }
        /// <summary> False only for Wanderers, who are playable in any deck without spending a clan slot. </summary>
        [MetaMember(4)] public bool   CountsTowardClanLimit { get; private set; }
        /// <summary> Placeholder art until the real chibi illustrations exist. </summary>
        [MetaMember(5)] public string ArtEmoji              { get; private set; }
        /// <summary> One-line designer note on what the clan is for. Shown in the collection filters. </summary>
        [MetaMember(6)] public string Identity              { get; private set; }

        public ClanId ConfigKey => ClanId;

        public ClanInfo() { }

        public ClanInfo(ClanId clanId, string displayName, bool countsTowardClanLimit, string colorHex = null, string artEmoji = null, string identity = null)
        {
            ClanId                = clanId;
            DisplayName           = displayName;
            ColorHex              = colorHex;
            CountsTowardClanLimit = countsTowardClanLimit;
            ArtEmoji              = artEmoji;
            Identity              = identity;
        }
    }
}
