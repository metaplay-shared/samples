using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace WebClient.Tests;

/// <summary>
/// Locks for the two server resources that concurrent live E2E cases collide on. <b>Queue</b> is the single
/// matchmaking queue: two taps in one fill wait land at the same table, so a case finds a human where it expected a
/// bot. <b>League</b> is the single league manager and group: a concurrent join changes the standings a case reads.
/// Wrap only the colliding step in <c>await using (await LiveServerLocks.QueueAsync("..."))</c>, not the fixture.
/// Take League before Queue. <see cref="LeagueAsync"/> fails at once if the case already holds Queue, keyed on
/// NUnit's test id: a <c>[ThreadStatic]</c> flag misses awaits that resume on another thread, and an
/// <see cref="AsyncLocal{T}"/> set after an await does not flow back to the caller. NUnit workers are threads in one
/// process (see <c>Parallelism.cs</c>), so a static <see cref="SemaphoreSlim"/> is enough.
/// </summary>
internal static class LiveServerLocks
{
    private static readonly SemaphoreSlim Queue = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim League = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Total time cases spent waiting for one lock and holding it during the run.
    /// <para>
    /// The sum of holds is a lower bound on the run's duration that more workers cannot reduce. A case's wait
    /// for a lock is included in its own reported duration, so without these totals, contention looks like slow
    /// tests. <see cref="WriteLockTotals"/> prints both totals at the end of the run.
    /// </para>
    /// </summary>
    private sealed class LockCost
    {
        private long _waitTicks;
        private long _holdTicks;
        private int  _acquisitions;

        public void AddWait(TimeSpan waited)
        {
            Interlocked.Add(ref _waitTicks, waited.Ticks);
            Interlocked.Increment(ref _acquisitions);
        }

        public void AddHold(TimeSpan held) => Interlocked.Add(ref _holdTicks, held.Ticks);

        public string Describe(string lockName) =>
            $"[lock] {lockName}: {Volatile.Read(ref _acquisitions)} acquisitions, "
            + $"held {TimeSpan.FromTicks(Volatile.Read(ref _holdTicks)).TotalSeconds:F1} s in total, "
            + $"waited {TimeSpan.FromTicks(Volatile.Read(ref _waitTicks)).TotalSeconds:F1} s in total";
    }

    private static readonly LockCost QueueCost = new LockCost();
    private static readonly LockCost LeagueCost = new LockCost();

    /// <summary>
    /// Write each lock's wait and hold totals. Called once from the assembly's one-time teardown.
    /// <para>
    /// Always writes to the test output, and also to the file named by <c>TABLESTAKES_E2E_GATE_REPORT</c> when
    /// that variable is set. The file exists because <c>dotnet test</c> does not show a passing run's console
    /// output at its usual verbosity. <c>tools/run-e2e.py</c> prints the file after the test process exits.
    /// </para>
    /// </summary>
    public static void WriteLockTotals()
    {
        string[] lines = { QueueCost.Describe("Queue"), LeagueCost.Describe("League") };

        foreach (string line in lines)
            TestContext.Progress.WriteLine(line);

        string? path = Environment.GetEnvironmentVariable("TABLESTAKES_E2E_GATE_REPORT");
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            File.WriteAllLines(path, lines);
        }
        catch (Exception)
        {
            // A report that cannot be written must not fail the run. Catch every exception, not only
            // IOException: a path that names a directory throws UnauthorizedAccessException, which would
            // otherwise escape the one-time teardown and fail the run.
        }
    }

    /// <summary>
    /// How long a case waits for a lock before the test fails instead of hanging. It is much longer than any
    /// guarded step takes, so reaching it means a lock was never released or two cases deadlocked by taking
    /// the locks in opposite orders.
    /// </summary>
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// NUnit test ids of the cases currently holding Queue. <see cref="LeagueAsync"/> reads it to detect the
    /// wrong acquisition order.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> CasesHoldingQueue = new ConcurrentDictionary<string, byte>();

    /// <summary>NUnit's id for the current test. It stays the same when an await resumes on another thread.</summary>
    private static string CaseId() => TestContext.CurrentContext.Test.ID;

    /// <summary>
    /// Hold the server's matchmaking queue for <paramref name="step"/>. Wrap only the tap that enters the queue
    /// through the assertion that the seat has landed.
    /// </summary>
    public static Task<IAsyncDisposable> QueueAsync(string step) => AcquireAsync(Queue, QueueCost, "Queue", step, entersQueue: true);

    /// <summary>
    /// Hold the league manager and its group for <paramref name="step"/>. Wrap only the join, or the read of the
    /// standings that a concurrent join would disturb. Fails the test if the current case already holds Queue.
    /// </summary>
    public static Task<IAsyncDisposable> LeagueAsync(string step)
    {
        if (CasesHoldingQueue.ContainsKey(CaseId()))
        {
            Assert.Fail($"LiveServerLocks.LeagueAsync(\"{step}\") was called while this case already held Queue — "
                + "acquire League first and Queue second, nested inside it, never the other way round.");
        }

        return AcquireAsync(League, LeagueCost, "League", step, entersQueue: false);
    }

    private static async Task<IAsyncDisposable> AcquireAsync(SemaphoreSlim semaphore, LockCost cost, string lockName, string step, bool entersQueue)
    {
        long waitStarted = Stopwatch.GetTimestamp();
        bool acquired = await semaphore.WaitAsync(AcquireTimeout);

        if (!acquired)
        {
            // Assert.Fail throws before AddWait below, so a timed-out wait is not added to the lock's wait total.
            Assert.Fail($"timed out after {AcquireTimeout.TotalSeconds:F0} s waiting for the {lockName} lock "
                + $"(\"{step}\") — either a held lock leaked past its guarded step, or two cases deadlocked on "
                + "League and Queue taken in opposite orders.");
        }

        cost.AddWait(Stopwatch.GetElapsedTime(waitStarted));

        string caseId = CaseId();
        if (entersQueue)
            CasesHoldingQueue[caseId] = 0;

        return new Held(semaphore, cost, entersQueue, caseId);
    }

    /// <summary>
    /// A held lock. Disposal releases the semaphore, records the hold time and removes the case from
    /// <see cref="CasesHoldingQueue"/>, once. <c>await using</c> disposes it however the guarded block exits.
    /// </summary>
    private sealed class Held : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly LockCost _cost;
        private readonly bool _entersQueue;
        private readonly string _caseId;
        private readonly long _heldFromTimestamp;
        private int _released;

        public Held(SemaphoreSlim semaphore, LockCost cost, bool entersQueue, string caseId)
        {
            _semaphore = semaphore;
            _cost = cost;
            _entersQueue = entersQueue;
            _caseId = caseId;
            _heldFromTimestamp = Stopwatch.GetTimestamp();
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _cost.AddHold(Stopwatch.GetElapsedTime(_heldFromTimestamp));

                if (_entersQueue)
                    CasesHoldingQueue.TryRemove(_caseId, out _);

                _semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
