using System.Collections.Concurrent;
using System.Text;

namespace PoWatch.Domain.Services;

/// <summary>
/// Generates consistent URL-safe subject IDs from display names.
/// Thread-safe slug generation with collision detection.
/// </summary>
public static class SubjectIdSlugger
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _slugRegistry = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _registrationLock = new();

    /// <summary>
    /// Returns the canonical subject ID for a rename or merge operation.
    /// Preserves the current ID when it is already a named identity (not a generated Subject-N slot).
    /// Throws <see cref="SlugCollisionException"/> when the target slug is already registered to another subject.
    /// </summary>
    public static string ResolveCanonicalSubjectId(string currentSubjectId, string displayName, string? existingSlugToPreserve = null)
    {
        if (string.IsNullOrWhiteSpace(currentSubjectId))
        {
            return BuildCanonicalSubjectId(displayName);
        }

        if (!currentSubjectId.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentSubjectId, displayName, StringComparison.OrdinalIgnoreCase))
        {
            return currentSubjectId;
        }

        var canonicalSlug = BuildCanonicalSubjectId(displayName);

        // If the slug matches our existing subject, allow it
        if (string.Equals(existingSlugToPreserve, canonicalSlug, StringComparison.OrdinalIgnoreCase))
        {
            return canonicalSlug;
        }

        // Check for collision with another subject
        if (IsSlugRegisteredByAnotherSubject(canonicalSlug, currentSubjectId))
        {
            // Generate a unique suffix to avoid collision
            return GenerateUniqueSlug(displayName, currentSubjectId);
        }

        return canonicalSlug;
    }

    /// <summary>
    /// Converts a display name to a lowercase URL-safe slug used as the subject storage key.
    /// </summary>
    public static string BuildCanonicalSubjectId(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "unnamed";
        }

        var builder = new StringBuilder();

        foreach (var character in displayName.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>
    /// Registers a slug for a subject. Call after successful rename/merge.
    /// </summary>
    public static void RegisterSlug(string subjectId, string slug)
    {
        lock (_registrationLock)
        {
            if (!_slugRegistry.TryGetValue(slug, out var subjects))
            {
                subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _slugRegistry[slug] = subjects;
            }
            subjects.Add(subjectId);
        }
    }

    /// <summary>
    /// Unregisters a slug when a subject is deleted.
    /// </summary>
    public static void UnregisterSlug(string subjectId, string slug)
    {
        lock (_registrationLock)
        {
            if (_slugRegistry.TryGetValue(slug, out var subjects))
            {
                subjects.Remove(subjectId);
                if (subjects.Count == 0)
                {
                    _slugRegistry.TryRemove(slug, out _);
                }
            }
        }
    }

    /// <summary>
    /// Clears the entire slug registry. Call after a full storage reset to avoid stale collision detection.
    /// </summary>
    public static void Reset()
    {
        lock (_registrationLock)
        {
            _slugRegistry.Clear();
        }
    }

    private static bool IsSlugRegisteredByAnotherSubject(string slug, string currentSubjectId)
    {
        if (_slugRegistry.TryGetValue(slug, out var subjects))
        {
            // Allow if the only registered subject is the current one
            return subjects.Count > 1 || !subjects.Contains(currentSubjectId);
        }
        return false;
    }

    private static string GenerateUniqueSlug(string displayName, string currentSubjectId)
    {
        var baseSlug = BuildCanonicalSubjectId(displayName);
        var slug = baseSlug;
        var counter = 2;

        lock (_registrationLock)
        {
            while (_slugRegistry.TryGetValue(slug, out var subjects) && !subjects.Contains(currentSubjectId))
            {
                slug = $"{baseSlug}-{counter}";
                counter++;
                if (counter > 1000)
                {
                    // Fallback to GUID-based slug
                    return $"{baseSlug}-{Guid.NewGuid():N}".Substring(0, 50);
                }
            }
        }

        return slug;
    }

    /// <summary>
    /// Resets the slug registry. For testing only.
    /// </summary>
    internal static void ResetRegistry()
    {
        _slugRegistry.Clear();
    }
}

/// <summary>
/// Exception thrown when a slug collision is detected during subject ID resolution.
/// </summary>
public sealed class SlugCollisionException : Exception
{
    public string Slug { get; }
    public string ExistingSubjectId { get; }

    public SlugCollisionException(string slug, string existingSubjectId)
        : base($"Slug '{slug}' is already registered to subject '{existingSubjectId}'.")
    {
        Slug = slug;
        ExistingSubjectId = existingSubjectId;
    }
}
