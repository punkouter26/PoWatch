using PoWatch.Domain.Services;

namespace PoWatch.Unit;

/// <summary>
/// Guards the rule that replaced <c>isSignificant = clinicalNote.length &gt; 10</c>. Under that rule
/// every well-formed caption qualified, so 100% of observations arrived flagged "Notable" — the
/// caregiver's triage signal carried no information at all. These tests exist to keep it meaningful:
/// the routine cases below are the ones a real room produces all day, and they must stay unflagged.
/// </summary>
public sealed class ActivitySignificanceClassifierTests
{
    [Theory]
    [InlineData("Person seated using laptop")]
    [InlineData("Person is working at a desk in a well-lit room")]
    [InlineData("A man sitting in front of a computer monitor")]
    [InlineData("The room is empty")]
    [InlineData("Person reading a book")]
    [InlineData("Someone watching television")]
    public void Ordinary_room_activity_is_not_flagged(string activity)
    {
        var verdict = ActivitySignificanceClassifier.Classify(activity, activity);

        Assert.Equal(ActivitySignificance.Routine, verdict.Level);
        Assert.False(verdict.IsSignificant);
        Assert.Null(verdict.Reason);
    }

    [Theory]
    [InlineData("Person has fallen next to the bed")]
    [InlineData("Someone is lying on the floor")]
    [InlineData("A man collapsed near the window")]
    [InlineData("The person fell while walking")]
    public void A_possible_fall_is_urgent(string activity)
    {
        var verdict = ActivitySignificanceClassifier.Classify(activity, string.Empty);

        Assert.Equal(ActivitySignificance.Urgent, verdict.Level);
        Assert.Contains("fall", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("The person is unresponsive")]
    [InlineData("Someone is calling for help")]
    [InlineData("A person appears to be bleeding")]
    [InlineData("The man is having a seizure")]
    public void Signs_of_harm_are_urgent(string activity)
    {
        var verdict = ActivitySignificanceClassifier.Classify(activity, string.Empty);

        Assert.Equal(ActivitySignificance.Urgent, verdict.Level);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
    }

    [Theory]
    [InlineData("A person entering the room", "entered or left")]
    [InlineData("Someone is leaving through the door", "entered or left")]
    [InlineData("Two people are talking", "one person")]
    [InlineData("A person taking their medication", "Medication")]
    [InlineData("The person is eating a meal", "Mealtime")]
    [InlineData("Someone standing up from the chair", "Changed position")]
    public void Everyday_transitions_are_notable_with_a_readable_reason(string activity, string expectedFragment)
    {
        var verdict = ActivitySignificanceClassifier.Classify(activity, string.Empty);

        Assert.Equal(ActivitySignificance.Notable, verdict.Level);
        Assert.True(verdict.IsSignificant);
        Assert.Contains(expectedFragment, verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_clinical_note_is_considered_when_the_caption_is_terse()
    {
        // The caption alone reads routine; the detail is in the note.
        var verdict = ActivitySignificanceClassifier.Classify("Person", "The person has fallen beside the chair.");

        Assert.Equal(ActivitySignificance.Urgent, verdict.Level);
    }

    [Fact]
    public void The_most_severe_matching_rule_wins()
    {
        // Mentions both a fall (urgent) and the door (notable) — a caregiver must be told about the fall.
        var verdict = ActivitySignificanceClassifier.Classify(
            "The person fell while walking to the door", string.Empty);

        Assert.Equal(ActivitySignificance.Urgent, verdict.Level);
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var lower = ActivitySignificanceClassifier.Classify("person has fallen", string.Empty);
        var upper = ActivitySignificanceClassifier.Classify("PERSON HAS FALLEN", string.Empty);

        Assert.Equal(lower.Level, upper.Level);
        Assert.Equal(ActivitySignificance.Urgent, upper.Level);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "  ")]
    public void Empty_input_is_routine_rather_than_throwing(string? activity, string? note)
    {
        var verdict = ActivitySignificanceClassifier.Classify(activity, note);

        Assert.Equal(ActivitySignificance.Routine, verdict.Level);
    }

    [Fact]
    public void A_long_but_ordinary_caption_is_still_routine()
    {
        // The exact failure mode of the old length-based rule: verbose, entirely unremarkable.
        var caption = "The image depicts a man sitting in front of a computer monitor. He is looking "
                    + "to the right side of the image, which is the corner of a large window.";

        Assert.False(ActivitySignificanceClassifier.Classify(caption, caption).IsSignificant);
    }
}
