// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Core.Guild;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Server.GuildDiscovery;
using System;

namespace Game.Server.GuildDiscovery
{
    [EntityConfig]
    public class GuildSearchConfig : GuildSearchConfigBase
    {
        public override Type EntityActorType => typeof(GuildSearchActor);
    }

    /// <summary>
    /// Game-specific component responsible for the guild search.
    /// </summary>
    public class GuildSearchActor : GuildSearchActorBase
    {
        protected override GuildSearchFilterBuilder GetSearchFilter(GuildSearchParamsBase searchParamsBase, GuildDiscoveryPlayerContextBase searchContextBase)
        {
            // Add SQL level filtering. Filter with level.
            GuildDiscoveryPlayerContext ctx = (GuildDiscoveryPlayerContext)searchContextBase;
            GuildSearchFilterBuilder filter = new GuildSearchFilterBuilder();
            filter.AddInterpolated($"RequiredPlayerLevel <= {ctx.PlayerLevel}");
            return filter;
        }

        protected override bool FilterSearchResult(GuildDiscoveryInfoBase publicDiscoveryInfoBase, GuildDiscoveryServerOnlyInfoBase serverOnlyDiscoveryInfoBase, GuildSearchParamsBase searchParamsBase, GuildDiscoveryPlayerContextBase searchContextBase)
        {
            // Check the name.
            if (!publicDiscoveryInfoBase.DisplayName.Contains(searchParamsBase.SearchString, StringComparison.OrdinalIgnoreCase))
                return false;

            // \todo: add custom filters here

            return true;
        }
    }
}
