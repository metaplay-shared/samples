using System;
using System.Threading.Tasks;

namespace WebClient.Services;

/// <summary>
/// A request whose answer arrives later as a separate server message, such as a tournament join or claim.
/// <para>
/// At most one request is outstanding. Its task always completes: with the answer, or with the fallback answer
/// passed to <see cref="Start"/> when a newer request starts, when <see cref="Abandon"/> is called, or when the
/// timeout passes. A screen waiting on it therefore never stays busy indefinitely.
/// </para>
/// <para>
/// Each request gets an id that the server copies into its answer. An answer is accepted only for the outstanding
/// request's id, so a late answer to a request that already timed out cannot complete a newer one.
/// </para>
/// </summary>
public sealed class AwaitedAnswer<TAnswer> where TAnswer : class
{
    readonly TimeSpan _timeout;

    TaskCompletionSource<TAnswer>? _pending;
    TAnswer?                       _noAnswer;
    int                            _pendingId;
    int                            _lastId;

    public AwaitedAnswer(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    /// <summary>
    /// Starts waiting for an answer. An outstanding request is first completed with its own fallback answer.
    /// </summary>
    /// <param name="noAnswer">The result if no answer arrives.</param>
    /// <param name="requestId">The id to send with the request. The server copies it into its answer.</param>
    public Task<TAnswer> Start(TAnswer noAnswer, out int requestId)
    {
        Abandon();

        TaskCompletionSource<TAnswer> pending = new TaskCompletionSource<TAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending   = pending;
        _noAnswer  = noAnswer;
        _pendingId = ++_lastId;
        requestId  = _pendingId;

        return WaitAsync(pending, noAnswer);
    }

    /// <summary>
    /// Completes the outstanding request with the server's answer, if <paramref name="requestId"/> is its id. An
    /// answer to any other request is ignored.
    /// </summary>
    public void Answer(int requestId, TAnswer answer)
    {
        if (_pending == null || requestId != _pendingId)
            return;

        _pending.TrySetResult(answer);
        Clear();
    }

    /// <summary>
    /// Completes the outstanding request, if any, with its fallback answer. Called when the session ends.
    /// </summary>
    public void Abandon()
    {
        _pending?.TrySetResult(_noAnswer!);
        Clear();
    }

    void Clear()
    {
        _pending   = null;
        _noAnswer  = null;
        _pendingId = 0;
    }

    async Task<TAnswer> WaitAsync(TaskCompletionSource<TAnswer> pending, TAnswer noAnswer)
    {
        try
        {
            return await pending.Task.WaitAsync(_timeout);
        }
        catch (TimeoutException)
        {
            // The request is no longer outstanding, so a late answer to it is ignored.
            if (ReferenceEquals(_pending, pending))
                Clear();

            return noAnswer;
        }
    }
}
