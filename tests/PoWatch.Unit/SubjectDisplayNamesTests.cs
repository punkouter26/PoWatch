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
    [Theory]
    [InlineData("Subject-1", "Person 1")]
    [InlineData("Subject-116", "Person 116")]
    [InlineData("subject-42", "Person 42")]
    [InlineData("SUBJECT-7", "Person 7")]
    public void Auto_generated_ids_become_person_numbers(string stored, string expected) =>
        Assert.Equal(expected, SubjectDisplayNames.Humanize(stored));

    [Theory]
    [InlineData("Mom")]
    [InlineData("Kim")]
    [InlineData("Dr. Alvarez")]
    public void Real_names_pass_through_untouched(string name) =>
        Assert.Equal(name, SubjectDisplayNames.Humanize(name));

    [Fact]
    public void A_known_identity_is_never_rewritten_even_if_it_looks_like_an_id() =>
        Assert.Equal("Subject-9", SubjectDisplayNames.Humanize("Subject-9", isKnownIdentity: true));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_names_render_as_unknown_rather_than_blank(string? stored) =>
        Assert.Equal("Unknown person", SubjectDisplayNames.Humanize(stored));

    [Theory]
    [InlineData("Subject-")]
    [InlineData("Subject-abc")]
    [InlineData("Subject-12b")]
    [InlineData("MySubject-12")]
    public void Near_misses_are_left_alone(string stored) =>
        Assert.Equal(stored, SubjectDisplayNames.Humanize(stored));

    [Fact]
    public void Humanizing_is_idempotent() =>
        Assert.Equal("Person 116", SubjectDisplayNames.Humanize(SubjectDisplayNames.Humanize("Subject-116")));
}
