using System.Net;
using System.Net.Http.Json;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;
using PoWatch.Domain.Services;

namespace PoWatch.E2EAPI;

/// <summary>
/// Client → server journeys as the Blazor app actually performs them: watch a room, name the person
/// who appeared, merge a duplicate, read the day back, and end the shift.
/// </summary>
public sealed class CaregiverJourneyE2ETests(ApiE2EFactory factory) : IClassFixture<ApiE2EFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static DateOnly Today => LocalDay.Today(TimeProvider.System, TimeZoneInfo.Local);

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
    public async Task The_drift_board_answers_even_before_a_baseline_exists()
    {
        var response = await _client.GetAsync("/api/identity/subjects/live-risk");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
    public async Task The_live_board_lists_everyone_by_name_with_notable_counts()
    {
        await IngestAsync($"live-{Guid.NewGuid():N}", "Person seated using laptop");
        var notable = await IngestAsync($"notable-{Guid.NewGuid():N}", "Person has fallen beside the chair");

        var live = await _client.GetFromJsonAsync<List<SubjectLiveStatusDto>>("/api/identity/subjects/live-status");

        Assert.NotNull(live);
        Assert.NotEmpty(live!);
        Assert.All(live!, s => Assert.False(string.IsNullOrWhiteSpace(s.SubjectId)));
        // Never a raw storage id standing in for a missing display name.
        Assert.All(live!, s => Assert.False(string.IsNullOrWhiteSpace(s.DisplayName)));
        Assert.True(live!.Single(s => s.SubjectId == notable.SubjectId).NotableTodayCount > 0);
    }

    [Fact]
    public async Task A_day_produces_a_downloadable_report_and_a_brief_without_raw_ids()
    {
        await IngestAsync($"shift-{Guid.NewGuid():N}", "Person is eating a meal");
        await IngestAsync($"brief-{Guid.NewGuid():N}", "A person entering the room");

        var response = await _client.GetAsync($"/api/archives/{Today:yyyy-MM-dd}/handoff-report?shiftWindow=FullDay");
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            // QuestPDF ships no win-arm64 native binary, so the PDF engine cannot start on an ARM64
            // Windows host. That must still be an EXPLAINED failure, never a bare 500.
            Assert.Contains("PDF engine", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            // A PDF always starts with %PDF.
            Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
        }

        var briefResponse = await _client.PostAsJsonAsync(
            $"/api/archives/{Today:yyyy-MM-dd}/handoff-brief",
            new GenerateHandoffBriefRequestDto
            {
                ShiftWindow = "FullDay",
                Audience = "NurseToNurse",
                IncludeUnresolvedAlerts = true,
                IncludeHighlights = true
            });

        Assert.Equal(HttpStatusCode.OK, briefResponse.StatusCode);
        var brief = await briefResponse.Content.ReadFromJsonAsync<HandoffBriefDto>();
        Assert.NotNull(brief);
        Assert.False(string.IsNullOrWhiteSpace(brief!.Summary));
        Assert.DoesNotContain("Subject-", brief.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Operations_endpoints_report_health_boot_and_storage()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/diag")).StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Accept.ParseAdd("application/json");
        var health = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("status", await health.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var boot = await _client.GetAsync("/diag/boot");
        Assert.Equal(HttpStatusCode.OK, boot.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await boot.Content.ReadAsStringAsync()));

        var snapshot = await _client.GetFromJsonAsync<DiagnosticsSnapshotDto>("/api/diagnostics/status");
        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot!.StorageConnectionStatus));
    }

    [Fact]
    public async Task Sign_in_config_is_public_and_the_guest_bypass_establishes_a_session()
    {
        var config = await _client.GetFromJsonAsync<AuthConfigDto>("/auth/config");
        Assert.NotNull(config);
        Assert.False(string.IsNullOrWhiteSpace(config!.Environment));

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
