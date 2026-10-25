using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers;

public sealed class OrganizationPage<T>
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<T> Items { get; set; } = [];
}

public sealed record CenterStatusChangeDto(string? FromStatus, string ToStatus, Guid? ActorUserId, string? Note,
    DateTimeOffset ChangedAtUtc)
{
    public string? ActorName { get; set; }
}

/// <summary>
/// Wave 4 center lifecycle: resubmitting a rejected center (D8), full status history, directory
/// opt-in (D4), withdrawing a pending change request (D10) and the platform review queue.
/// </summary>
public sealed record CenterStatusCountsDto(int PendingApproval, int Approved, int Suspended, int Rejected, int Total);

public sealed class CenterLifecycleWorkflow(
    ICenterRepository centers,
    ICenterChangeRequestRepository changes,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<OrganizationPage<CenterDto>> ListAsync(CenterStatus? status, string? search, int page,
        int pageSize, CancellationToken token)
    {
        var (items, total) = await centers.ListPageAsync(status, search, page, pageSize, token);
        return new OrganizationPage<CenterDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total, Items = items.Select(CenterDto.From).ToList()
        };
    }

    public async Task<Result<CenterDto>> GetAsync(Guid centerId, CancellationToken token) =>
        await centers.FindAsync(centerId, token) is { } center
            ? Result<CenterDto>.Success(CenterDto.From(center))
            : Result<CenterDto>.Failure(CenterErrors.NotFound);

    /// <summary>Counts for the platform status filter.</summary>
    public async Task<CenterStatusCountsDto> CountsAsync(CancellationToken token)
    {
        var counts = await centers.CountByStatusAsync(token);
        int Of(CenterStatus status) => counts.TryGetValue(status, out var value) ? value : 0;
        return new CenterStatusCountsDto(Of(CenterStatus.PendingApproval), Of(CenterStatus.Approved),
            Of(CenterStatus.Suspended), Of(CenterStatus.Rejected), counts.Values.Sum());
    }

    public async Task<Result<CenterDto>> ResubmitAsync(Guid centerId, Guid actorId, CenterChangeProposal details,
        string? note, CancellationToken token)
    {
        var center = await centers.FindAsync(centerId, token);
        if (center is null) return Result<CenterDto>.Failure(CenterErrors.NotFound);
        if (center.Status != CenterStatus.Rejected) return Result<CenterDto>.Failure(CenterErrors.ResubmitRequiresRejection);
        if (await centers.TaxCodeExistsForOtherAsync(details.TaxCode.Trim(), centerId, token))
            return Result<CenterDto>.Failure(CenterErrors.TaxCodeAlreadyExists);
        center.Resubmit(details, actorId, note, clock.UtcNow);
        try { await unitOfWork.SaveChangesAsync(token); }
        catch (OrganizationDuplicateException) { return Result<CenterDto>.Failure(CenterErrors.TaxCodeAlreadyExists); }
        return Result<CenterDto>.Success(CenterDto.From(center));
    }

    public async Task<Result<IReadOnlyList<CenterStatusChangeDto>>> HistoryAsync(Guid centerId, CancellationToken token)
    {
        if (await centers.FindAsync(centerId, token) is null)
            return Result<IReadOnlyList<CenterStatusChangeDto>>.Failure(CenterErrors.NotFound);
        var rows = await centers.ListHistoryAsync(centerId, token);
        return Result<IReadOnlyList<CenterStatusChangeDto>>.Success(rows.Select(x => new CenterStatusChangeDto(
            x.FromStatus?.ToString(), x.ToStatus.ToString(), x.ActorUserId, x.Note, x.ChangedAtUtc)).ToArray());
    }

    public async Task<Result<CenterDto>> SetDirectoryListingAsync(Guid centerId, bool listed, CancellationToken token)
    {
        var center = await centers.FindAsync(centerId, token);
        if (center is null) return Result<CenterDto>.Failure(CenterErrors.NotFound);
        if (listed && center.Status != CenterStatus.Approved)
            return Result<CenterDto>.Failure(CenterErrors.DirectoryRequiresApproval);
        center.SetDirectoryListing(listed, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(token);
        return Result<CenterDto>.Success(CenterDto.From(center));
    }

    public async Task<Result<CenterChangeRequestDto>> WithdrawChangeAsync(Guid centerId, Guid requestId, Guid ownerId,
        CancellationToken token)
    {
        var request = await changes.FindAsync(centerId, requestId, token);
        if (request is null) return Result<CenterChangeRequestDto>.Failure(CenterErrors.ChangeNotFound);
        if (request.Status != CenterChangeStatus.Pending) return Result<CenterChangeRequestDto>.Failure(CenterErrors.ChangeNotPending);
        request.Withdraw(ownerId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(token);
        return Result<CenterChangeRequestDto>.Success(CenterChangeRequestDto.From(request));
    }

    public async Task<OrganizationPage<CenterChangeRequestDto>> ChangeQueueAsync(CenterChangeStatus? status, int page,
        int pageSize, CancellationToken token)
    {
        var (items, total) = await changes.ListAllAsync(status, page, pageSize, token);
        return new OrganizationPage<CenterChangeRequestDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = items.Select(x =>
            {
                var dto = CenterChangeRequestDto.From(x.Request);
                dto.CenterName = x.CenterName;
                return dto;
            }).ToList()
        };
    }
}
