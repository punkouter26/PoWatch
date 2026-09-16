using PoWatch.Infrastructure.Runtime;
using PoWatch.Shared.Models;

namespace PoWatch.Unit;

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

    [Fact]
    public void TrySanitize_Rejects_TautologicalPayload()
    {
        foreach (var (payload, activity) in new (string payload, string activity)[]
        {
            ("The man is a man", "standing still"),
            ("A dog is a dog", "lying on floor"),
            ("the woman is a woman", "seated"),
            ("person is person", "at desk"),
        })
        {
            var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out var reason);

            Assert.False(result);
            Assert.Contains("tautological", reason, StringComparison.OrdinalIgnoreCase);

        }
    }

    [Fact]
    public void TrySanitize_Rejects_TautologicalActivity()
    {
        foreach (var (payload, activity) in new (string payload, string activity)[]
        {
            ("standing still", "The man is a man"),
            ("normal observation", "the dog is a dog"),
        })
        {
            var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out var reason);

            Assert.False(result);
            Assert.Contains("tautological", reason, StringComparison.OrdinalIgnoreCase);

        }
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

    [Fact]
    public void TrySanitize_Rejects_ShortTextConsecutiveRepeat()
    {
        foreach (var (payload, activity) in new (string payload, string activity)[]
        {
            ("man man standing", "present"),
            ("room room rest and more", "present"),
        })
        {
            var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out _);

            Assert.False(result);

        }
    }

    // ── Low diversity (5+ tokens) ──────────────────────────────────────────────

    [Fact]
    public void TrySanitize_Rejects_LowDiversityText()
    {
        foreach (var (payload, activity) in new (string payload, string activity)[]
        {
            ("the man in the room the man in the room", "observation"),
            ("cat cat cat cat cat", "animal seen"),
        })
        {
            var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out _);

            Assert.False(result);

        }
    }

    // ── Legitimate observations still pass ────────────────────────────────────

    [Fact]
    public void TrySanitize_Accepts_GoodObservations()
    {
        foreach (var (payload, activity) in new (string payload, string activity)[]
        {
            ("The person is standing up close to the camera", "standing near camera"),
            ("Subject appears to be working at a standing desk with a laptop", "desk work"),
            ("Individual seated, looking at a monitor, occasional head movement", "computer use"),
        })
        {
            var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out _);

            Assert.True(result);

        }
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
