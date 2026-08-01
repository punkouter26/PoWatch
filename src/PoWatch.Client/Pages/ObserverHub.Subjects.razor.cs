using Microsoft.AspNetCore.Components;
using PoWatch.Shared.Models;
using Radzen;

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

    private bool _clearingData;

    /// <summary>
    /// Clears ALL stored data (observations, subjects, blobs) via the same guarded endpoint the
    /// System page uses — there is no per-day delete. Confirmed first, then the Room Activity
    /// timeline and People-today strip are reloaded so the panel empties without a page refresh.
    /// </summary>
    private async Task ClearDataAsync()
    {
        var confirmed = await DialogService.Confirm(
            "This permanently deletes ALL recorded activity, every named person, and all saved images. It cannot be undone.",
            "Clear all data?",
            new ConfirmOptions { OkButtonText = "Clear everything", CancelButtonText = "Cancel", CloseDialogOnOverlayClick = true });

        if (confirmed != true)
            return;

        _clearingData = true;
        StateHasChanged();
        try
        {
            var result = await ApiClient.ClearAllDataAsync();
            if (result?.Success == true)
            {
                await RefreshTimelineAsync();
                await LoadSubjectsAsync();
                NotificationService.Notify(NotificationSeverity.Success, "Data cleared",
                    $"{result.ObservationsDeleted} events, {result.SubjectsDeleted} people, {result.BlobsDeleted} images deleted.",
                    duration: 5000);
            }
            else
            {
                NotificationService.Notify(NotificationSeverity.Error, "Clear failed",
                    result?.ErrorMessage ?? "The data-reset endpoint is unavailable in this environment.", duration: 6000);
            }
        }
        catch (Exception ex)
        {
            NotificationService.Notify(NotificationSeverity.Error, "Clear failed", ex.Message, duration: 6000);
        }
        finally
        {
            _clearingData = false;
            StateHasChanged();
        }
    }
}
