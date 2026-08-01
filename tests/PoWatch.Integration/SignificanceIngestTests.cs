using System.Net;
using System.Net.Http.Json;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Integration;

/// <summary>
/// Significance end to end against real storage: what the classifier decides is what gets persisted,
/// what comes back on the response, and what the archive treats as a highlight.
/// </summary>
public sealed class SignificanceIngestTests(AzuriteWebApplicationFactory factory)
    : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Routine_activity_round_trips_unflagged()
    {
        var result = await IngestAsync("Person seated using laptop", "<S>Person seated using laptop.<E>");

        Assert.True(result.Accepted);
        Assert.False(result.IsSignificant);
        Assert.Null(result.SignificantReason);
    }

    [Fact]
    public async Task A_fall_round_trips_flagged_with_a_readable_reason()
    {
        var result = await IngestAsync("Person has fallen by the door", "<S>Person has fallen by the door.<E>");

        Assert.True(result.IsSignificant);
        Assert.Contains("fall", result.SignificantReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_client_claiming_significance_on_routine_activity_is_overruled()
    {
        var result = await IngestAsync(
            "Person seated using laptop",
            "<S>Person seated using laptop.<E>",
            claimSignificant: true);

        Assert.False(result.IsSignificant);
    }

    [Fact]
    public async Task Only_flagged_events_reserve_an_image_reference()
    {
        var routine = await IngestAsync("Person reading a book", "<S>Person reading a book.<E>");
        var flagged = await IngestAsync("A person entering the room", "<S>A person entering the room.<E>");

        Assert.Null(routine.ImageReference);
        Assert.False(string.IsNullOrWhiteSpace(flagged.ImageReference));
    }

    [Fact]
    public async Task Flagged_events_become_archive_highlights_and_routine_ones_do_not()
    {
        await IngestAsync("Person is eating a meal", "<S>Person is eating a meal.<E>");
        await IngestAsync("Person seated using laptop", "<S>Person seated using laptop.<E>");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var chapter = await _client.GetFromJsonAsync<DailyChapter>($"/api/archives/{today:yyyy-MM-dd}");

        Assert.NotNull(chapter);
        Assert.Contains(chapter!.Highlights, h => h.Activity.Contains("eating", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(chapter.Highlights, h => h.Activity.Contains("laptop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_daily_narrative_never_prints_a_raw_subject_id()
    {
        await IngestAsync("Person seated using laptop", "<S>Person seated using laptop.<E>");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var chapter = await _client.GetFromJsonAsync<DailyChapter>($"/api/archives/{today:yyyy-MM-dd}");

        Assert.NotNull(chapter);
        Assert.DoesNotContain("Subject-", chapter!.ClinicalNarrative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_explicit_caller_reason_survives_the_round_trip()
    {
        var response = await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            SubjectHint = "integration-explicit",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Known subject resumed desk work.<E>",
            IsSignificant = true,
            SignificantReason = "Known person entered"
        });

        var result = await response.Content.ReadFromJsonAsync<IngestObservationResultDto>();

        Assert.NotNull(result);
        Assert.True(result!.IsSignificant);
        Assert.Equal("Known person entered", result.SignificantReason);
    }

    [Fact]
    public async Task A_malformed_payload_is_recorded_as_an_outlier()
    {
        var result = await IngestAsync("Unknown movement", "not a tagged payload");

        Assert.True(result.Accepted);
        Assert.True(result.IsOutlier);
    }

    [Fact]
    public async Task Ingest_is_idempotent_for_a_repeated_key()
    {
        var key = Guid.NewGuid();

        var first = await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            SubjectHint = "integration-idempotent",
            Activity = "Person seated using laptop",
            ClinicalPayload = "<S>Person seated using laptop.<E>",
            IdempotencyKey = key
        });
        var second = await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            SubjectHint = "integration-idempotent",
            Activity = "Person seated using laptop",
            ClinicalPayload = "<S>Person seated using laptop.<E>",
            IdempotencyKey = key
        });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var a = await first.Content.ReadFromJsonAsync<IngestObservationResultDto>();
        var b = await second.Content.ReadFromJsonAsync<IngestObservationResultDto>();
        Assert.Equal(a!.EventId, b!.EventId);
    }

    [Fact]
    public async Task The_server_timestamps_the_observation_and_ignores_a_backdated_client_clock()
    {
        var response = await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            SubjectHint = "integration-backdate",
            Activity = "Person seated using laptop",
            ClinicalPayload = "<S>Person seated using laptop.<E>",
            ObservedAtUtc = DateTimeOffset.UtcNow.AddYears(-5)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var chapter = await _client.GetFromJsonAsync<DailyChapter>($"/api/archives/{today:yyyy-MM-dd}");

        // Backdating would have filed it under a 5-year-old partition, so today's chapter would miss it.
        Assert.NotNull(chapter);
        Assert.Contains(chapter!.Timeline, e => e.SubjectDisplayName.Length > 0);
    }

    private async Task<IngestObservationResultDto> IngestAsync(
        string activity,
        string payload,
        bool claimSignificant = false)
    {
        var response = await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            SubjectHint = $"integration-{Guid.NewGuid():N}",
            Activity = activity,
            ClinicalPayload = payload,
            IsSignificant = claimSignificant
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IngestObservationResultDto>();
        Assert.NotNull(result);
        return result!;
    }
}
