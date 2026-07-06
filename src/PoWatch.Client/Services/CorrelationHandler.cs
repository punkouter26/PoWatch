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
