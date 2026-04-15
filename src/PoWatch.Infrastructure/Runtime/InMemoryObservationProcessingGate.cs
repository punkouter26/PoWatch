using System.Threading;
using PoWatch.Application.Contracts;

namespace PoWatch.Infrastructure.Runtime;

public sealed class InMemoryObservationProcessingGate : IObservationProcessingGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool TryEnter() => _semaphore.Wait(0);

    public void Exit()
    {
        if (_semaphore.CurrentCount == 0)
        {
            _semaphore.Release();
        }
    }
}
