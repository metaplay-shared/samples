// Tests run in parallel, including the tests of one fixture: a cold client boot takes seconds, and tests mostly wait
// for it. Each test has its own browser context, so its own stored credentials and its own player. The E2E harness
// sets the number of workers.
[assembly: Parallelizable(ParallelScope.All)]

// Playwright's PageTest keeps a test's page and browser context in instance fields, so tests that run at the same
// time need an instance each. One-time setup is then static, as no one instance runs the fixture.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]

// The number of workers for a direct `dotnet test` run. The E2E harness passes its own count.
[assembly: LevelOfParallelism(6)]
