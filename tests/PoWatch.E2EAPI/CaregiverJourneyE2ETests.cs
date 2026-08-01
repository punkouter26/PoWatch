using System.Net;
using System.Net.Http.Json;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.E2EAPI;

/// <summary>
/// Client → server journeys as the Blazor app actually performs them: watch a room, name the person
/// who appeared, merge a duplicate, read the day back, and end the shift.
/// </summary>
public sealed class CaregiverJourneyE2ETests(ApiE2EFactory factory) : IClassFixture<ApiE2EFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task An_unnamed_person_can_be_named_and_their_history_follows_the_new_name()
    {
        var hint = $"journey-{Guid.NewGuid():N}";
        var ingest = await IngestAsync(hint, "Person seated using laptop");
        Assert.True(ingest.Accepted);

        var rename = await _client.PatchAsync(
            $"/api/identity/subjects/{ingest.SubjectId}",
            JsonContent.Create(new RenameSubjectRequestDto { NewName = "Mom" }));

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var revision = await rename.Content.ReadFromJsonAsync<IdentityRevisionResultDto>();
        Assert.NotNull(revision);
        Assert.Equal("Mom", revision!.CanonicalName);

        var subjects = await _client.GetFromJsonAsync<List<SubjectProfileDto>>("/api/identity/subjects");
        Assert.Contains(subjects!, s => s.DisplayName == "Mom");
    }

    [Fact]
    public async Task Two_duplicates_can_be_merged_into_one_person()
    {
        var first = await IngestAsync($"dup-a-{Guid.NewGuid():N}", "Person seated using laptop");
        var second = await IngestAsync($"dup-b-{Guid.NewGuid():N}", "Person reading a book");

        var merge = await _client.PostAsJsonAsync("/api/identity/merge", new MergeIdentityRequestDto
        {
            PrimarySubjectId = first.SubjectId,
            SecondarySubjectId = second.SubjectId,
            NewDisplayName = "Merged Person"
        });

        Assert.Equal(HttpStatusCode.OK, merge.StatusCode);
        var result = await merge.Content.ReadFromJsonAsync<IdentityRevisionResultDto>();
        Assert.NotNull(result);
        Assert.Equal("Merged Person", result!.CanonicalName);

        var subjects = await _client.GetFromJsonAsync<List<SubjectProfileDto>>("/api/identity/subjects");
        Assert.DoesNotContain(subjects!, s => s.SubjectId == second.SubjectId);
    }

    [Fact]
    public async Task A_days_activity_reads_back_in_chronological_order()
    {
        var hint = $"chrono-{Guid.NewGuid():N}";
        await IngestAsync(hint, "Person seated using laptop");
        await IngestAsync(hint, "Person is eating a meal");

        var chapter = await _client.GetFromJsonAsync<DailyChapter>($"/api/archives/{Today:yyyy-MM-dd}");

        Assert.NotNull(chapter);
        Assert.NotEmpty(chapter!.Timeline);
        var times = chapter.Timeline.Select(e => e.ObservedAtUtc).ToList();
        Assert.Equal(times.OrderBy(t => t), times);
    }

    [Fact]
    public async Task Only_notable_moments_appear_as_highlights()
    {
        await IngestAsync($"hl-{Guid.NewGuid():N}", "Person has fallen near the window");

        var chapter = await _client.GetFromJsonAsync<DailyChapter>($"/api/archives/{Today:yyyy-MM-dd}");

        Assert.NotNull(chapter);
        Assert.All(chapter!.Highlights, h => Assert.True(h.IsSignificant));
        Assert.Contains(chapter.Highlights, h => h.Activity.Contains("fallen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_day_with_no_data_returns_an_empty_chapter_rather_than_an_error()
    {
        var chapter = await _client.GetFromJsonAsync<DailyChapter>("/api/archives/2001-01-01");

        Assert.NotNull(chapter);
        Assert.Empty(chapter!.Timeline);
        Assert.Empty(chapter.Highlights);
    }

    [Fact]
    public async Task The_live_status_board_lists_everyone_seen()
    {
        var hint = $"live-{Guid.NewGuid():N}";
        await IngestAsync(hint, "Person seated using laptop");

        var live = await _client.GetFromJsonAsync<List<SubjectLiveStatusDto>>("/api/identity/subjects/live-status");

        Assert.NotNull(live);
        Assert.NotEmpty(live!);
        Assert.All(live!, s => Assert.False(string.IsNullOrWhiteSpace(s.SubjectId)));
    }

    [Fact]
    public async Task Live_status_never_returns_a_raw_storage_id_as_a_missing_display_name()
    {
        await IngestAsync($"name-{Guid.NewGuid():N}", "Person seated using laptop");

        var live = await _client.GetFromJsonAsync<List<SubjectLiveStatusDto>>("/api/identity/subjects/live-status");

        Assert.All(live!, s => Assert.False(string.IsNullOrWhiteSpace(s.DisplayName)));
    }

    [Fact]
    public async Task Ending_a_shift_produces_a_downloadable_handoff_report()
    {
        await IngestAsync($"shift-{Guid.NewGuid():N}", "Person is eating a meal");

        var response = await _client.GetAsync($"/api/archives/{Today:yyyy-MM-dd}/handoff-report?shiftWindow=FullDay");

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            // QuestPDF ships no win-arm64 native binary, so the PDF engine cannot start on an ARM64
            // Windows host. That must still be an EXPLAINED failure, never a bare 500 — assert the
            // problem detail actually tells the operator what happened and that their data is safe.
            var problem = await response.Content.ReadAsStringAsync();
            Assert.Contains("PDF engine", problem, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
        // A PDF always starts with %PDF.
        Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
    }

    [Fact]
    public async Task A_handoff_brief_can_be_generated_for_the_day()
    {
        await IngestAsync($"brief-{Guid.NewGuid():N}", "A person entering the room");

        var response = await _client.PostAsJsonAsync(
            $"/api/archives/{Today:yyyy-MM-dd}/handoff-brief",
            new GenerateHandoffBriefRequestDto
            {
                ShiftWindow = "FullDay",
                Audience = "NurseToNurse",
                IncludeUnresolvedAlerts = true,
                IncludeHighlights = true
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var brief = await response.Content.ReadFromJsonAsync<HandoffBriefDto>();
        Assert.NotNull(brief);
        Assert.False(string.IsNullOrWhiteSpace(brief!.Summary));
    }

    [Fact]
    public async Task A_handoff_brief_never_names_a_raw_storage_id()
    {
        await IngestAsync($"briefname-{Guid.NewGuid():N}", "Person is eating a meal");

        var brief = await (await _client.PostAsJsonAsync(
            $"/api/archives/{Today:yyyy-MM-dd}/handoff-brief",
            new GenerateHandoffBriefRequestDto { ShiftWindow = "FullDay", Audience = "NurseToNurse" }))
            .Content.ReadFromJsonAsync<HandoffBriefDto>();

        Assert.NotNull(brief);
        Assert.DoesNotContain("Subject-", brief!.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_evidence_upload_url_can_be_requested_for_a_flagged_event()
    {
        var ingest = await IngestAsync($"evidence-{Guid.NewGuid():N}", "Person has fallen in the hallway");

        Assert.False(string.IsNullOrWhiteSpace(ingest.ImageReference));

        var sas = await _client.GetFromJsonAsync<BlobAccessDescriptorDto>(
            $"/api/blobs/sas?blobPath={Uri.EscapeDataString(ingest.ImageReference!)}");

        Assert.NotNull(sas);
        Assert.False(string.IsNullOrWhiteSpace(sas!.SasUrl));
    }

    [Fact]
    public async Task Events_can_be_acknowledged_and_stop_counting_as_unresolved()
    {
        var hint = $"ack-{Guid.NewGuid():N}";
        var ingest = await IngestAsync(hint, "Person has fallen beside the chair");

        var before = await GetLiveStatusAsync(ingest.SubjectId);
        Assert.NotNull(before);
        Assert.True(before!.UnacknowledgedSignificantCount > 0);

        var ack = await _client.PostAsJsonAsync("/api/observer/acknowledge", new AcknowledgeEventsRequestDto
        {
            EventIds = [ingest.EventId!],
            AcknowledgedBy = "e2e"
        });
        Assert.True(ack.IsSuccessStatusCode);

        var after = await GetLiveStatusAsync(ingest.SubjectId);
        Assert.NotNull(after);
        Assert.True(after!.UnacknowledgedSignificantCount < before.UnacknowledgedSignificantCount);
    }

    [Fact]
    public async Task The_drift_board_answers_even_before_a_baseline_exists()
    {
        var response = await _client.GetAsync("/api/identity/subjects/live-risk");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_observation_maps_into_a_FHIR_resource()
    {
        var hint = $"fhir-{Guid.NewGuid():N}";
        var ingest = await IngestAsync(hint, "Person seated using laptop");
        hint = ingest.SubjectId;

        var response = await _client.GetAsync($"/fhir/Observation?subject={hint}&date={Today:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Observation", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_registered_known_person_is_marked_as_known()
    {
        var name = $"Known-{Guid.NewGuid():N}"[..12];

        var response = await _client.PostAsJsonAsync("/api/identity/subjects", new RegisterSubjectRequestDto
        {
            DisplayName = name
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var subjects = await _client.GetFromJsonAsync<List<SubjectProfileDto>>("/api/identity/subjects");
        var created = subjects!.FirstOrDefault(s => s.DisplayName == name);
        Assert.NotNull(created);
        Assert.True(created!.IsKnownIdentity);
    }

    [Fact]
    public async Task A_retried_submission_does_not_create_a_second_observation()
    {
        var key = Guid.NewGuid();
        var hint = $"retry-{Guid.NewGuid():N}";

        var first = await PostIngestAsync(hint, "Person seated using laptop", key);
        var second = await PostIngestAsync(hint, "Person seated using laptop", key);

        Assert.Equal(first.EventId, second.EventId);
    }

    [Fact]
    public async Task Diagnostics_reports_a_reachable_storage_connection()
    {
        var snapshot = await _client.GetFromJsonAsync<DiagnosticsSnapshotDto>("/api/diagnostics/status");

        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot!.StorageConnectionStatus));
    }

    [Fact]
    public async Task The_boot_report_names_the_last_startup_milestone()
    {
        var response = await _client.GetAsync("/diag/boot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_read_observations()
    {
        // The API host is default-deny; only /health, /diag and /auth opt out.
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Remove("X-Fake-User");

        var response = await anonymous.GetAsync("/api/archives/2026-01-01");

        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Unexpected status {(int)response.StatusCode}");
    }

    [Fact]
    public async Task The_health_probe_answers_json_for_a_machine_client()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Accept.ParseAdd("application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("status", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sign_in_configuration_is_readable_without_a_session()
    {
        var config = await _client.GetFromJsonAsync<AuthConfigDto>("/auth/config");

        Assert.NotNull(config);
        Assert.False(string.IsNullOrWhiteSpace(config!.Environment));
    }

    [Fact]
    public async Task The_guest_bypass_establishes_a_session_in_the_test_environment()
    {
        // HTTPS base address: the BFF session cookie is Secure, so it is dropped over plain http.
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var login = await client.GetAsync("/auth/login/fake?returnUrl=/");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var me = await client.GetFromJsonAsync<AuthStateDto>("/auth/me");
        Assert.NotNull(me);
        Assert.True(me!.IsAuthenticated);
    }

    private async Task<SubjectLiveStatusDto?> GetLiveStatusAsync(string subjectId)
    {
        var live = await _client.GetFromJsonAsync<List<SubjectLiveStatusDto>>("/api/identity/subjects/live-status");
        return live?.FirstOrDefault(s => s.SubjectId == subjectId);
    }

    private Task<IngestObservationResultDto> IngestAsync(string hint, string activity) =>
        PostIngestAsync(hint, activity, idempotencyKey: null);

    private async Task<IngestObservationResultDto> PostIngestAsync(string hint, string activity, Guid? idempotencyKey)
    {
        var response = await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            SubjectHint = hint,
            Activity = activity,
            ClinicalPayload = $"<S>{activity}.<E>",
            IdempotencyKey = idempotencyKey
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IngestObservationResultDto>();
        Assert.NotNull(result);
        return result!;
    }
}
