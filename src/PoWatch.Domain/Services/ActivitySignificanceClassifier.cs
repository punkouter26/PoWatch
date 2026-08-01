namespace PoWatch.Domain.Services;

/// <summary>
/// How much attention an observed activity deserves.
/// </summary>
public enum ActivitySignificance
{
    /// <summary>Ordinary room activity. Recorded, but not surfaced as needing attention.</summary>
    Routine,

    /// <summary>Worth a caregiver's notice — a transition, a meal, medication, a visitor.</summary>
    Notable,

    /// <summary>Possible harm. Surfaced immediately.</summary>
    Urgent
}

/// <summary>The classifier's verdict, with the plain-language reason shown to the caregiver.</summary>
public readonly record struct SignificanceVerdict(ActivitySignificance Level, string? Reason)
{
    public bool IsSignificant => Level != ActivitySignificance.Routine;

    public static SignificanceVerdict Routine { get; } = new(ActivitySignificance.Routine, null);
}

/// <summary>
/// Decides whether an observed activity is worth a caregiver's attention.
/// <para>
/// This replaces the client-side heuristic <c>isSignificant = clinicalNote.length &gt; 10</c>, under which
/// every well-formed caption qualified. In practice that flagged 100% of observations as "Notable", which
/// lit every person card amber, inflated the unacknowledged-alert counters, uploaded an evidence image per
/// cycle and announced every frame aloud. A flag that always fires carries no information, and it is the
/// signal a caregiver is meant to triage by.
/// </para>
/// <para>
/// It also lives on the server rather than in the inference worker, so significance cannot be asserted by
/// whatever is posting to the ingest endpoint, and so it is unit-testable without a browser.
/// </para>
/// </summary>
public static class ActivitySignificanceClassifier
{
    // Ordered most-severe first: the first matching rule wins, so "fell while walking to the door"
    // reads as a possible fall rather than as movement through the room.
    private static readonly (ActivitySignificance Level, string Reason, string[] Phrases)[] Rules =
    [
        (ActivitySignificance.Urgent, "Possible fall — someone appears to be on the floor",
            ["fallen", "has fallen", "fell", "falling", "collapsed", "on the floor", "on the ground",
             "lying on the floor", "lying on the ground", "slumped", "face down"]),

        (ActivitySignificance.Urgent, "Someone may need help",
            ["unconscious", "unresponsive", "not moving", "motionless", "seizure", "convulsing",
             "calling for help", "waving for help", "bleeding", "blood", "injured", "in pain"]),

        (ActivitySignificance.Notable, "Signs of distress",
            ["distress", "distressed", "crying", "agitated", "shouting", "screaming", "upset",
             "holding their head", "holding their chest"]),

        (ActivitySignificance.Notable, "Someone entered or left the room",
            ["entering", "entered", "enters", "leaving", "left the room", "walking out", "walking in",
             "coming in", "going out", "opening the door", "at the door", "in the doorway"]),

        (ActivitySignificance.Notable, "More than one person in view",
            ["two people", "three people", "several people", "a group of", "another person",
             "someone else", "two men", "two women", "a visitor"]),

        (ActivitySignificance.Notable, "Medication activity",
            ["medication", "medicine", "pills", "tablets", "inhaler", "syringe", "injection"]),

        (ActivitySignificance.Notable, "Mealtime activity",
            ["eating", "drinking", "having a meal", "having lunch", "having dinner", "having breakfast",
             "feeding", "a glass of water", "a cup of"]),

        (ActivitySignificance.Notable, "Changed position",
            ["standing up", "stands up", "getting up", "gets up", "sitting down", "lying down",
             "getting into bed", "getting out of bed", "climbing", "reaching up", "bending over",
             "stumbling", "unsteady", "holding onto"]),
    ];

    /// <summary>
    /// Classifies an observation from the model's caption and the extracted clinical note.
    /// Both are considered, because the caption is often terse ("Person on the floor") while the note
    /// carries the detail — and the reverse happens just as often.
    /// </summary>
    public static SignificanceVerdict Classify(string? activity, string? clinicalDescription)
    {
        var haystack = $"{activity} {clinicalDescription}";
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return SignificanceVerdict.Routine;
        }

        foreach (var (level, reason, phrases) in Rules)
        {
            foreach (var phrase in phrases)
            {
                if (haystack.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    return new SignificanceVerdict(level, reason);
                }
            }
        }

        return SignificanceVerdict.Routine;
    }
}
