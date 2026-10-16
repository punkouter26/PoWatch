namespace PoWatch.Shared.Models;

public sealed class CreateShareDto
{
    public DateOnly Day { get; init; }
    public string? TimeZoneId { get; init; }

    /// <summary>How long the link works, in days: one of <see cref="ShareDto.LifetimeChoices"/>.</summary>
    public int Days { get; init; } = 7;
}

/// <summary>A shared recap link the caller made.</summary>
public sealed class ShareDto
{
    public static readonly IReadOnlyList<int> LifetimeChoices = [1, 7, 30];

    public string Token { get; init; } = string.Empty;

    /// <summary>Relative to the app's base address: s/{token}.</summary>
    public string Path { get; init; } = string.Empty;
    public DateOnly Day { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }
}
