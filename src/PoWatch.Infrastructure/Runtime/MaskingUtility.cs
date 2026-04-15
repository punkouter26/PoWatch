namespace PoWatch.Infrastructure.Runtime;

public static class MaskingUtility
{
    /// <summary>
    /// Masks the middle characters of a sensitive string.
    /// Returns the first 3 characters, "...", and the last 3 characters.
    /// Returns "***" for null, empty, or strings shorter than 7 characters.
    /// </summary>
    public static string MaskMiddle(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length < 7)
        {
            return "***";
        }

        return $"{source[..3]}...{source[^3..]}";
    }
}
