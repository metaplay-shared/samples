// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.System.Tests;
using Microsoft.Playwright.NUnit;

namespace System.Tests;

[SetUpFixture]
public class GlobalTestSetUp
{
    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        await TestUtil.Initialize();
    }
}

/// <summary>
/// Example test fixture that derives from <see cref="MetaPageTest"/> instead
/// of Playwright.NET's <see cref="ContextTest"/> to get
/// additional features like automatic screenshots on failures and optional
/// video recording.
/// </summary>
[TestFixture]
public class ExampleTests : MetaPageTest
{
    /// <summary>
    /// Example test case. Does simple browser testing of LiveOps Dashboard using
    /// Playwright.NET's <see cref="Microsoft.Playwright.IPage"/> interface.
    /// </summary>
    [Test]
    public async Task TestPlayerPageTitle()
    {
        await Page.GotoAsync($"{TestUtil.DashboardBaseUrl}/players");
        await Expect(Page).ToHaveTitleAsync("LiveOps Dashboard - Manage Players");
    }
}
