namespace PoWatch.Domain.Services;

public static class ClinicalTagParser
{
    private const string StartTag = "<S>";
    private const string EndTag = "<E>";

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

        if (startIndex != 0 || endIndex != trimmed.Length - EndTag.Length)
        {
            return false;
        }

        var inner = trimmed[StartTag.Length..^EndTag.Length].Trim();
        if (inner.Length == 0)
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
