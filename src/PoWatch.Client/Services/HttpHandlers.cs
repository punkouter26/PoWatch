using System.Net;

namespace PoWatch.Client.Services;

/// <summary>
/// Stamps every outbound BFF request with a stable per-session <c>X-Session-ID</c> and a fresh
/// per-request <c>X-Correlation-ID</c>. Previously the client sent neither, so the server minted an
/// unrelated correlation id per request and a single monitoring session fragmented into N disconnected
/// traces. The server middleware reuses both headers, restoring end-to-end client→server correlation.
/// </summary>
public sealed class CorrelationHandler : DelegatingHandler
{
    public const string CorrelationHeader = "X-Correlation-ID";
    public const string SessionHeader = "X-Session-ID";

    private readonly string _sessionId;

    public CorrelationHandler(string sessionId) => _sessionId = sessionId;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(SessionHeader))
            request.Headers.Add(SessionHeader, _sessionId);

        if (!request.Headers.Contains(CorrelationHeader))
            request.Headers.Add(CorrelationHeader, Guid.NewGuid().ToString("N"));

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Retries reads (GET and HEAD) that failed on the way: a dropped connection, a 408, a 429 or a 5xx.
/// Two more tries, 300 ms and then 900 ms later. Writes are never retried here; ingest has its own
/// durable outbox, and repeating any other write is the caller's call.
/// </summary>
public sealed class RetryHandler : DelegatingHandler
{
    private static readonly TimeSpan[] Delays = [TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(900)];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            return await base.SendAsync(request, cancellationToken);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (attempt == Delays.Length || !IsTransient(response.StatusCode)) return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < Delays.Length)
            {
                // Dropped on the way; try again below.
            }

            await Task.Delay(Delays[attempt], cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;
}
