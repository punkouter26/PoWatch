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

    [Fact]
    public void TrySanitize_Rejects_degenerate_or_non_observational_text()
    {
        // Tautologies, in the payload or the activity, name why they were rejected.
        foreach (var (payload, activity) in new (string payload, string activity)[]
        {
            ("The man is a man", "standing still"),
            ("A dog is a dog", "lying on floor"),
            ("the woman is a woman", "seated"),
            ("person is person", "at desk"),
            ("standing still", "The man is a man"),
            ("normal observation", "the dog is a dog"),
        })
        {
            var result = Sut.TrySanitize(ValidRequest(payload, activity), out _, out var reason);

            Assert.False(result);
            Assert.Contains("tautological", reason, StringComparison.OrdinalIgnoreCase);
        }

        // Short consecutive repeats, low diversity, empty text, prompt leakage and bare markers.
        foreach (var (payload, activity) in new (string payload, string activity)[]
        {
            ("man man standing", "present"),
            ("room room rest and more", "present"),
            ("the man in the room the man in the room", "observation"),
            ("cat cat cat cat cat", "animal seen"),
            ("   ", "activity"),
            ("you are a clinical room observer and you should describe what you see briefly", "monitoring"),
            ("yes", "yes"),
        })
        {
            Assert.False(Sut.TrySanitize(ValidRequest(payload, activity), out _, out _), payload);
        }
    }

    [Fact]
    public void TrySanitize_Accepts_real_observations()
    {
        foreach (var (payload, activity) in new (string payload, string activity)[]
        {
            // "man" in the subject with a different predicate must not read as a tautology.
            ("The man is sitting at a desk working", "desk work"),
            ("The person is standing up close to the camera", "standing near camera"),
            ("Subject appears to be working at a standing desk with a laptop", "desk work"),
            ("Individual seated, looking at a monitor, occasional head movement", "computer use"),
        })
        {
            Assert.True(Sut.TrySanitize(ValidRequest(payload, activity), out _, out _), payload);
        }
    }
}
