using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Application.Models;
using PoWatch.Domain.Models;

namespace PoWatch.Application.Services;

public sealed class IdentityService(
    ISubjectRepository subjectRepository,
    IObservationRepository observationRepository,
    ILogger<IdentityService> logger)
{
    public Task<IReadOnlyList<SubjectProfile>> GetSubjectsAsync(CancellationToken cancellationToken) =>
        subjectRepository.GetAllAsync(cancellationToken);

    public async Task<IdentityRevisionResult> RenameAsync(string subjectId, RenameSubjectRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            throw new InvalidOperationException("SubjectId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.NewName))
        {
            throw new InvalidOperationException("NewName is required.");
        }

        logger.LogInformation(
            "Inline rename requested. SubjectId={SubjectId}, NewName={NewName}",
            subjectId,
            request.NewName);

        var canonical = await subjectRepository.RenameAsync(subjectId, request.NewName, cancellationToken);
        var rewritten = await observationRepository.MergeSubjectAsync(subjectId, canonical, cancellationToken);
        var removed = string.Equals(subjectId, canonical.SubjectId, StringComparison.OrdinalIgnoreCase) ? 0 : 1;

        logger.LogInformation(
            "Inline rename completed. CanonicalSubjectId={CanonicalSubjectId}, CanonicalName={CanonicalName}, EventsRewritten={EventsRewritten}, SubjectsRemoved={SubjectsRemoved}",
            canonical.SubjectId,
            canonical.DisplayName,
            rewritten,
            removed);

        return new IdentityRevisionResult
        {
            CanonicalSubjectId = canonical.SubjectId,
            CanonicalName = canonical.DisplayName,
            EventsRewritten = rewritten,
            SubjectsRemoved = removed
        };
    }

    public async Task<IdentityRevisionResult> MergeAsync(MergeIdentityRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PrimarySubjectId) || string.IsNullOrWhiteSpace(request.SecondarySubjectId))
        {
            throw new InvalidOperationException("PrimarySubjectId and SecondarySubjectId are required.");
        }

        if (string.Equals(request.PrimarySubjectId, request.SecondarySubjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Primary and secondary subjects must be different.");
        }

        logger.LogInformation(
            "Merge requested. PrimarySubjectId={PrimarySubjectId}, SecondarySubjectId={SecondarySubjectId}, NewDisplayName={NewDisplayName}",
            request.PrimarySubjectId,
            request.SecondarySubjectId,
            request.NewDisplayName);

        var merged = await subjectRepository.MergeAsync(
            request.PrimarySubjectId,
            request.SecondarySubjectId,
            request.NewDisplayName,
            cancellationToken);

        var rewritten = 0;

        if (!string.Equals(request.PrimarySubjectId, merged.SubjectId, StringComparison.OrdinalIgnoreCase))
        {
            rewritten += await observationRepository.MergeSubjectAsync(request.PrimarySubjectId, merged, cancellationToken);
        }

        rewritten += await observationRepository.MergeSubjectAsync(request.SecondarySubjectId, merged, cancellationToken);

        var removed = string.Equals(request.PrimarySubjectId, merged.SubjectId, StringComparison.OrdinalIgnoreCase) ? 1 : 2;

        logger.LogInformation(
            "Merge completed. CanonicalSubjectId={CanonicalSubjectId}, CanonicalName={CanonicalName}, EventsRewritten={EventsRewritten}, SubjectsRemoved={SubjectsRemoved}",
            merged.SubjectId,
            merged.DisplayName,
            rewritten,
            removed);

        return new IdentityRevisionResult
        {
            CanonicalSubjectId = merged.SubjectId,
            CanonicalName = merged.DisplayName,
            EventsRewritten = rewritten,
            SubjectsRemoved = removed
        };
    }
}
