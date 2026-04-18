using Microsoft.AspNetCore.Components;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Pages;

public partial class ObserverHub
{
    private List<SubjectLiveStatusDto> _liveSubjects = [];
    private bool _subjectsLoading;
    private string _subjectFilter = string.Empty;

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

    private void NavigateToIdentityPage() => Navigation.NavigateTo("/identity");

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