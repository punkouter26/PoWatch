using System.Text.Json;
using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Middleware;

/// <summary>
/// Collapses retries of the same ingest observation into one response, keyed by the
/// <see cref="IngestObservationRequestDto.IdempotencyKey"/> field. The cache lives in
/// <see cref="IIdempotencyCache"/> with a ~10-minute TTL — long enough to span a WiFi blip
/// and the retry storm that follows; short enough that an operator re-running the same
/// ingest a day later gets a fresh response.
///
/// The middleware buffers the response body so it can be replayed verbatim on a retry. It is
/// intentionally narrow: only POST /api/observer/ingest. Anything else passes through with
/// no caching and no overhead beyond a single dictionary lookup on the hot path.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate next;
    private readonly IIdempotencyCache cache;
    private readonly ILogger<IdempotencyMiddleware> logger;

    public IdempotencyMiddleware(RequestDelegate next, IIdempotencyCache cache, ILogger<IdempotencyMiddleware> logger)
    {
        this.next = next;
        this.cache = cache;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only the ingest endpoint participates. Other POSTs go straight through.
        if (!IsIngestPath(context))
        {
            await next(context);
            return;
        }

        // Buffer the request so we can read the body twice (once for the cache lookup, once for
        // the actual handler). The buffered stream is rewound before passing on.
        context.Request.EnableBuffering();
        var key = await ReadIdempotencyKeyAsync(context);
        context.Request.Body.Position = 0;

        if (key is null)
        {
            // No key — let the request through. The ObservationService treats null keys as
            // fresh events and mints a new ObservationEventId.
            await next(context);
            return;
        }

        var cached = await cache.GetAsync(key.Value, context.RequestAborted);
        if (cached is not null)
        {
            logger.LogInformation(
                "Idempotency cache hit. IdempotencyKey={IdempotencyKey}",
                key.Value);
            await WriteCachedResponseAsync(context, cached);
            return;
        }

        // Capture the response so we can store it on the way out. The body is read into a
        // MemoryStream and replaced with a fresh stream the handler can write to; the buffered
        // copy is replayed verbatim on a subsequent retry with the same key.
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, leaveOpen: true);
        var bodyText = await reader.ReadToEndAsync();

        // Only cache success responses. A 4xx/5xx means the request can be safely retried
        // without the cache interfering; storing it would let a transient 500 become a
        // permanent one for the next 10 minutes.
        if (context.Response.StatusCode is >= 200 and < 300)
        {
            await cache.SetAsync(key.Value, bodyText, context.RequestAborted);
        }

        await context.Response.WriteAsync(bodyText);
    }

    private static bool IsIngestPath(HttpContext context)
    {
        // HttpRequest.Path is unescaped; case-insensitive compare so /API/Observer/Ingest still
        // matches. Method must be POST — anything else (GET/HEAD/OPTIONS) does not carry a body
        // and cannot be idempotent in this sense.
        return HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.Equals("/api/observer/ingest", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Guid?> ReadIdempotencyKeyAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)) return null;
        if (!context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? true) return null;

        try
        {
            // The body is buffered by EnableBuffering() above. Read it as JSON and pull out
            // just the IdempotencyKey. We do not deserialize the whole DTO because the ingest
            // payload has additional validators on the server side that may reject the request;
            // we only need the key here.
            using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (!doc.RootElement.TryGetProperty("idempotencyKey", out var element))
            {
                return null;
            }
            if (element.ValueKind != JsonValueKind.String) return null;
            var raw = element.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return Guid.TryParse(raw, out var g) ? g : (Guid?)null;
        }
        catch (JsonException)
        {
            // Malformed body — let the actual handler respond with the right validation error.
            return null;
        }
    }

    private static async Task WriteCachedResponseAsync(HttpContext context, string cachedBody)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        // X-Idempotency-Replay lets clients (and the E2E suite) distinguish a replayed response
        // from a fresh one. The header is purely informational and never authoritative.
        context.Response.Headers["X-Idempotency-Replay"] = "true";
        await context.Response.WriteAsync(cachedBody);
    }
}
