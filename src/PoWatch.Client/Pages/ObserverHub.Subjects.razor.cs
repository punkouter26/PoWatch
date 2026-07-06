using Microsoft.AspNetCore.Components;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Pages;

public partial class ObserverHub
{
    private List<SubjectLiveStatusDto> _liveSubjects = [];
    private bool _subjectsLoading;
    private string _subjectFilter = string.Empty;

    /// <summary>
    /// Drill-down from Live Dashboard: navigating to <c>/?subjectFilter=&lt;id&gt;</c> now actually filters the
    /// subjects strip. Previously the query param was supplied but never bound, so the link was a dead no-op.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "subjectFilter")]
    public string? SubjectFilterQuery { get; set; }

    private IReadOnlyList<SubjectLiveStatusDto> FilteredLiveSubjects =>
        string.IsNullOrWhiteSpace(_subjectFilter)
            ? _liveSubjects
            : _liveSubjects
                .Where(subject =>
                    subject.DisplayName.Contains(_subjectFilter, StringComparison.OrdinalIgnoreCase) ||
                    (subject.LastActivity ?? string.Empty).Contains(_subjectFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

    private void OnSubjectFilterInput(ChangeEventArgs e) =>
        _subjectFilter = e.Value?.ToString() ?? string.Empty;

    private static string GetSubjectCardClass(SubjectLiveStatusDto subject)
    {
        if (subject.LastActivityIsOutlier) return "subject-card--outlier";
        if (subject.UnacknowledgedSignificantCount > 0) return "subject-card--alert";
        return string.Empty;
    }

    // One-tap (audit #7): carry the subject id so Identity opens with this subject's rename ready,
    // instead of landing on the full list and forcing the user to hunt for it again.
    private void NavigateToIdentityPage() => Navigation.NavigateTo("/identity");

    private void NavigateToManageSubject(string subjectId) =>
        Navigation.NavigateTo($"/identity?focus={Uri.EscapeDataString(subjectId)}");

    private async Task LoadSubjectsAsync()
    {
        _subjectsLoading = true;
        try
        {
            _liveSubjects = [.. await ApiClient.GetLiveDashboardStatusAsync()];
        }
        catch
        {
            // Non-critical — subjects strip degrades gracefully.
        }
        finally
        {
            _subjectsLoading = false;
        }
    }
}
