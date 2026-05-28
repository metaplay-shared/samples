// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Server.Guild;
using Microsoft.EntityFrameworkCore;

namespace Game.Server.Guild
{
    // Composite index on searched columns, as described in GuildSearchActorBase.GetSearchFilter().
    [Index(nameof(EntityId), nameof(RequiredPlayerLevel))]
    public class PersistedGuild : PersistedGuildBase
    {
        public int RequiredPlayerLevel { get; set; }
    }
}
