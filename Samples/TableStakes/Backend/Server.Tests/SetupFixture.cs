using Metaplay.Core.Tests;
using NUnit.Framework;
using System.Diagnostics.CodeAnalysis;

[assembly: Parallelizable(ParallelScope.Fixtures)]

// In the global namespace, so it runs for every fixture in this assembly. Tests that use an EntityId or a model
// need it, because both use registries that exist only after Metaplay core is initialized.
[SetUpFixture]
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces", Scope = "type", Target = "OutputWriter")]
public class ServerTestSetUp
{
    [OneTimeSetUp]
    public void SetUp()
    {
        // Initialize Metaplay core for tests and set the working directory to the project root.
        TestHelper.SetupForTests();
    }
}
