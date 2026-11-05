using Microsoft.Playwright;
using System.Net.Http;

namespace WebClient.Tests;

/// <summary>
/// Real developer authorization, match completion, Heist transfer and the subsequent metagame gift. Home's
/// development-only shortcut banks all but one of the matches the gift needs, so one real result is what
/// completes it.
/// </summary>
[TestFixture, NonParallelizable]
public class ProgressionTests : MatchTestBase
{
    [Test]
    public async Task ADeveloperWinReachesTheHeistAndCompletesACardGift()
    {
        await Page.GotoAsync(ClientUrl("/", "dev=gift"));
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Page.GetByTestId("dev-gift-one-match-away").ClickAsync();
        await Expect(Page.GetByTestId("card-gift-progress")).ToContainTextAsync("2 / 3 matches");
        string playerId = (await Page.Locator(".session-menu p").TextContentAsync())!.Trim();
        string adminUrl = Environment.GetEnvironmentVariable("E2E_SERVER_ADMIN_URL") is { Length: > 0 } url ? url.TrimEnd('/') : "http://127.0.0.1:5550";
        using HttpClient admin = new HttpClient();
        string endpoint = $"{adminUrl}/api/players/{playerId}/developerStatus";
        await Page.GetByTestId("queue-practice").ClickAsync();
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(Page.GetByTestId("debug-win-match")).ToHaveCountAsync(0);
        (await admin.PostAsync(endpoint + "?newStatus=true", new StringContent(""))).EnsureSuccessStatusCode();
        try
        {
            await ReloadOnStackAsync(Page);
            await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
            await Page.GetByLabel("Developer tools", new() { Exact = true }).ClickAsync();
            await Page.GetByTestId("debug-win-match").ClickAsync();
            await Expect(Page.GetByTestId("heist-screen")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
            await Page.GetByTestId("heist-pick").First.GetByTestId("heist-loot-tile").ClickAsync();
            await Expect(Page.Locator("[data-testid=heist-pick][data-selected=true]")).ToHaveCountAsync(1);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Pressed = true })).ToHaveCountAsync(1);
            await Page.GetByTestId("heist-snatch").ClickAsync();
            await Expect(Page.GetByTestId("play-again")).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
            await Page.GetByTestId("heist-screen").GetByTestId("leave-match").ClickAsync();
            await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
            ILocator gift = Page.GetByTestId("card-gift-reveal");
            await Expect(gift).ToBeVisibleAsync();
            string id = (await gift.GetAttributeAsync("data-card-id"))!;
            await ReloadOnStackAsync(Page);
            await Expect(gift).ToHaveAttributeAsync("data-card-id", id, new() { Timeout = BootTimeout });
            await Page.GetByTestId("card-gift-dismiss").ClickAsync();
            await Expect(Page.GetByTestId("card-gift-progress")).ToContainTextAsync("0 / 3 matches");
            await Page.GetByTestId("nav-collection").ClickAsync();
            await Expect(Card(id)).ToHaveAttributeAsync("data-owned", "true");
            await Expect(Card(id)).ToHaveAttributeAsync("data-rank", "1");
        }
        finally
        {
            (await admin.PostAsync(endpoint + "?newStatus=false", new StringContent(""))).EnsureSuccessStatusCode();
        }
    }
}
