namespace OnlyFate.Desktop;

/// <summary>Keeps the native operation serialized even when its caller stops waiting.</summary>
public sealed class PreviewOperationCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private int active;
    public bool IsBusy => Volatile.Read(ref active) != 0;
    // Reset is synchronous on the UI thread. Skipping a clear is safe because the view is hidden;
    // the next real navigation uses RunAsync. Taking the same gate also closes the check/use race.
    public bool TryRunWhenIdle(Action operation)
    {
        if(!gate.Wait(0)) return false;
        try { operation(); return true; }
        finally { gate.Release(); }
    }
    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken token)
    {
        await gate.WaitAsync(token);
        Interlocked.Exchange(ref active, 1);
        try
        {
            token.ThrowIfCancellationRequested();
            // Do not WaitAsync(token) here: native printing has no cancellation API.
            var result = await operation();
            token.ThrowIfCancellationRequested();
            return result;
        }
        finally { Interlocked.Exchange(ref active, 0); gate.Release(); }
    }
}

/// <summary>One initialization at a time; faults can be retried, timeouts don't duplicate native work.</summary>
public sealed class PreviewInitialization
{
    private Task? pending;
    public async Task EnsureAsync(Func<Task> initialize, TimeSpan timeout, CancellationToken token)
    {
        if(pending is null || pending.IsFaulted || pending.IsCanceled) pending = initialize();
        await pending.WaitAsync(timeout, token);
    }
}
