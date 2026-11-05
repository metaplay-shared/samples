using Metaplay.Cloud.Persistence;

namespace Game.Server.Database
{
    /// <summary>
    /// Game-specific EF Core database context.
    /// <para>
    /// To add a new table, create a row class implementing <c>IPersistedItem</c> and annotate it with a <c>[Table]</c> attribute.
    /// An alternative, not recommended, way to add a table is by adding a <c>DbSet</c> property for a row class to this class.
    /// Using both the <c>[Table]</c> attribute and a <c>DbSet</c> property for the same row class is not allowed.
    /// </para>
    /// </summary>
    public class GameDbContext : MetaDbContext
    {
    }
}
