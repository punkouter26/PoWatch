using System.Text.RegularExpressions;
using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;

namespace PoWatch.Infrastructure.Runtime;

/// <summary>
/// Sanitizes model-generated observation payloads to prevent prompt leakage and degenerate text from being persisted.
/// ClinicalPayload and Activity are validated; SubjectHint is restricted to safe characters.
/// </summary>
public sealed partial class TelemetryContentSanitizer : ITelemetryContentSanitizer
{
    private static readonly HashSet<string> NonObservationalTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes",
        "no",
        "1",
        "<end_of_utterance>",
        "end_of_utterance"
    };

    /// <summary>
    /// Validates and normalises an ingest request. Returns false with a reason when the payload is unsafe or degenerate.
    /// </summary>
    public bool TrySanitize(IngestObservationRequestDto input, out IngestObservationRequestDto sanitized, out string reason)
    {
        ArgumentNullException.ThrowIfNull(input);

        var normalizedPayload = NormalizeText(input.ClinicalPayload, 512);
        var normalizedActivity = NormalizeText(input.Activity, 128);

        if (string.IsNullOrWhiteSpace(normalizedPayload))
        {
            sanitized = input;
            reason = "ClinicalPayload is empty after normalization.";
            return false;
        }

        if (NonObservationalTokens.Contains(normalizedPayload))
        {
            sanitized = input;
            reason = "ClinicalPayload is a non-observational marker.";
            return false;
        }

        if (PromptLeakPattern().IsMatch(normalizedPayload))
        {
            sanitized = input;
            reason = "ClinicalPayload contains prompt leakage.";
            return false;
        }

        if (SubjectRunawayPattern().IsMatch(normalizedPayload) || NumericLoopPattern().IsMatch(normalizedPayload))
        {
            sanitized = input;
            reason = "ClinicalPayload contains degenerate repetition.";
            return false;
        }

        if (!normalizedPayload.Any(char.IsLetter))
        {
            sanitized = input;
            reason = "ClinicalPayload has no alphabetic content.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalizedActivity))
        {
            sanitized = input;
            reason = "Activity is empty after normalization.";
            return false;
        }

        if (LowInformationPattern().IsMatch(normalizedPayload) || LowInformationPattern().IsMatch(normalizedActivity))
        {
            sanitized = input;
            reason = "Observation contains low-information or uncertain text.";
            return false;
        }

        if (HasDegenerateText(normalizedPayload) || HasDegenerateText(normalizedActivity))
        {
            sanitized = input;
            reason = "Observation contains repetitive or low-diversity text.";
            return false;
        }

        var normalizedHint = NormalizeSubjectHint(input.SubjectHint);

        sanitized = new IngestObservationRequestDto
        {
            ObservedAtUtc = input.ObservedAtUtc,
            SubjectHint = normalizedHint,
            Activity = normalizedActivity,
            ClinicalPayload = normalizedPayload,
            IsSignificant = input.IsSignificant,
            SignificantReason = input.SignificantReason
        };

        reason = "Accepted";
        return true;
    }

    private static string NormalizeText(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var collapsedWhitespace = WhitespacePattern().Replace(value.Trim(), " ");
        var withoutWrappingQuotes = TrimWrappingQuotes(collapsedWhitespace);
        if (withoutWrappingQuotes.Length <= maxLength)
            return withoutWrappingQuotes;

        return withoutWrappingQuotes[..maxLength].Trim();
    }

    private static string? NormalizeSubjectHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return null;

        var compact = InvalidSubjectCharPattern().Replace(hint.Trim(), string.Empty);
        return compact.Length <= 32 ? compact : compact[..32];
    }

    private static string TrimWrappingQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1].Trim();
        return value;
    }

    private static bool HasDegenerateText(string value)
    {
        var tokens = WordTokenPattern()
            .Matches(value)
            .Select(match => match.Value.ToLowerInvariant())
            .ToArray();

        if (tokens.Length == 0)
            return false;

        var consecutiveRepeats = 1;
        for (var index = 1; index < tokens.Length; index++)
        {
            if (tokens[index] == tokens[index - 1])
            {
                consecutiveRepeats++;
                if (consecutiveRepeats >= 3)
                    return true;
            }
            else
            {
                consecutiveRepeats = 1;
            }
        }

        if (tokens.Length >= 8)
        {
            var uniqueRatio = tokens.Distinct(StringComparer.Ordinal).Count() / (double)tokens.Length;
            if (uniqueRatio < 0.45)
                return true;
        }

        return false;
    }

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex("you are a clinical room observer|describe what you see briefly|end your response with either|<\\s*subject\\s*:codename", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PromptLeakPattern();

    [GeneratedRegex("(<\\s*subject\\s*:\\s*){3,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SubjectRunawayPattern();

    [GeneratedRegex("(?:-\\d+\\.?\\s*){8,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NumericLoopPattern();

    [GeneratedRegex("^\\s*(i am not sure|i'm not sure|the answer is|unknown answer|not sure)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LowInformationPattern();

    [GeneratedRegex("[A-Za-z]{2,}", RegexOptions.Compiled)]
    private static partial Regex WordTokenPattern();

    [GeneratedRegex("[^A-Za-z0-9_-]", RegexOptions.Compiled)]
    private static partial Regex InvalidSubjectCharPattern();
}
