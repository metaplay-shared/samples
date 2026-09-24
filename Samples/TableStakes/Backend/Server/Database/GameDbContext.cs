using Metaplay.Cloud.Persistence;

namespace Game.Server.Database
{
    /// <summary>
    /// Game-specific EFCore database context. Used to declare the database tables.
    /// </summary>
    public class GameDbContext : MetaDbContext
    {
    }
}
