using PoWatch.Infrastructure.Runtime;
using PoWatch.Shared.Models;

namespace PoWatch.Tests;

public sealed class TelemetryContentSanitizerTests
{
    private static readonly TelemetryContentSanitizer Sut = new();

    private static IngestObservationRequestDto ValidRequest(string payload, string activity) => new()
    {
        ClinicalPayload = payload,
        Activity = activity,
        ObservedAtUtc = DateTimeOffset.UtcNow
    };

    // ── Tautology ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("The man is a man", "standing still")]
    [InlineData("A dog is a dog", "lying on floor")]
    [InlineData("the woman is a woman", "seated")]
    [InlineData("person is person", "at desk")]
    public void TrySanitize_Rejects_TautologicalPayload(string payload, string activity)
    {
        var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out var reason);

        Assert.False(result);
        Assert.Contains("tautological", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("standing still", "The man is a man")]
    [InlineData("normal observation", "the dog is a dog")]
    public void TrySanitize_Rejects_TautologicalActivity(string payload, string activity)
    {
        var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out var reason);

        Assert.False(result);
        Assert.Contains("tautological", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrySanitize_Accepts_NonTautologicalSentence()
    {
        // "man" in subject, different predicate — must not be caught
        var result = Sut.TrySanitize(
            ValidRequest("The man is sitting at a desk working", "desk work"),
            out _, out _);

        Assert.True(result);
    }

    // ── Short-text consecutive repeat ──────────────────────────────────────────

    [Theory]
    [InlineData("man man standing", "present")]  // "man man" = 2 consecutive in 3-token text (threshold=2 for ≤5)
    [InlineData("room room rest and more", "present")] // "room room" = 2 consecutive in 5-token text
    public void TrySanitize_Rejects_ShortTextConsecutiveRepeat(string payload, string activity)
    {
        var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out _);

        Assert.False(result);
    }

    // ── Low diversity (5+ tokens) ──────────────────────────────────────────────

    [Theory]
    [InlineData("the man in the room the man in the room", "observation")] // 10 tokens, 4 unique = 0.40 < 0.45
    [InlineData("cat cat cat cat cat", "animal seen")]                     // 5 consecutive repeats → caught early
    public void TrySanitize_Rejects_LowDiversityText(string payload, string activity)
    {
        var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out _);

        Assert.False(result);
    }

    // ── Legitimate observations still pass ────────────────────────────────────

    [Theory]
    [InlineData("The person is standing up close to the camera", "standing near camera")]
    [InlineData("Subject appears to be working at a standing desk with a laptop", "desk work")]
    [InlineData("Individual seated, looking at a monitor, occasional head movement", "computer use")]
    public void TrySanitize_Accepts_GoodObservations(string payload, string activity)
    {
        var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out _);

        Assert.True(result);
    }

    // ── Other rejection cases (regression) ────────────────────────────────────

    [Fact]
    public void TrySanitize_Rejects_EmptyPayload()
    {
        var result = Sut.TrySanitize(ValidRequest("   ", "activity"), out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TrySanitize_Rejects_PromptLeakage()
    {
        var result = Sut.TrySanitize(
            ValidRequest("you are a clinical room observer and you should describe what you see briefly", "monitoring"),
            out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TrySanitize_Rejects_NonObservationalMarker()
    {
        var result = Sut.TrySanitize(ValidRequest("yes", "yes"), out _, out _);

        Assert.False(result);
    }
}
