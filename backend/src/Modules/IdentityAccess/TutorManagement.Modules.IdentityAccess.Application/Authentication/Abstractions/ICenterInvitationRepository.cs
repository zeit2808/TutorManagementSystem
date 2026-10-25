using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface ICenterInvitationRepository
{
    Task<CenterInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task<bool> HasActiveInvitationAsync(
        Guid centerId,
        string normalizedEmail,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
    Task AddAsync(CenterInvitation invitation, CancellationToken cancellationToken);
}
