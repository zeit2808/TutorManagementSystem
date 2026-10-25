namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

public sealed record EmailOutboxBatchResult(
    int SelectedCount,
    int ProcessedCount,
    int FailedCount);
