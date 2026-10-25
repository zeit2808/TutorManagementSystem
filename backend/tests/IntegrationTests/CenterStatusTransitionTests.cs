using TutorManagement.Modules.Organizations.Domain.Centers;

namespace TutorManagement.IntegrationTests;

public sealed class CenterStatusTransitionTests
{
    private static readonly string[] ExpectedResubmitHistory =
        ["-:PendingApproval", "PendingApproval:Rejected", "Rejected:PendingApproval", "PendingApproval:Approved"];

    [Fact]
    public void Suspended_center_can_only_be_reinstated_by_review()
    {
        var center = NewCenter();
        var reviewerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        center.Approve(reviewerId, now);
        center.Suspend(reviewerId, "Compliance review", now.AddMinutes(1));

        Assert.Equal(CenterStatus.Suspended, center.Status);
        Assert.Equal("Compliance review", center.ReviewNote);
        Assert.Throws<InvalidOperationException>(() =>
            center.Approve(reviewerId, now.AddMinutes(2)));

        center.Reinstate(reviewerId, "Review completed", now.AddMinutes(3));
        Assert.Equal(CenterStatus.Approved, center.Status);
        Assert.Equal("Review completed", center.ReviewNote);
    }

    [Fact]
    public void Rejected_center_cannot_be_suspended_or_reinstated()
    {
        var center = NewCenter();
        var reviewerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        center.Reject(reviewerId, "Missing documents", now);

        Assert.Equal(CenterStatus.Rejected, center.Status);
        Assert.Throws<InvalidOperationException>(() =>
            center.Suspend(reviewerId, "Invalid", now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() =>
            center.Reinstate(reviewerId, "Invalid", now.AddMinutes(1)));
    }

    [Fact]
    public void Rejected_center_is_corrected_and_resubmitted_before_approval()
    {
        var center = NewCenter();
        var reviewerId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        center.Reject(reviewerId, "Wrong tax code", now);
        Assert.Throws<InvalidOperationException>(() => center.Approve(reviewerId, now.AddMinutes(1)));

        center.Resubmit(new CenterChangeProposal("Fixed LLC", "Fixed", "0312345678", null, "2 Street",
            "fixed@example.com", "0911111111"), ownerId, "  Tax code corrected  ", now.AddMinutes(2));
        Assert.Equal(CenterStatus.PendingApproval, center.Status);
        Assert.Equal("0312345678", center.TaxCode);
        Assert.Null(center.ReviewNote);

        center.Approve(reviewerId, now.AddMinutes(3));
        Assert.Equal(ExpectedResubmitHistory,
            center.StatusHistory.Select(x => $"{x.FromStatus?.ToString() ?? "-"}:{x.ToStatus}").ToArray());
        Assert.Equal("Tax code corrected", center.StatusHistory.ElementAt(2).Note);
    }

    [Fact]
    public void Directory_listing_needs_approval_and_suspension_unlists()
    {
        var center = NewCenter();
        var reviewerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<InvalidOperationException>(() => center.SetDirectoryListing(true, now));
        center.Approve(reviewerId, now);
        center.SetDirectoryListing(true, now.AddMinutes(1));
        Assert.True(center.ListedInDirectory);

        center.Suspend(reviewerId, "Complaint", now.AddMinutes(2));
        Assert.False(center.ListedInDirectory);
    }

    private static Center NewCenter() => Center.Create(
        "Example Center LLC",
        "Example Center",
        "TEST-TAX-CODE",
        null,
        "1 Test Street",
        "center@example.com",
        "0900000000",
        DateTimeOffset.UtcNow);
}
