using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;

namespace Game.Server
{
    /// <summary>
    /// Enables the <c>test/</c> routes that the E2E suite uses to force match timers and to create and seed
    /// weekly events (<c>docs/architecture.md</c>, "HTTP endpoints").
    /// <para>
    /// The routes are on the public, unauthenticated PublicWebApi host, so they are off in every environment
    /// and only the E2E harness turns them on (<c>tools/run-e2e.py</c>). The environment type and
    /// <c>Environment:EnableDevelopmentFeatures</c> are not used as the switch, because both are on in deployed
    /// development environments that anyone can reach.
    /// </para>
    /// </summary>
    [RuntimeOptions("TestRoutes", isStatic: true, "The unauthenticated test/ routes that the E2E suite uses on the public web host.")]
    public class TestRoutesOptions : RuntimeOptionsBase
    {
        [MetaDescription("Serves the test/ routes, which force match timers and create, seed and conclude weekly events. They need no authentication, so only the E2E harness turns them on.")]
        public bool Enabled { get; private set; } = false;
    }
}
