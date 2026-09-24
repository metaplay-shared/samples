using NUnit.Framework;

// Test parallelism for this assembly. Most tests pay seconds for a Playwright context, a cold WASM boot and a
// session handshake, so running cases in parallel spreads that cost over several cores. Playwright's NUnit
// integration keys its browser resources by NUnit's worker id, which makes this safe.
//
// The scope is All rather than Fixtures so that the largest fixture, ShellPageTests, does not run serially while
// other workers sit idle. Live-server fixtures also run in parallel and hold a LiveServerLocks lock only around the
// step that collides on a shared server resource. LiveServerWeeklyEventSeedingTests and LiveServerWeeklyEventTests
// are [NonParallelizable] because the [Order] between them only holds in the non-parallel phase.
[assembly: Parallelizable(ParallelScope.All)]

// Required for ParallelScope.All. By default NUnit runs every case of a fixture on one instance, and Playwright's
// PageTest keeps a test's Page, Context, Browser and worker in instance fields, so parallel cases on one instance
// would overwrite each other's page. With an instance per case, each test has its own fields. Playwright's worker
// pool is static, so browsers are still reused across tests.
//
// A consequence is that any [OneTimeSetUp] must be static, because no single fixture instance exists to run it on.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]

// A fixed worker count instead of NUnit's default of one worker per processor. Too many simultaneous cold WASM boots
// starve each other of CPU, and a starved boot fails with "An unhandled error has occurred" instead of running
// slowly. tools/run-e2e.py may also run two suites on one host at once (E2E_MAX_RUNS). The harness passes its own
// worker count (E2E_TEST_WORKERS, or a default derived from the core count), so this value applies to direct
// `dotnet test` runs.
[assembly: LevelOfParallelism(6)]
