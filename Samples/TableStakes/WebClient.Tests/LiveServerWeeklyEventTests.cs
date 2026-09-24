using Microsoft.Playwright;
using WebClient.Meta;
using Microsoft.Playwright.NUnit;
using System.Globalization;
using System.Text.Json;

namespace WebClient.Tests;

/// <summary>
/// End-to-end test of the weekly event against a <b>live game server</b>: an event created through the test route
/// reaches the player, a real game scores into it, crossing the target makes the reward claimable, and the claim
/// pays once. It is one test because it checks how three paths fit together: delivery through the SDK's
/// synchronized actions, progress from an unsynchronized action, and the claim as an ordinary client action.
/// <c>WeeklyEventTests</c> and <c>WeeklyEventViewTests</c> cover each path on its own.
/// <c>[NonParallelizable]</c> and <c>[Order]</c> run it after <see cref="LiveServerWeeklyEventSeedingTests"/>,
/// which explains why. Running in the non-parallel phase also keeps other players out of its matchmaking queue.
/// </summary>
[NonParallelizable]
[Order(2)]
[TestFixture]
public class LiveServerWeeklyEventTests : PlaywrightPageTest
{
    /// <summary>
    /// Creates a weekly event through the <c>test/weeklyevent</c> route and returns its id and theme. The one-point
    /// target lets a single trick cross it, and every other setting comes from the shipped template. The event lasts
    /// a week, because a shorter one would be inside the ending-soon window and put the event on Home's next-action
    /// card for every other live fixture's player. Creating it concludes the overlapped seeded events, and the seeder
    /// does not recreate them. The event is not removed afterwards, because the E2E harness gives each run its own
    /// server and database.
    /// </summary>
    private static async Task<(string EventId, string Theme)> CreateWeekAsync()
    {
        using HttpClient http = new HttpClient();
        using HttpResponseMessage response = await http.PostAsync(
            $"{ServerPublicUrl}/test/weeklyevent?targetPoints=1&durationSeconds={7 * 24 * 60 * 60}&reviewSeconds={24 * 60 * 60}",
            content: null);

        response.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (body.RootElement.GetProperty("eventId").GetString()!,
                body.RootElement.GetProperty("theme").GetString()!);
    }

    /// <summary>
    /// Returns the rank of a <c>data-card</c> value, for example 14 for "AS" and 10 for "10H", or 0 when it cannot be
    /// parsed.
    /// </summary>
    private static int RankOf(string? card)
    {
        if (string.IsNullOrEmpty(card) || card.Length < 2)
            return 0;

        string rank = card[..^1];
        return rank switch
        {
            "A" => 14,
            "K" => 13,
            "Q" => 12,
            "J" => 11,
            _   => int.TryParse(rank, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0,
        };
    }

    /// <summary>
    /// Plays the highest-ranked legal card. This raises the chance of taking a trick, but some deals take no trick
    /// with any play, so the test below retries a game that took none.
    /// </summary>
    private async Task PlayHighestLegalCardAsync()
    {
        ILocator legal = Page.GetByTestId("legal-card");
        await Expect(legal.First).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });

        int count     = await legal.CountAsync();
        int bestIndex = 0;
        int bestRank  = -1;

        for (int index = 0; index < count; index++)
        {
            int rank = RankOf(await legal.Nth(index).GetAttributeAsync("data-card"));
            if (rank > bestRank)
            {
                bestRank  = rank;
                bestIndex = index;
            }
        }

        await legal.Nth(bestIndex).ClickAsync();
    }

    private static int ReadNumber(string text) =>
        int.Parse(text.Trim(), NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    /// <summary>
    /// Opens the weekly event screen through the Events tab and returns the player's points. The weekly event has no
    /// tab of its own, and navigating by taps keeps the same session.
    /// </summary>
    private async Task<int> ReadWeeklyPointsAsync()
    {
        await Page.GetByTestId("nav-events").ClickAsync();
        await Page.GetByTestId("feature-weeklyevent-action").ClickAsync();

        ILocator points = Page.GetByTestId("weekly-points");
        await Expect(points).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // The text reads like "0 / 1 points". Parse the number before the slash.
        string text = await points.InnerTextAsync();
        return ReadNumber(text.Split('/')[0]);
    }

    [Test]
    public async Task AnOperatorsWeekReachesThePlayerScoresAndPaysItsRewardOnce()
    {
        (string _, string theme) = await CreateWeekAsync();

        // ---- The week reaches the player ----
        await Page.GotoAsync(ClientUrl(MetaRoutes.WeeklyEvent));
        await WaitForLiveSessionAsync();

        // Seeing the created event's theme proves the event reached this player's model. The fixture data uses a
        // different theme.
        await Expect(Page.GetByTestId("page-title")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("weekly-theme")).ToHaveTextAsync(theme, new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("weekly-points")).ToContainTextAsync("0 / 1 points");
        await Expect(Page.GetByTestId("weekly-play")).ToBeVisibleAsync();

        int coinsBefore = await ReadBalanceAsync("coins");
        int gemsBefore  = await ReadBalanceAsync("gems");

        // ---- Play real games until one scores ----
        //
        // A game scores points per trick taken, so a game with no trick taken scores nothing. That is a legal
        // outcome, so such a game is retried. A game that took a trick and scored nothing fails the test at once,
        // because the completion fact did not reach the weekly event.
        //
        // MostGamesToTry is high enough that a run where every game takes no trick is very unlikely.
        const int MostGamesToTry = 10;
        int       scored         = 0;

        for (int game = 1; game <= MostGamesToTry && scored == 0; game++)
        {
            await OpenProfileFromHomeAsync();
            // Tricks taken tells a game that scored nothing because it took no trick apart from a scoring defect.
            (int playedBefore, _, int tricksBefore) = await ReadRecordAsync();

            await PlayFromTheNavigationBarAsync("the weekly event's game");
            await PlayWholeHandAsync(PlayHighestLegalCardAsync);
            await LeaveTheResultsAsync();

            // Wait until games played increments before reading tricks and points. PlayerRecordMatchResult updates
            // the record and the weekly event in the same action (SharedCode/Player/MatchCompletion.cs), so once
            // games played has moved, the tricks and points read next include this game. Reading earlier could see
            // zero tricks and zero points for a result still in flight, and the test would retry instead of failing.
            await OpenProfileFromHomeAsync();
            await Expect(Page.GetByTestId("record-played"))
                .ToHaveTextAsync((playedBefore + 1).ToString(), new() { Timeout = TurnTimeoutMs });

            int tricksTaken = (await ReadRecordAsync()).Tricks - tricksBefore;

            scored = await ReadWeeklyPointsAsync();

            if (scored == 0)
            {
                Assert.That(tricksTaken, Is.Zero,
                    $"game {game} took {tricksTaken} trick(s) and the week scored nothing — the completion fact did not reach the weekly event");
                continue;
            }

            // The score is at least one point per trick. A win bonus may add more, which this test does not know.
            Assert.That(scored, Is.GreaterThanOrEqualTo(tricksTaken),
                $"the week scored {scored} for a game that took {tricksTaken} trick(s)");
        }

        Assert.That(scored, Is.GreaterThan(0),
            $"{MostGamesToTry} games taking the highest legal card every turn took no trick between them");

        // ---- The game scored, and it scored on the server ----
        await Expect(Page.GetByTestId("weekly-claim")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsBefore), "crossing the target paid a reward; only the claim may pay");
        Assert.That(await ReadBalanceAsync("gems"), Is.EqualTo(gemsBefore));

        // ---- Claim it ----
        await Page.GetByTestId("weekly-claim").ClickAsync();
        await Expect(Page.GetByTestId("reward-reveal")).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
        await Page.GetByTestId("reward-continue").ClickAsync();

        await Expect(Page.GetByTestId("weekly-claimed")).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });

        // The expected amounts are the reward in GameConfigSource/WeeklyEventTemplates.csv, copied here because
        // this project cannot read the config archive. Update them when the template changes.
        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsBefore + 2000), "the coin balance did not move by the week's reward");
        Assert.That(await ReadBalanceAsync("gems"), Is.EqualTo(gemsBefore + 100), "the gem balance did not move by the week's reward");

        // ---- The reward is paid once. A reload rebuilds the client state from the server, so the page shows
        //      the committed state rather than the client's prediction.
        await ReloadOnStackAsync(Page);
        await WaitForLiveSessionAsync();

        await Expect(Page.GetByTestId("weekly-claimed")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("weekly-claim")).ToHaveCountAsync(0);
        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsBefore + 2000), "the reward was paid twice, or not at all");
    }
}
