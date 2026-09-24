using Metaplay.Core.Tests;
using NUnit.Framework;
using System.Diagnostics.CodeAnalysis;

[assembly: Parallelizable(ParallelScope.Fixtures)]

// \note In global namespace to make sure it covers all the server-side tests, too
[SetUpFixture]
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces", Scope = "type", Target = "OutputWriter")]
public class TestSetUp
{
    [OneTimeSetUp]
    public void SetUp()
    {
        // Initialize for tests & set working directory to project root.
        TestHelper.SetupForTests();
    }
}
