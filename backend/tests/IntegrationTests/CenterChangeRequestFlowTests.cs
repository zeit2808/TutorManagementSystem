using System.Xml.Serialization;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Application.Centers.RequestCenterChange;
using TutorManagement.Modules.Organizations.Application.Centers.ReviewCenterChange;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.IntegrationTests;

public sealed class CenterChangeRequestFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 2, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminId = Guid.Parse("70000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Change_request_response_with_proposals_is_xml_serializable()
    {
        var state = new State();
        var submitted = await state.RequestHandler.HandleAsync(NewCommand(state.Center.Id), CancellationToken.None);
        var response = ApiResponse<CenterChangeRequestDto>.Ok(submitted.Value, "test-trace");
        var serializer = new XmlSerializer(typeof(ApiResponse<CenterChangeRequestDto>));
        using var writer = new StringWriter();
        serializer.Serialize(writer, response);
        Assert.Contains("new@example.com", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submitting_does_not_change_any_center_field_until_admin_approval()
    {
        var state = new State();
        var submitted = await state.RequestHandler.HandleAsync(
            NewCommand(state.Center.Id), CancellationToken.None);

        Assert.True(submitted.IsSuccess);
        Assert.Equal("Pending", submitted.Value!.Status);
        Assert.Equal("Original Center LLC", state.Center.LegalName);
        Assert.Equal("Original Center", state.Center.DisplayName);
        Assert.Equal("old@example.com", state.Center.ContactEmail);
        Assert.Equal("0912345678", state.Center.ContactPhone);
        Assert.Equal("0312345678", state.Center.TaxCode);
        Assert.Equal("new@example.com", submitted.Value.Proposed.ContactEmail);
        Assert.Equal("old@example.com", submitted.Value.Original.ContactEmail);

        var pendingRequest = state.Requests.Single();
        Assert.Throws<InvalidOperationException>(() =>
            state.Center.ApplyApprovedChange(pendingRequest, Now));

        var duplicate = await state.RequestHandler.HandleAsync(
            NewCommand(state.Center.Id), CancellationToken.None);
        Assert.Equal(CenterErrors.ChangeAlreadyPending, duplicate.Error);
    }

    [Fact]
    public async Task Approval_applies_exact_proposal_and_records_reviewer()
    {
        var state = new State();
        var submitted = await state.RequestHandler.HandleAsync(
            NewCommand(state.Center.Id), CancellationToken.None);

        var approved = await state.ReviewHandler.HandleAsync(
            new ReviewCenterChangeCommand(state.Center.Id, submitted.Value!.Id, AdminId, true,
                "Official record checked"), CancellationToken.None);

        Assert.True(approved.IsSuccess);
        Assert.Equal("Approved", approved.Value!.Status);
        Assert.Equal(AdminId, approved.Value.ReviewedByUserId);
        Assert.Equal("Updated Center LLC", state.Center.LegalName);
        Assert.Equal("Updated Center", state.Center.DisplayName);
        Assert.Equal("0312345679", state.Center.TaxCode);
        Assert.Equal("2 New Street", state.Center.Address);
        Assert.Equal("new@example.com", state.Center.ContactEmail);
        Assert.Equal("0987654321", state.Center.ContactPhone);
        Assert.Equal(2, state.SaveCount);

        var replay = await state.ReviewHandler.HandleAsync(
            new ReviewCenterChangeCommand(state.Center.Id, submitted.Value.Id, AdminId, true,
                "Again"), CancellationToken.None);
        Assert.Equal(CenterErrors.ChangeAlreadyReviewed, replay.Error);
    }

    [Fact]
    public async Task Rejection_keeps_center_unchanged_and_records_reason()
    {
        var state = new State();
        var submitted = await state.RequestHandler.HandleAsync(
            NewCommand(state.Center.Id), CancellationToken.None);
        var rejected = await state.ReviewHandler.HandleAsync(
            new ReviewCenterChangeCommand(state.Center.Id, submitted.Value!.Id, AdminId, false,
                "Official record not updated"), CancellationToken.None);

        Assert.True(rejected.IsSuccess);
        Assert.Equal("Rejected", rejected.Value!.Status);
        Assert.Equal("Official record not updated", rejected.Value.ReviewNote);
        Assert.Equal("Original Center LLC", state.Center.LegalName);
        Assert.Equal("old@example.com", state.Center.ContactEmail);
    }

    [Fact]
    public async Task Stale_or_identical_request_cannot_be_approved()
    {
        var state = new State();
        var noChange = await state.RequestHandler.HandleAsync(
            NewCommand(state.Center.Id) with
            {
                Proposal = new CenterChangeProposal(
                    state.Center.LegalName, state.Center.DisplayName, state.Center.TaxCode,
                    state.Center.BusinessRegistrationNumber, state.Center.Address,
                    state.Center.ContactEmail, state.Center.ContactPhone)
            }, CancellationToken.None);
        Assert.Equal(CenterErrors.ChangeHasNoDifferences, noChange.Error);

        var submitted = await state.RequestHandler.HandleAsync(
            NewCommand(state.Center.Id), CancellationToken.None);
        var external = CenterChangeRequest.Create(state.Center, OwnerId,
            new CenterChangeProposal("Corrected name", state.Center.DisplayName,
                state.Center.TaxCode, null, state.Center.Address,
                state.Center.ContactEmail, state.Center.ContactPhone),
            "External correction", "Official reference", Now);
        external.Approve(AdminId, "External approval", Now);
        state.Center.ApplyApprovedChange(external, Now);

        var stale = await state.ReviewHandler.HandleAsync(
            new ReviewCenterChangeCommand(state.Center.Id, submitted.Value!.Id, AdminId, true,
                "Approved"), CancellationToken.None);
        Assert.Equal(CenterErrors.CenterDetailsChanged, stale.Error);
        Assert.Equal("Corrected name", state.Center.LegalName);
    }

    [Fact]
    public async Task Owner_can_withdraw_only_a_pending_request_and_queue_lists_it_with_center_name()
    {
        var state = new State();
        var lifecycle = new CenterLifecycleWorkflow(state, state, state, state.Clock);
        var submitted = await state.RequestHandler.HandleAsync(NewCommand(state.Center.Id), CancellationToken.None);

        var queue = await lifecycle.ChangeQueueAsync(CenterChangeStatus.Pending, 1, 25, CancellationToken.None);
        Assert.Equal("Original Center", Assert.Single(queue.Items).CenterName);

        var withdrawn = await lifecycle.WithdrawChangeAsync(state.Center.Id, submitted.Value!.Id, OwnerId,
            CancellationToken.None);
        Assert.True(withdrawn.IsSuccess);
        Assert.Equal("Withdrawn", withdrawn.Value.Status);
        Assert.Equal("Original Center LLC", state.Center.LegalName);

        var again = await lifecycle.WithdrawChangeAsync(state.Center.Id, submitted.Value.Id, OwnerId,
            CancellationToken.None);
        Assert.Equal(CenterErrors.ChangeNotPending, again.Error);

        // A withdrawn request no longer blocks a new one.
        var next = await state.RequestHandler.HandleAsync(NewCommand(state.Center.Id), CancellationToken.None);
        Assert.True(next.IsSuccess);
    }

    private static RequestCenterChangeCommand NewCommand(Guid centerId) => new(
        centerId, OwnerId,
        new CenterChangeProposal("Updated Center LLC", "Updated Center", "0312345679",
            "REG-2", "2 New Street", "new@example.com", "0987654321"),
        "Official registration updated", "Government record number 123");

    private sealed class State : ICenterRepository, ICenterChangeRequestRepository,
        IOrganizationUnitOfWork
    {
        public Center Center { get; } = CreateApprovedCenter();
        public Clock Clock { get; } = new();
        public List<CenterChangeRequest> Requests { get; } = [];
        public int SaveCount { get; private set; }
        public RequestCenterChangeCommandHandler RequestHandler => new(this, this, this, Clock);
        public ReviewCenterChangeCommandHandler ReviewHandler => new(this, this, this, Clock);

        public Task<Center?> FindAsync(Guid centerId, CancellationToken cancellationToken) =>
            Task.FromResult(centerId == Center.Id ? Center : null);
        public Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task AddAsync(Center center, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyCollection<Center>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Center>>([Center]);
        public Task<bool> TaxCodeExistsForOtherAsync(string taxCode, Guid exceptCenterId,
            CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<(IReadOnlyList<Center> Items, int TotalCount)> ListPageAsync(CenterStatus? status,
            string? search, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<(IReadOnlyList<Center>, int)>(([Center], 1));
        public Task<IReadOnlyDictionary<CenterStatus, int>> CountByStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<CenterStatus, int>>(new Dictionary<CenterStatus, int>());
        public Task<IReadOnlyList<CenterStatusChange>> ListHistoryAsync(Guid centerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CenterStatusChange>>(Center.StatusHistory.ToArray());
        public Task<(IReadOnlyList<(CenterChangeRequest Request, string CenterName)> Items, int TotalCount)>
            ListAllAsync(CenterChangeStatus? status, int page, int pageSize, CancellationToken cancellationToken)
        {
            var rows = Requests.Where(x => status is null || x.Status == status)
                .Select(x => (x, Center.DisplayName)).ToArray();
            return Task.FromResult<(IReadOnlyList<(CenterChangeRequest, string)>, int)>((rows, rows.Length));
        }
        public Task<bool> HasPendingAsync(Guid centerId, CancellationToken cancellationToken) =>
            Task.FromResult(Requests.Any(request =>
                request.CenterId == centerId && request.Status == CenterChangeStatus.Pending));
        public Task<CenterChangeRequest?> FindAsync(
            Guid centerId, Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Requests.SingleOrDefault(request =>
                request.CenterId == centerId && request.Id == requestId));
        public Task<IReadOnlyCollection<CenterChangeRequest>> ListAsync(
            Guid centerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CenterChangeRequest>>(
                Requests.Where(request => request.CenterId == centerId).ToArray());
        public Task AddAsync(CenterChangeRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private static Center CreateApprovedCenter()
    {
        var center = Center.Create(
            "Original Center LLC", "Original Center", "0312345678", null,
            "1 Original Street", "old@example.com", "0912345678", Now.AddDays(-1));
        center.Approve(AdminId, Now.AddHours(-1));
        return center;
    }
}
