using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Application.Members;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class CenterMemberStore(IdentityDbContext dbContext) : ICenterMemberStore
{
    public Task<CenterMembership?> FindMembershipAsync(Guid centerId, Guid userId, CancellationToken token) =>
        dbContext.CenterMemberships.SingleOrDefaultAsync(x => x.CenterId == centerId && x.UserId == userId, token);

    public Task<CenterMembership?> FindMembershipByIdAsync(Guid centerId, Guid membershipId, CancellationToken token) =>
        dbContext.CenterMemberships.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == membershipId, token);

    public Task<bool> IsActiveOwnerOfAnyCenterAsync(Guid userId, CancellationToken token) =>
        dbContext.CenterMemberships.AnyAsync(x => x.UserId == userId && x.IsActive &&
            x.Role == CenterMemberRole.CenterOwner, token);

    public async Task<IReadOnlyList<CenterMemberDto>> ListMembersAsync(Guid centerId, CancellationToken token)
    {
        var rows = await (from membership in dbContext.CenterMemberships.AsNoTracking()
                          join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
                          where membership.CenterId == centerId
                          select new
                          {
                              membership.Id, membership.UserId, user.Username, user.Email, membership.Role,
                              Active = membership.IsActive && user.IsActive, membership.CreatedAtUtc
                          }).ToListAsync(token);
        return rows
            .OrderBy(x => x.Active ? 0 : 1).ThenBy(x => x.Role).ThenBy(x => x.Username ?? x.Email, StringComparer.Ordinal)
            .Select(x => new CenterMemberDto(x.Id, x.UserId, x.Username, x.Email, x.Role.ToString(), x.Active, x.CreatedAtUtc))
            .ToArray();
    }

    public async Task<IReadOnlyList<MemberInvitationDto>> ListPendingInvitationsAsync(Guid centerId,
        DateTimeOffset nowUtc, CancellationToken token)
    {
        var rows = await dbContext.CenterInvitations.AsNoTracking()
            .Where(x => x.CenterId == centerId && x.AcceptedAtUtc == null && x.RevokedAtUtc == null &&
                x.ExpiresAtUtc > nowUtc)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id, x.Email, x.Role, x.CreatedAtUtc, x.ExpiresAtUtc,
                AccountExists = dbContext.Users.Any(user => user.NormalizedEmail == x.NormalizedEmail)
            })
            .ToListAsync(token);
        return rows.Select(x => new MemberInvitationDto(x.Id, x.Email, x.Role.ToString(), x.CreatedAtUtc,
            x.ExpiresAtUtc, x.AccountExists)).ToArray();
    }

    public async Task<IReadOnlyList<MemberInvitationDto>> ListOpenInvitationsAsync(Guid centerId,
        CancellationToken token)
    {
        var rows = await dbContext.CenterInvitations.AsNoTracking()
            .Where(x => x.CenterId == centerId && x.AcceptedAtUtc == null && x.RevokedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id, x.Email, x.Role, x.CreatedAtUtc, x.ExpiresAtUtc,
                AccountExists = dbContext.Users.Any(user => user.NormalizedEmail == x.NormalizedEmail)
            })
            .Take(200)
            .ToListAsync(token);
        return rows.Select(x => new MemberInvitationDto(x.Id, x.Email, x.Role.ToString(), x.CreatedAtUtc,
            x.ExpiresAtUtc, x.AccountExists)).ToArray();
    }

    public Task<CenterInvitation?> FindInvitationAsync(Guid centerId, Guid invitationId, CancellationToken token) =>
        dbContext.CenterInvitations.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == invitationId, token);
}
