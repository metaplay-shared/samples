using Metaplay.Core.Tests;
using NUnit.Framework;
using System.Diagnostics.CodeAnalysis;

[assembly: Parallelizable(ParallelScope.Fixtures)]

// \note In the global namespace so that NUnit runs this setup before the tests in every namespace of the assembly.
[SetUpFixture]
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces", Scope = "type", Target = "OutputWriter")]
public class TestSetUp
{
    [OneTimeSetUp]
    public void SetUp()
    {
        // Initialize the SDK for tests and set the working directory to the project root.
        TestHelper.SetupForTests();
    }
}
