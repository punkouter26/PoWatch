using Microsoft.JSInterop;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Services.Outbox;

/// <summary>
/// Offline-tolerant ingest queue. Wraps the IndexedDB-backed JS store and adds drain logic:
/// when the network is back (or when the caller explicitly asks), walk the FIFO queue and
/// post each entry to the BFF. The BFF collapses retries by IdempotencyKey, so a double-send
/// from a race between the JS layer and the network handler produces one row, not two.
/// </summary>
public sealed class InferenceOutbox : IAsyncDisposable
{
    private readonly PoWatchApiClient apiClient;
    private readonly IJSRuntime js;
    private bool draining;

    public InferenceOutbox(PoWatchApiClient apiClient, IJSRuntime js)
    {
        this.apiClient = apiClient;
        this.js = js;
    }

    /// <summary>Append an observation to the outbox. Returns the queue key so the caller can
    /// show a status badge if it cares.</summary>
    public async Task<long> EnqueueAsync(IngestObservationRequestDto request, CancellationToken cancellationToken = default)
    {
        var idempotencyKey = request.IdempotencyKey?.ToString("N")
            ?? Guid.NewGuid().ToString("N");
        var payload = request.IdempotencyKey is null
            ? new IngestObservationRequestDto
            {
                ObservedAtUtc = request.ObservedAtUtc,
                SubjectHint = request.SubjectHint,
                Activity = request.Activity,
                ClinicalPayload = request.ClinicalPayload,
                IsSignificant = request.IsSignificant,
                SignificantReason = request.SignificantReason,
                IdempotencyKey = Guid.ParseExact(idempotencyKey, "N")
            }
            : request;

        return await js.InvokeAsync<long>("powatchOutbox.enqueue",
            payload,
            idempotencyKey);
    }

    /// <summary>Walk the queue and post each entry. Idempotent at the row level (server keeps the
    /// same ObservationEventId for the same IdempotencyKey). Safe to call concurrently —
    /// callers should still guard at the page level so the loop runs once at a time.</summary>
    public async Task<int> DrainAsync(CancellationToken cancellationToken = default)
    {
        if (draining) return 0;
        draining = true;
        try
        {
            var entries = await js.InvokeAsync<OutboxEntry[]>("powatchOutbox.listAll");
            var sent = 0;
            foreach (var entry in entries)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    var result = await apiClient.IngestObservationAsync(entry.Payload, cancellationToken);
                    if (result is not null)
                    {
                        await js.InvokeVoidAsync("powatchOutbox.dequeue", entry.EnqueuedAtUtc);
                        sent++;
                    }
                    else
                    {
                        await js.InvokeVoidAsync("powatchOutbox.markFailure", entry.EnqueuedAtUtc, "ingest returned null");
                    }
                }
                catch (Exception ex)
                {
                    await js.InvokeVoidAsync("powatchOutbox.markFailure", entry.EnqueuedAtUtc, ex.Message);
                    // Stop draining on the first hard failure; the next online event will
                    // retry from the same point.
                    break;
                }
            }
            return sent;
        }
        finally
        {
            draining = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ValueTask.CompletedTask;
    }
}
