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

/// <summary>How the daily chapter's narrative should be rendered. The prose view speaks the day
/// in one paragraph; the structured view tabulates the same events as time-stamped rows. Both
/// are always populated server-side so a client-side toggle never refetches.</summary>
public enum ActivitySignificanceNarrativeMode
{
    Prose = 0,
    Structured = 1,
}

/// <summary>The classifier's verdict, with the plain-language reason shown to the caregiver.</summary>
/// <param name="Level">Discrete band the observation falls into. Authoritative: alert gates and
/// ingest responses filter on this, never on the numeric values below.</param>
/// <param name="Reason">Plain-language explanation of why the band was chosen, or null when routine.</param>
/// <param name="Score">Strength of the underlying signal itself, in [0.0, 1.0]. Two matched rule
/// phrases → 1.0; one phrase → 0.5; Routine with no matches → 0.0. Lets the client render soft
/// gradients (heatmaps, pattern bars) instead of discrete steps.</param>
/// <param name="Confidence">How sure the classifier is that the chosen band is the correct one, in
/// [0.0, 1.0]. Discounted by short / no-letter input and by partial-match ratios. Independent of
/// <paramref name="Score"/>: a long input with one weak phrase scores high-confidence / low-score,
/// and a short input with a strong phrase scores low-confidence / high-score.</param>
public readonly record struct SignificanceVerdict(
    ActivitySignificance Level,
    string? Reason,
    double Score,
    double Confidence)
{
    public bool IsSignificant => Level != ActivitySignificance.Routine;

    /// <summary>The canonical Routine reading: no signal, fully confident there isn't one.</summary>
    public static SignificanceVerdict Routine { get; } = new(ActivitySignificance.Routine, null, 0.0, 1.0);

    /// <summary>The "we couldn't even look" reading: empty input short-circuits here. Score is 0
    /// because there is no signal, but Confidence is the floor because there is no information to
    /// be confident in either.</summary>
    public static SignificanceVerdict EmptyInput { get; } = new(ActivitySignificance.Routine, null, 0.0, 0.25);
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
            return SignificanceVerdict.EmptyInput;
        }

        // First matching rule wins, but we still count how many phrases matched within that rule so
        // the Score reflects "how strongly the signal spoke" rather than "did it speak at all". A single
        // match for "Possible fall" reads as 0.5 strength; matching both "fell" and "on the floor" reaches 1.0.
        foreach (var (level, reason, phrases) in Rules)
        {
            var matchedInRule = 0;
            foreach (var phrase in phrases)
            {
                if (haystack.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    matchedInRule++;
                }
            }

            if (matchedInRule > 0)
            {
                return new SignificanceVerdict(
                    Level: level,
                    Reason: reason,
                    Score: MatchedScore(matchedInRule),
                    Confidence: ConfidenceFor(haystack));
            }
        }

        return SignificanceVerdict.Routine;
    }

    // Two matched phrases saturate at 1.0; one phrase is 0.5 (a single weak signal). The cap protects
    // against accidental runs through the phrase list that would otherwise produce 0.5*N scores.
    private static double MatchedScore(int matchedPhrases) =>
        Math.Min(1.0, 0.5 * matchedPhrases);

    // Discount by input length (shorter input → less confidence) and by letter content (anything with
    // three or fewer letters is unlikely to convey a reliable signal). The minimum is intentionally
    // generous — we want the band itself to do the gating, and the confidence number is informational.
    private static double ConfidenceFor(string haystack)
    {
        var lengthFactor = Math.Min(1.0, haystack.Length / 60.0);
        var letterCount = 0;
        foreach (var c in haystack)
        {
            if (char.IsLetter(c))
                letterCount++;
        }
        var letterFactor = Math.Min(1.0, letterCount / 12.0);
        return Math.Clamp(lengthFactor * (0.5 + 0.5 * letterFactor), 0.25, 1.0);
    }
}
