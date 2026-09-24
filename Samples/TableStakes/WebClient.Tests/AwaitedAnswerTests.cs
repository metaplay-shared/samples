using WebClient.Services;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="AwaitedAnswer{TAnswer}"/>, the wait behind the tournament's join and claim requests. The
/// Compete page stays busy until the wait returns, so every way an answer can fail to arrive must release the wait.
/// </summary>
[TestFixture]
public class AwaitedAnswerTests
{
    private static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(5);

    private sealed record Reply(string Text);

    [Test]
    public async Task TheAnswerReleasesTheWait()
    {
        AwaitedAnswer<Reply> awaited = new AwaitedAnswer<Reply>(LongTimeout);

        Task<Reply> wait = awaited.Start(new Reply("no answer"), out int id);
        awaited.Answer(id, new Reply("joined"));

        Assert.That((await wait).Text, Is.EqualTo("joined"));
    }

    [Test]
    public async Task AnEndedSessionReleasesTheWaitWithTheFallback()
    {
        AwaitedAnswer<Reply> awaited = new AwaitedAnswer<Reply>(LongTimeout);

        Task<Reply> wait = awaited.Start(new Reply("no answer"), out int _);
        awaited.Abandon();

        Assert.That((await wait).Text, Is.EqualTo("no answer"));
    }

    [Test]
    public async Task ANewerRequestReleasesTheOlderWaitWithItsOwnFallback()
    {
        AwaitedAnswer<Reply> awaited = new AwaitedAnswer<Reply>(LongTimeout);

        Task<Reply> first  = awaited.Start(new Reply("first, no answer"), out int _);
        Task<Reply> second = awaited.Start(new Reply("second, no answer"), out int secondId);
        awaited.Answer(secondId, new Reply("answered"));

        Assert.That((await first).Text, Is.EqualTo("first, no answer"));
        Assert.That((await second).Text, Is.EqualTo("answered"));
    }

    [Test]
    public async Task ALostAnswerReleasesTheWaitAtTheTimeout()
    {
        AwaitedAnswer<Reply> awaited = new AwaitedAnswer<Reply>(TimeSpan.FromMilliseconds(50));

        Task<Reply> wait = awaited.Start(new Reply("no answer"), out int _);

        Assert.That(await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(wait), "the wait outlived its timeout");
        Assert.That((await wait).Text, Is.EqualTo("no answer"));
    }

    [Test]
    public void AnAnswerWithNothingWaitingIsIgnored()
    {
        AwaitedAnswer<Reply> awaited = new AwaitedAnswer<Reply>(LongTimeout);

        Assert.DoesNotThrow(() => awaited.Answer(1, new Reply("late")));
        Assert.DoesNotThrow(() => awaited.Abandon());
    }

    /// <summary>
    /// A request that was released without its answer can still be answered later. That late answer must not
    /// complete the next request, and the next request's own answer must still complete it.
    /// </summary>
    [Test]
    public async Task ALateAnswerToAnEarlierRequestDoesNotCompleteTheNextOne()
    {
        AwaitedAnswer<Reply> awaited = new AwaitedAnswer<Reply>(LongTimeout);

        Task<Reply> first  = awaited.Start(new Reply("first, no answer"), out int firstId);
        Task<Reply> second = awaited.Start(new Reply("second, no answer"), out int secondId);
        Assert.That((await first).Text, Is.EqualTo("first, no answer"));

        awaited.Answer(firstId, new Reply("answer to the first"));
        Assert.That(second.IsCompleted, Is.False, "the first request's late answer completed the second request");

        awaited.Answer(secondId, new Reply("answer to the second"));
        Assert.That((await second).Text, Is.EqualTo("answer to the second"));
    }

    /// <summary>A request that timed out is no longer outstanding, so its late answer is ignored.</summary>
    [Test]
    public async Task ALateAnswerToATimedOutRequestIsIgnored()
    {
        AwaitedAnswer<Reply> awaited = new AwaitedAnswer<Reply>(TimeSpan.FromMilliseconds(50));

        Task<Reply> wait = awaited.Start(new Reply("no answer"), out int id);
        Assert.That((await wait).Text, Is.EqualTo("no answer"));

        Assert.DoesNotThrow(() => awaited.Answer(id, new Reply("late")));
        Assert.That((await wait).Text, Is.EqualTo("no answer"));
    }
}
