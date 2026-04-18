namespace PoWatch.Domain.Services;

public static class ClinicalTagParser
{
    private const string StartTag = "<S>";
    private const string EndTag = "<E>";

    // Maximum character length allowed for the extracted clinical description.
    // Prevents oversized AI payloads from passing validation and hitting Azure Table Storage entity limits (~1 MB).
    private const int MaxDescriptionLength = 500;

    public static bool TryExtract(string? payload, out string extracted)
    {
        extracted = string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var trimmed = payload.Trim();
        var startIndex = trimmed.IndexOf(StartTag, StringComparison.Ordinal);
        var endIndex = trimmed.LastIndexOf(EndTag, StringComparison.Ordinal);

        // Allow AI preamble/postamble — require only that both tags are present and in order.
        // Previous strict check (startIndex != 0) caused high outlier rates when models
        // prefixed output with e.g. "Sure! <S>...<E>" or "Activity: <S>...<E>".
        if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
        {
            return false;
        }

        var inner = trimmed[(startIndex + StartTag.Length)..endIndex].Trim();
        if (inner.Length == 0)
        {
            return false;
        }

        if (inner.Length > MaxDescriptionLength)
        {
            return false;
        }

        if (inner.Contains(StartTag, StringComparison.Ordinal) || inner.Contains(EndTag, StringComparison.Ordinal))
        {
            return false;
        }

        extracted = inner;
        return true;
    }
}
