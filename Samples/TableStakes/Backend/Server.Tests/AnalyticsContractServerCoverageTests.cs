using Game.Server.Match;
using Metaplay.Core.Analytics;
using NUnit.Framework;
using System;
using System.Linq;
using System.Reflection;

namespace Game.Server.Tests
{
    /// <summary>
    /// Checks that <c>AnalyticsContractTests</c>, compiled into this project, covers the server assembly.
    /// <para>
    /// <c>AnalyticsContractTests</c> finds events by scanning the loaded assemblies. In the shared-code test
    /// project it sees only shared code. The server assembly could also declare events, for example a
    /// table-level event on the match actor (<c>docs/analytics.md</c>). So the fixture is also compiled into
    /// this project, and these tests check that the server assembly is loaded.
    /// </para>
    /// </summary>
    [TestFixture]
    public class AnalyticsContractServerCoverageTests
    {
        /// <summary>The server assembly. Referencing a type in it guarantees that it is loaded.</summary>
        static readonly Assembly ServerAssembly = typeof(MatchActor).Assembly;

        [Test]
        public void TheContractSeesTheServerAssembly()
        {
            Assert.That(ServerAssembly.GetReferencedAssemblies().Any(reference => reference.Name != null && reference.Name.StartsWith("Metaplay.", StringComparison.Ordinal)),
                Is.True,
                "the contract's scan finds an assembly by its reference to the SDK, and the server no longer has one");
        }

        /// <summary>
        /// The server assembly declares no analytics events. An event added there would be checked by
        /// <c>AnalyticsContractTests</c> like any other, and this test fails to point that out.
        /// </summary>
        [Test]
        public void TheServerDeclaresNoAnalyticsEventOfItsOwn()
        {
            string[] declared = ServerAssembly.GetTypes()
                .Where(type => type.GetCustomAttribute<AnalyticsEventAttribute>(inherit: false) != null)
                .Select(type => type.Name)
                .ToArray();

            Assert.That(declared, Is.Empty,
                "the server declares an analytics event; it is covered by AnalyticsContractTests, which runs in this assembly");
        }
    }
}
