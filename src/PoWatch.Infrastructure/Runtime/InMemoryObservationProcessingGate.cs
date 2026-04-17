using PoWatch.Application.Contracts;

namespace PoWatch.Infrastructure.Runtime;

/// <summary>
/// Thread-safe observation processing gate using SemaphoreSlim with explicit state tracking.
/// Ensures exactly-once exit semantics and prevents semaphore leaks under any code path.
/// </summary>
public sealed class InMemoryObservationProcessingGate : IObservationProcessingGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _isProcessing = 0; // 0 = idle, 1 = processing

    /// <summary>
    /// Attempts to enter the gate. Returns true if successful (gate was idle),
    /// false if gate is already held (previous inference still running).
    /// </summary>
    /// <returns>True if the lock was acquired; false if already held.</returns>
    public bool TryEnter()
    {
        // Quick check without entering
        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
        {
            return false; // Already processing
        }

        // Now wait for the semaphore
        if (_semaphore.Wait(0))
        {
            return true; // Successfully entered
        }

        // Semaphore was already held - this shouldn't happen normally
        // but handle it gracefully
        Interlocked.Exchange(ref _isProcessing, 0);
        return false;
    }

    /// <summary>
    /// Releases the gate. Must be called exactly once after each successful TryEnter().
    /// Safe to call multiple times from finally blocks if guarded by _isProcessing check.
    /// </summary>
    public void Exit()
    {
        // Only exit if we're the one holding the lock
        if (Interlocked.Exchange(ref _isProcessing, 0) == 1)
        {
            _semaphore.Release(1);
        }
        // If _isProcessing was already 0, Exit() was called without matching TryEnter()
        // or was already called - this is a no-op
    }

    /// <summary>
    /// Returns true if the gate is currently held by a processor.
    /// For diagnostics/monitoring only.
    /// </summary>
    public bool IsProcessing => Interlocked.CompareExchange(ref _isProcessing, 0, 0) == 1;
}
