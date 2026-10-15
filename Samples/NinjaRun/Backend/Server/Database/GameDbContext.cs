using Metaplay.Cloud.Persistence;

namespace Game.Server.Database
{
    /// <summary>
    /// Game-specific EFCore database context. Used to declare the database tables.
    /// <para>
    /// A persisted entity's own table is picked up from its <c>[Table]</c> attribute (see
    /// <c>PersistedCourseGhostPool</c>) and declaring a <c>DbSet</c> for it here as well is an error.
    /// </para>
    /// </summary>
    public class GameDbContext : MetaDbContext
    {
        // Example table declaration for a plain (non-entity) table:
        //public DbSet<MyPersistedThing> Things { get; set; }
    }
}
