using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class CenterInvitationRepository(IdentityDbContext dbContext)
    : ICenterInvitationRepository
{
    public Task<CenterInvitation?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken) =>
        dbContext.CenterInvitations.SingleOrDefaultAsync(
            invitation => invitation.TokenHash == tokenHash,
            cancellationToken);

    public Task<bool> HasActiveInvitationAsync(
        Guid centerId,
        string normalizedEmail,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken) =>
        dbContext.CenterInvitations.AnyAsync(
            invitation =>
                invitation.CenterId == centerId &&
                invitation.NormalizedEmail == normalizedEmail &&
                invitation.AcceptedAtUtc == null &&
                invitation.RevokedAtUtc == null &&
                invitation.ExpiresAtUtc > nowUtc,
            cancellationToken);

    public async Task AddAsync(CenterInvitation invitation, CancellationToken cancellationToken) =>
        await dbContext.CenterInvitations.AddAsync(invitation, cancellationToken);
}
