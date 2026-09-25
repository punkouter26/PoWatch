using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;
using PoWatch.Shared.Models;
using PoWatch.Shared.Services.Recaps;

namespace PoWatch.Application.Services;

/// <summary>
/// Writes recaps for a session or a day. The template recap is always built; when an AI chat client
/// is configured it rewrites only the summary paragraph from the same facts, and any failure or
/// timeout falls back to the template paragraph.
/// </summary>
public sealed class RecapService(
    StatsQueryService stats,
    ISessionRepository sessions,
    ISensingLog sensingLog,
    ISnapshotStore snapshots,
    IEnumerable<IChatClient> chatClients,
    ILogger<RecapService> logger)
{
    private static readonly TimeSpan AiTimeout = TimeSpan.FromSeconds(20);

    public async Task<RecapDto?> ForSessionAsync(string userId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (await sessions.GetAsync(userId, sessionId, cancellationToken) is not { } session) return null;
        var window = await stats.ResolveAsync(userId, StatsRange.Session, session.TimeZoneId, sessionId, cancellationToken);
        if (window is null) return null;

        var start = TimeZoneInfo.ConvertTime(session.StartedUtc, window.Zone);
        var facts = await GatherAsync(userId, window,
            $"Session #{sessionId.ToString("N")[..4].ToUpperInvariant()}",
            $"{start:ddd d MMM, HH:mm} · {TemplateRecap.Duration(session.Duration(DateTimeOffset.UtcNow))}",
            await MomentsAsync(userId, session, cancellationToken),
            cancellationToken);
        return await WriteAsync(facts, cancellationToken);
    }

    public async Task<RecapDto?> ForDayAsync(string userId, DateOnly day, string? timeZoneId, CancellationToken cancellationToken)
    {
        var window = await stats.ResolveAsync(userId, StatsRange.Day, timeZoneId, null, cancellationToken, day);
        if (window is null) return null;

        var daySessions = (await sessions.ListAsync(userId, SessionService.MaxListed, cancellationToken))
            .Where(s => s.StartedUtc < window.ToUtc && (s.EndedUtc ?? DateTimeOffset.UtcNow) >= window.FromUtc);
        var moments = new List<MomentDto>();
        foreach (var session in daySessions)
            moments.AddRange((await MomentsAsync(userId, session, cancellationToken)).Where(m => m.AtUtc >= window.FromUtc && m.AtUtc < window.ToUtc));

        var facts = await GatherAsync(userId, window,
            day.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture),
            $"Daily recap · {window.Zone.Id}",
            [.. moments.OrderByDescending(m => m.Score)],
            cancellationToken);
        return await WriteAsync(facts, cancellationToken);
    }

    /// <summary>A session's notable moments, best first, with short-lived snapshot links.</summary>
    public async Task<IReadOnlyList<MomentDto>> MomentsAsync(string userId, Session session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var zone = session.TimeZone;
        var end = session.EndedUtc ?? DateTimeOffset.UtcNow;
        var notable = new List<SceneEvent>();
        for (var day = LocalDay.Of(session.StartedUtc, zone); day <= LocalDay.Of(end, zone); day = day.AddDays(1))
            notable.AddRange((await sensingLog.GetEventsAsync(userId, day, cancellationToken)).Where(e => e.SessionId == session.Id && e.Kind == SceneEventKind.Notable));

        var moments = new List<MomentDto>();
        foreach (var moment in notable.OrderByDescending(e => e.Score ?? 0).ThenBy(e => e.AtUtc).Take(12))
        {
            var url = moment.ImagePath is { } path ? await snapshots.CreateReadUrlAsync(userId, path, cancellationToken) : null;
            moments.Add(new MomentDto { AtUtc = moment.AtUtc, Text = moment.Text ?? string.Empty, Score = moment.Score ?? 0, ImageUrl = url?.ToString() });
        }
        return moments;
    }

    private async Task<RecapFacts> GatherAsync(string userId, StatsWindow window, string title, string subtitle, IReadOnlyList<MomentDto> moments, CancellationToken ct)
    {
        var presence = await stats.PresenceAsync(userId, window, ct);
        var objects = await stats.ObjectsAsync(userId, window, ct);
        var environment = await stats.EnvironmentAsync(userId, window, ct);
        var patterns = window.Range == StatsRange.Day ? await stats.PatternsAsync(userId, window, ct) : null;
        return new RecapFacts(title, subtitle, window.FromUtc, window.ToUtc, window.Zone, presence, objects, environment, patterns, moments);
    }

    private async Task<RecapDto> WriteAsync(RecapFacts facts, CancellationToken cancellationToken)
    {
        var recap = TemplateRecap.Build(facts);
        if (chatClients.FirstOrDefault() is not { } chat || facts.Presence.SecondsObserved <= 0) return recap;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AiTimeout);
        var prompt = RecapPrompt.User(recap);
        try
        {
            // Identical facts give an identical prompt, which the chat client's response cache answers
            // (RecapAi), so re-opening a past day or its PDF does not pay for the model again.
            var response = await chat.GetResponseAsync(
                [new ChatMessage(ChatRole.System, RecapPrompt.System), new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { Temperature = 0.4f, MaxOutputTokens = 300 },
                timeout.Token);

            var text = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return recap;
            if (!RecapPrompt.KeepsToFacts(text, prompt))
            {
                logger.LogWarning("AI recap used a number that is not in the facts; using the template recap.");
                return recap;
            }

            return RecapPrompt.Rewritten(recap, text, chat.GetService<ChatClientMetadata>()?.ProviderName ?? "ai");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeouts, transport and provider errors (the OpenAI SDK throws ClientResultException, not
            // HttpRequestException) all end the same way: the template paragraph.
            logger.LogWarning(ex, "AI recap failed; using the template recap. Provider={Provider}", chat.GetType().Name);
            return recap;
        }
    }
}
