using PoWatch.Shared.Models;

namespace PoWatch.Unit;

/// <summary>
/// One person must never appear under two names. Before this helper was shared, the client
/// humanized "Subject-116" to "Person 116" in its cards and timeline while the People table, the
/// Archives timeline, the daily narrative and the handoff PDF printed the raw storage id — so a
/// card could not be matched to its row on the same screen.
/// </summary>
public sealed class SubjectDisplayNamesTests
{
    [Fact]
    public void Auto_generated_ids_become_person_numbers_And_Real_names_pass_through_untouched()
    {
        // Auto_generated_ids_become_person_numbers
        {
            foreach (var (stored, expected) in new (string stored, string expected)[]
            {
                ("Subject-1", "Person 1"),
                ("Subject-116", "Person 116"),
                ("subject-42", "Person 42"),
                ("SUBJECT-7", "Person 7"),
            })
            {
                Assert.Equal(expected, SubjectDisplayNames.Humanize(stored));
            }

        }
        // Real_names_pass_through_untouched
        {
            foreach (string name in new string[] { "Mom", "Kim", "Dr. Alvarez" })
            {
                Assert.Equal(name, SubjectDisplayNames.Humanize(name));
            }

        }
    }

    [Fact]
    public void A_known_identity_is_never_rewritten_even_if_it_looks_like_an_id_And_Missing_names_render_as_unknown_rather_than_blank()
    {
        // A_known_identity_is_never_rewritten_even_if_it_looks_like_an_id
        {
            Assert.Equal("Subject-9", SubjectDisplayNames.Humanize("Subject-9", isKnownIdentity: true));
        }
        // Missing_names_render_as_unknown_rather_than_blank
        {
            foreach (string? stored in new string?[] { null, "", "   " })
            {
                Assert.Equal("Unknown person", SubjectDisplayNames.Humanize(stored));
            }

        }
    }

    [Fact]
    public void Near_misses_are_left_alone_And_Humanizing_is_idempotent()
    {
        // Near_misses_are_left_alone
        {
            foreach (string stored in new string[] { "Subject-", "Subject-abc", "Subject-12b", "MySubject-12" })
            {
                Assert.Equal(stored, SubjectDisplayNames.Humanize(stored));
            }

        }
        // Humanizing_is_idempotent
        {
            Assert.Equal("Person 116", SubjectDisplayNames.Humanize(SubjectDisplayNames.Humanize("Subject-116")));
        }
    }

}
