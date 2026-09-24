using Metaplay.Cloud.Persistence;

namespace Game.Server.Database
{
    /// <summary>
    /// Game-specific EFCore database context. Deliberately empty, but required: EF Core resolves the
    /// migrations assembly from the concrete context type, so without it the server finds no migrations.
    /// Persisted types register through the SDK's <c>IPersistedItem</c> scan, not as members here.
    /// </summary>
    public class GameDbContext : MetaDbContext
    {
    }
}
