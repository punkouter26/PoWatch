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
    [Fact]
    public void Ordinary_room_activity_is_not_flagged_And_A_possible_fall_is_urgent()
    {
        // Ordinary_room_activity_is_not_flagged
        {
            foreach (string activity in new string[] { "Person seated using laptop", "Person is working at a desk in a well-lit room", "A man sitting in front of a computer monitor", "The room is empty", "Person reading a book", "Someone watching television" })
            {
                var verdict = ActivitySignificanceClassifier.Classify(activity, activity);

                Assert.Equal(ActivitySignificance.Routine, verdict.Level);
                Assert.False(verdict.IsSignificant);
                Assert.Null(verdict.Reason);

            }

        }
        // A_possible_fall_is_urgent
        {
            foreach (string activity in new string[] { "Person has fallen next to the bed", "Someone is lying on the floor", "A man collapsed near the window", "The person fell while walking", "Resident slipped near the nightstand", "Patient slid to the floor", "Person is sprawled on the floor" })
            {
                var verdict = ActivitySignificanceClassifier.Classify(activity, string.Empty);

                Assert.Equal(ActivitySignificance.Urgent, verdict.Level);
                Assert.Contains("fall", verdict.Reason, StringComparison.OrdinalIgnoreCase);

            }

        }
    }

    [Fact]
    public void Signs_of_harm_are_urgent_And_Everyday_transitions_are_notable_with_a_readable_reason()
    {
        // Signs_of_harm_are_urgent
        {
            foreach (string activity in new string[] { "The person is unresponsive", "Someone is calling for help", "A person appears to be bleeding", "The man is having a seizure", "The person is clutching chest", "Someone is gasping and difficulty breathing" })
            {
                var verdict = ActivitySignificanceClassifier.Classify(activity, string.Empty);

                Assert.Equal(ActivitySignificance.Urgent, verdict.Level);
                Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));

            }

        }
        // Everyday_transitions_are_notable_with_a_readable_reason
        {
            foreach (var (activity, expectedFragment) in new (string activity, string expectedFragment)[]
            {
                ("A person entering the room", "entered or left"),
                ("Someone is leaving through the door", "entered or left"),
                ("Two people are talking", "one person"),
                ("A person taking their medication", "Medication"),
                ("The person is eating a meal", "Mealtime"),
                ("Someone standing up from the chair", "Changed position"),
                ("Patient is sitting on edge of bed", "Changed position"),
                ("Someone attempting to stand", "Changed position"),
                ("Resident pacing in room", "Changed position"),
            })
            {
                var verdict = ActivitySignificanceClassifier.Classify(activity, string.Empty);

                Assert.Equal(ActivitySignificance.Notable, verdict.Level);
                Assert.True(verdict.IsSignificant);
                Assert.Contains(expectedFragment, verdict.Reason, StringComparison.OrdinalIgnoreCase);

            }

        }
    }

    [Fact]
    public void The_clinical_note_is_considered_when_the_caption_is_terse_And_The_most_severe_matching_rule_wins()
    {
        // The_clinical_note_is_considered_when_the_caption_is_terse
        {
            // The caption alone reads routine; the detail is in the note.
            var verdict = ActivitySignificanceClassifier.Classify("Person", "The person has fallen beside the chair.");

            Assert.Equal(ActivitySignificance.Urgent, verdict.Level);

        }
        // The_most_severe_matching_rule_wins
        {
            // Mentions both a fall (urgent) and the door (notable) — a caregiver must be told about the fall.
            var verdict = ActivitySignificanceClassifier.Classify(
                "The person fell while walking to the door", string.Empty);

            Assert.Equal(ActivitySignificance.Urgent, verdict.Level);

        }
    }

    [Fact]
    public void Matching_is_case_insensitive_And_Empty_input_is_routine_rather_than_throwing()
    {
        // Matching_is_case_insensitive
        {
            var lower = ActivitySignificanceClassifier.Classify("person has fallen", string.Empty);
            var upper = ActivitySignificanceClassifier.Classify("PERSON HAS FALLEN", string.Empty);

            Assert.Equal(lower.Level, upper.Level);
            Assert.Equal(ActivitySignificance.Urgent, upper.Level);

        }
        // Empty_input_is_routine_rather_than_throwing
        {
            foreach (var (activity, note) in new (string? activity, string? note)[]
            {
                (null, null),
                ("", ""),
                ("   ", "  "),
            })
            {
                var verdict = ActivitySignificanceClassifier.Classify(activity, note);

                Assert.Equal(ActivitySignificance.Routine, verdict.Level);

            }

        }
    }

    [Fact]
    public void A_long_but_ordinary_caption_is_still_routine_And_Routine_input_emits_zero_score_with_unit_confidence()
    {
        // A_long_but_ordinary_caption_is_still_routine
        {
            // The exact failure mode of the old length-based rule: verbose, entirely unremarkable.
            var caption = "The image depicts a man sitting in front of a computer monitor. He is looking "
                        + "to the right side of the image, which is the corner of a large window.";

            Assert.False(ActivitySignificanceClassifier.Classify(caption, caption).IsSignificant);

        }
        // Routine_input_emits_zero_score_with_unit_confidence
        {
            var verdict = ActivitySignificanceClassifier.Classify("Person seated using laptop", null);

            Assert.Equal(0.0, verdict.Score);
            Assert.Equal(1.0, verdict.Confidence);

        }
    }

    [Fact]
    public void Empty_input_emits_zero_score_with_floor_confidence_And_A_match_emits_a_positive_score_below_one()
    {
        // Empty_input_emits_zero_score_with_floor_confidence
        {
            // The ConfidenceFloor exists so a routine verdict never reads as "I know nothing" — the band
            // itself does the gating, and the floor keeps the number from being alarming.
            var verdict = ActivitySignificanceClassifier.Classify(string.Empty, string.Empty);

            Assert.Equal(0.0, verdict.Score);
            Assert.InRange(verdict.Confidence, 0.25, 0.5);

        }
        // A_match_emits_a_positive_score_below_one
        {
            foreach (string activity in new string[] { "fell", "collapsed", "The person fell", "Someone collapsed nearby" })
            {
                // Each of these inputs matches at least one fall phrase but is short enough to land below
                // the saturation threshold (matchedInRule * 0.5 < 1.0). The exact Score is not the property
                // we care about; the property is "positive, less than one, and definitely not zero".
                var verdict = ActivitySignificanceClassifier.Classify(activity, string.Empty);

                Assert.Equal(ActivitySignificance.Urgent, verdict.Level);
                Assert.InRange(verdict.Score, 0.5, 1.0);
                Assert.True(verdict.Score > 0.0);

            }

        }
    }

    [Fact]
    public void Score_saturates_at_one_for_strong_signal_input_And_Score_is_monotonic_in_signal_strength()
    {
        // Score_saturates_at_one_for_strong_signal_input
        {
            // A caption that hits multiple fall-related phrases must read as "as strong as it gets",
            // not as 0.5 per match. This is the property the heatmap's soft-opacity rendering relies on.
            var verdict = ActivitySignificanceClassifier.Classify(
                "The person fell, is on the floor, and is not moving.", string.Empty);

            Assert.Equal(1.0, verdict.Score, precision: 3);

        }
        // Score_is_monotonic_in_signal_strength
        {
            // Two phrases → stronger score than one phrase. The fall rule has overlapping phrase
            // spellings ("has fallen" and "fallen" both match in many inputs) so we pick a sentence
            // that hits the rule once, then a longer one that hits it multiple times.
            var onePhrase = ActivitySignificanceClassifier.Classify("Someone collapsed", string.Empty);
            var manyPhrases = ActivitySignificanceClassifier.Classify(
                "Someone collapsed and is lying on the floor motionless.", string.Empty);

            Assert.Equal(ActivitySignificance.Urgent, onePhrase.Level);
            Assert.Equal(ActivitySignificance.Urgent, manyPhrases.Level);
            Assert.True(onePhrase.Score < manyPhraseScore(manyPhrases),
                $"Expected one-phrase score {onePhrase.Score} to be strictly less than many-phrase score.");
            Assert.Equal(1.0, manyPhrases.Score, precision: 3);

            static double manyPhraseScore(SignificanceVerdict v) => v.Score;

        }
    }

    [Fact]
    public void Short_input_lowers_confidence_even_when_score_matches_And_Score_and_confidence_are_independent_dimensions()
    {
        // Short_input_lowers_confidence_even_when_score_matches
        {
            // "collapsed" and "The person collapsed on the carpet by the sofa." both match the same
            // single fall phrase, so their Score is identical; only the input length differs, which is
            // what the Confidence formula must reflect.
            var shortVerdict = ActivitySignificanceClassifier.Classify("collapsed", string.Empty);
            var longVerdict = ActivitySignificanceClassifier.Classify(
                "The person collapsed on the carpet by the sofa.", string.Empty);

            Assert.Equal(shortVerdict.Level, longVerdict.Level);
            Assert.Equal(shortVerdict.Score, longVerdict.Score);
            Assert.True(shortVerdict.Confidence < longVerdict.Confidence,
                $"Expected short confidence {shortVerdict.Confidence} to be lower than long confidence {longVerdict.Confidence}.");

        }
        // Score_and_confidence_are_independent_dimensions
        {
            // A two-band scenario is hard to construct because the first rule that wins is the one we use,
            // but the algebra itself must hold: an empty input yields 0 score + floor confidence;
            // a single-phrase match yields positive score + higher confidence (longer input).
            var empty = ActivitySignificanceClassifier.Classify(string.Empty, string.Empty);
            var strong = ActivitySignificanceClassifier.Classify(
                "The person fell and is lying on the floor calling for help.", string.Empty);

            Assert.True(empty.Score < strong.Score);
            Assert.True(empty.Confidence <= strong.Confidence);

        }
    }

}
