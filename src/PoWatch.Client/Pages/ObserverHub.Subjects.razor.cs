using Microsoft.AspNetCore.Components;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Pages;

public partial class ObserverHub
{
    private List<SubjectLiveStatusDto> _liveSubjects = [];
    private bool _subjectsLoading;

    /// <summary>
    /// The Live Room strip shows only people seen in the last 24 hours (consolidation #9) —
    /// the full, filterable library lives on the People page.
    /// </summary>
    private IReadOnlyList<SubjectLiveStatusDto> RecentLiveSubjects =>
        _liveSubjects.Where(s => Services.DisplayText.IsRecent(s.LastSeenUtc)).ToList();

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
