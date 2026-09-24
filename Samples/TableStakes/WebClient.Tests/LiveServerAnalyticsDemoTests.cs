using Microsoft.Playwright;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace WebClient.Tests;

/// <summary>
/// Walks the analytics demo script (<c>docs/analytics.md</c>) in a browser against a live server, then reads the
/// player's event log through the Admin API that the LiveOps Dashboard uses. Asserts that the log holds exactly the
/// expected rows, that every game row has a description, and that the Dashboard's keyword filters return only the
/// rows they name. <c>AnalyticsDemoRowCountTests</c> checks the same count without a server. This fixture checks that the
/// rows are written through a real session, purchase validation and event log.
/// It never taps PLAY, so it needs no <see cref="LiveServerLocks"/> lock. The Admin API finds the player by the
/// per-run unique name the test gives it.
/// </summary>
[TestFixture]
public class LiveServerAnalyticsDemoTests : PlaywrightPageTest
{
    /// <summary>
    /// Number of custom persisted events the demo path writes, not counting the wheel's prize row. The prize
    /// row is written only when the spin lands a paying sector, because a blank sector pays nothing.
    /// </summary>
    private const int NumDemoPathCustomEvents = 23;

    /// <summary>
    /// How long the demo purchase may take to be validated. Longer than in other fixtures, because this test
    /// reaches the Shop after several other steps instead of booting into it.
    /// </summary>
    private const int PurchaseTimeoutMs = 60000;

    /// <summary>A name no other player in this database carries, so the Admin API can find this player by it.</summary>
    private static string UniqueName() => "Demo" + DateTime.UtcNow.ToString("HHmmssff");

    [Test]
    public async Task TheDemoScriptWritesAReadableLog()
    {
        string name = UniqueName();

        // ---- 1. Start the client on Home ----
        //
        // Boot once and use in-app navigation afterwards. A page load would restart the session and log an
        // extra screen view, which changes the event counts asserted below.
        await Page.GotoAsync(ClientUrl());
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("primary-nav")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // ---- 2. Home and one feature hub ----
        await Page.GetByTestId("nav-events").ClickAsync();
        await Expect(Page.GetByTestId("feature-dailyreward")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Page.GetByTestId("nav-home").ClickAsync();

        // ---- 3. The promoted next action, which for a fresh player is the daily reward ----
        ILocator nextUp = Page.GetByTestId("next-up");
        await Expect(nextUp).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Page.GetByTestId("next-up-action").ClickAsync();

        ILocator claim = Page.GetByTestId("daily-claim");
        await Expect(claim).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await claim.ClickAsync();
        await Expect(Page.GetByTestId("reward-continue")).ToHaveTextAsync("Claim", new() { Timeout = BootTimeoutMs });
        await Page.GetByTestId("reward-continue").ClickAsync();
        await Expect(Page.GetByTestId("daily-claimed")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // ---- 4. Rename the player ----
        //
        // Profile has no tab. It is reached through the identity row's button on Home.
        await Page.GetByTestId("nav-home").ClickAsync();
        await Page.GetByTestId("home-profile-action").ClickAsync();
        await Expect(Page.GetByTestId("player-name-text")).Not.ToBeEmptyAsync(new() { Timeout = BootTimeoutMs });
        await Page.GetByTestId("player-name").ClickAsync();
        await Page.GetByTestId("name-input").FillAsync(name);
        await Page.GetByTestId("name-save").ClickAsync();
        await Expect(Page.GetByTestId("name-message")).ToContainTextAsync("Name changed.", new() { Timeout = BootTimeoutMs });

        // ---- 5. Spin the wheel, reached through its feature card ----
        //
        await Page.GetByTestId("nav-events").ClickAsync();
        ILocator wheelCard = Page.GetByTestId("feature-spinwheel");
        await Expect(wheelCard).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Page.GetByTestId("feature-spinwheel-action").ClickAsync();

        ILocator spin = Page.GetByTestId("spin");
        await Expect(spin).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await spin.ClickAsync();
        await Expect(Page.GetByTestId("reward-continue")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Page.GetByTestId("reward-continue").ClickAsync();

        // ---- 6. The demo purchase ----
        await Page.GetByTestId("nav-shop").ClickAsync();
        ILocator offer = Page.GetByTestId("offer-starter-pack-1");
        await Expect(offer).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await offer.GetByTestId("offer-buy").ClickAsync();
        await Expect(Page.GetByTestId("confirm")).ToBeVisibleAsync();
        await Page.GetByTestId("confirm-accept").ClickAsync();
        await Expect(Page.GetByTestId("reward-title")).ToHaveTextAsync("Demo purchase complete", new() { Timeout = PurchaseTimeoutMs });
        await Page.GetByTestId("reward-continue").ClickAsync();
        await Expect(offer.GetByTestId("offer-purchased")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // ---- 7. Read the log the way the Dashboard does ----
        string? playerId = await FindLivePlayerIdAsync(name);
        Assert.That(playerId, Is.Not.Null, $"the Admin API found no player named {name}");

        List<LogRow> rows   = await ReadEventLogAsync(playerId!);
        List<LogRow> custom = rows.Where(row => row.IsCustom).ToList();

        await TestContext.Out.WriteLineAsync($"{playerId} wrote {rows.Count} rows, {custom.Count} of them this game's:");
        foreach (LogRow row in rows)
            await TestContext.Out.WriteLineAsync($"  {(row.IsCustom ? "game" : "sdk ")} {row.ShortTypeName,-42} {row.Description}");

        // Asserted exactly rather than as a ceiling, because a drop in the count also changes the taxonomy that
        // docs/analytics.md describes. The server draws the wheel's sector, so whether the prize row exists is
        // read from the log.
        bool prizePaid = custom.Any(row => row.ShortTypeName == "PlayerEventEconomyTransaction" &&
                                           row.Description.Contains("WheelPrize", StringComparison.Ordinal));
        Assert.That(custom.Count, Is.EqualTo(NumDemoPathCustomEvents + (prizePaid ? 1 : 0)),
            "the demo path's custom-event count moved; AnalyticsDemoRowCountTests quotes it");

        // Every game row has a description, so a reader does not have to decode the payload.
        foreach (LogRow row in custom)
            Assert.That(row.Description, Is.Not.Null.And.Not.Empty, $"{row.ShortTypeName} reached the log describing itself as nothing");

        // Privacy check on the written rows: no row carries the player's chosen name, which is the only free
        // text the demo path enters.
        foreach (LogRow row in custom)
            Assert.That(row.Payload.Contains(name, StringComparison.OrdinalIgnoreCase), Is.False,
                $"{row.ShortTypeName} carries the player's chosen name");

        // The client submitted its allow-listed events and nothing else.
        Assert.That(custom.Select(row => row.ShortTypeName).Where(type => type.Contains("Screen") || type.Contains("Promoted")).Distinct(),
            Is.EquivalentTo(new[] { "PlayerEventScreenViewed", "PlayerEventPromotedEntrySelected" }));

        // ---- 8. The keyword filters isolate what they name ----
        //
        // Applies the Dashboard's keyword filters to each row's keywords. The Sink check matters most: each
        // currency row moves in one direction, so a Sink filter that also returned grants would be useless.
        Assert.That(WithKeyword(custom, "Identity").Select(row => row.ShortTypeName),
            Is.EquivalentTo(new[] { "PlayerEventIdentityInitialized", "PlayerEventNameChangeAccepted" }));

        // Counted per type rather than as a total. The two types are debounced separately, so an extra row of
        // one type could hide a missing row of the other in a total.
        Assert.That(custom.Count(row => row.ShortTypeName == "PlayerEventScreenViewed"), Is.EqualTo(9),
            "Home, Events, Home, DailyReward, Home, Profile, Events, SpinWheel, Shop");
        Assert.That(custom.Count(row => row.ShortTypeName == "PlayerEventPromotedEntrySelected"), Is.EqualTo(2),
            "the next-action card and the wheel's feature card");
        Assert.That(WithKeyword(custom, "Navigation").Count, Is.EqualTo(11), "and both are filed under Navigation");

        Assert.That(WithKeyword(custom, "Economy").Count, Is.EqualTo(8 + (prizePaid ? 1 : 0)), "one row per currency moved");

        // Exactly one currency row (the spin's token spend) is a sink, and the other currency rows are sources.
        // PlayerEventWheelSpinResolved has both keywords, because the spin both spent and paid. Rows are
        // matched by type rather than by description text, so retuning prices does not break these checks.
        List<LogRow> sinks = WithKeyword(custom, "Sink");
        Assert.That(sinks.Count(row => row.ShortTypeName == "PlayerEventEconomyTransaction"), Is.EqualTo(1),
            "exactly one currency row is a spend; both keywords on every row is a filter that answers nothing");
        Assert.That(sinks.Count(row => row.ShortTypeName == "PlayerEventWheelSpinResolved"), Is.EqualTo(1));
        Assert.That(sinks, Has.Count.EqualTo(2), "and nothing else on this path is a sink");

        Assert.That(sinks.Single(row => row.ShortTypeName == "PlayerEventEconomyTransaction").Description,
            Does.StartWith("Spent").And.Contains("SpinTokens"), "the spend is the spin's token");

        Assert.That(WithKeyword(custom, "Source").Count(row => row.ShortTypeName == "PlayerEventEconomyTransaction"),
            Is.EqualTo(7 + (prizePaid ? 1 : 0)), "the remaining currency rows are grants");

        Assert.That(WithKeyword(custom, "SpinWheel").Select(row => row.ShortTypeName),
            Is.EquivalentTo(new[] { "PlayerEventWheelSpinResolved" }));

        // IAP rows are the SDK's purchase events. The game adds the IAP keyword to them and declares no events of
        // its own under it.
        Assert.That(WithKeyword(rows, "IAP").Any(row => row.ShortTypeName == "PlayerEventInAppPurchased"), Is.True);
        Assert.That(WithKeyword(rows, "IAP").All(row => !row.IsCustom), Is.True,
            "a custom event answered to IAP, which means this game shipped a lookalike of an SDK purchase event");
    }

    private static List<LogRow> WithKeyword(IEnumerable<LogRow> rows, string keyword) =>
        rows.Where(row => row.Keywords.Contains(keyword)).ToList();

    /// <summary>One row of the player's event log, as returned by the Admin API.</summary>
    private sealed record LogRow(string FullType, string Description, IReadOnlyList<string> Keywords, string Payload)
    {
        /// <summary>The event's type name, without the namespace.</summary>
        public string ShortTypeName { get; } = FullType.Split('.').Last();

        /// <summary>
        /// Whether the game declared the event, rather than the SDK. Decided by the payload's type name, not by
        /// searching its JSON, because an SDK purchase row contains the game's reward type names.
        /// </summary>
        public bool IsCustom { get; } = FullType.StartsWith("Game.Logic.", StringComparison.Ordinal);
    }

    /// <summary>The player's event log as <see cref="LogRow"/>s, oldest first.</summary>
    private static async Task<List<LogRow>> ReadEventLogAsync(string playerId)
    {
        List<LogRow> rows = new List<LogRow>();
        foreach (JsonElement payload in await ReadEventLogPayloadsAsync(playerId))
        {
            string   type        = payload.GetProperty("$type").GetString()!;
            string   description = payload.TryGetProperty("eventDescription", out JsonElement text) ? text.GetString() ?? "" : "";
            string[] keywords    = payload.TryGetProperty("keywords", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array
                ? listed.EnumerateArray().Select(keyword => keyword.GetString() ?? "").ToArray()
                : Array.Empty<string>();

            rows.Add(new LogRow(type.Split(',')[0], description, keywords, payload.GetRawText()));
        }

        return rows;
    }
}
