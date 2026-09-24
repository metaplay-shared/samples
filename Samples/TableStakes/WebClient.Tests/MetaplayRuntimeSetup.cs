using Metaplay.Core;
using NUnit.Framework;

namespace WebClient.Tests;

/// <summary>
/// Registers the <see cref="EntityKindRegistry"/> for the whole assembly. <c>EntityId.IsValid</c> reads the
/// registry, and the match logic calls it on every turn step, so any test that runs a real
/// <see cref="Game.Logic.MatchModel"/> needs it. The registry is installed with
/// <c>MetaplayServices.SetServiceProvider</c>, which the SDK documents for use by test fixtures. No other SDK
/// service is registered, so the session-backed parts of the SDK stay uninitialized and the shell's screens
/// render the fixture data without a session.
/// </summary>
[SetUpFixture]
public class MetaplayRuntimeSetup
{
    [OneTimeSetUp]
    public void SetUp()
    {
        // The root must be an assembly with source-generated Metaplay integration info. SharedCode.Client has it,
        // and it also registers the game's own entity kinds.
        IntegrationConfig config = IntegrationConfig.FromAssembly(typeof(Game.Logic.MatchModel).Assembly);
        IntegrationDataSet dataSet = IntegrationDataSet.Gather(config);
        EntityKindRegistry registry = new EntityKindRegistry(dataSet);

        SimpleServiceInitializers initializers = new SimpleServiceInitializers();
        initializers.Add<IntegrationConfig>(_ => config);
        initializers.Add<IntegrationDataSet>(_ => dataSet);
        initializers.Add<EntityKindRegistry>(_ => registry);

        MetaplayServices.SetServiceProvider(new SimpleMetaplayServiceProvider(initializers));

        // Resolve the registry here, before any test runs. MetaplayServices is not thread-safe, and parallel
        // fixtures that each resolved it first would race to create the same service.
        MetaplayServices.Get<EntityKindRegistry>();
    }

    /// <summary>
    /// Prints the wait and hold totals of <see cref="LiveServerLocks"/>. Called here because this hook runs after every
    /// fixture in the assembly has finished.
    /// </summary>
    [OneTimeTearDown]
    public void TearDown() => LiveServerLocks.WriteLockTotals();
}
