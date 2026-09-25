using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;

namespace PoWatch.Application.Services;

/// <summary>Starts, stops and lists a user's observation sessions. Start and stop are both safe to retry.</summary>
public sealed class SessionService(ISessionRepository sessions, TimeProvider time)
{
    public const int MaxListed = 100;

    /// <exception cref="ArgumentException">The time zone is not a known IANA or Windows zone.</exception>
    public async Task<Session> StartAsync(string userId, string timeZoneId, Guid? sessionId, CancellationToken cancellationToken)
    {
        if (sessionId is { } id && await sessions.GetAsync(userId, id, cancellationToken) is { } existing)
            return existing;

        var session = Session.Start(sessionId ?? Guid.NewGuid(), userId, time.GetUtcNow(), timeZoneId);
        await sessions.UpsertAsync(session, cancellationToken);
        return session;
    }

    /// <summary>Stops the session; a session that already stopped keeps its first end time. Null when not found.</summary>
    public async Task<Session?> StopAsync(string userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(userId, sessionId, cancellationToken);
        if (session is null || !session.IsRunning)
            return session;

        var stopped = session.Stop(Max(time.GetUtcNow(), session.StartedUtc));
        await sessions.UpsertAsync(stopped, cancellationToken);
        return stopped;
    }

    public Task<Session?> GetAsync(string userId, Guid sessionId, CancellationToken cancellationToken) =>
        sessions.GetAsync(userId, sessionId, cancellationToken);

    public Task<IReadOnlyList<Session>> ListAsync(string userId, int take, CancellationToken cancellationToken) =>
        sessions.ListAsync(userId, Math.Clamp(take, 1, MaxListed), cancellationToken);

    public DateTimeOffset Now => time.GetUtcNow();

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
