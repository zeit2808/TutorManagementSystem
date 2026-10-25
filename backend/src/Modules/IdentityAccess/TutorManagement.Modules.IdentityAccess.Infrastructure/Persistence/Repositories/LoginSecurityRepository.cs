using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class LoginSecurityRepository(IdentityDbContext dbContext) : ILoginSecurityRepository
{
    public async Task<LoginSecurityState> AcquireAsync(Guid userId, CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A login transaction is required.");

        // Lock the existing user row, including the first login before a state row exists.
        // This prevents concurrent attempts from independently creating or updating state.
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT id FROM iam.users WHERE id = @userId FOR UPDATE";
        command.Parameters.Add(new NpgsqlParameter<Guid>("userId", userId));
        var locked = await command.ExecuteScalarAsync(cancellationToken);
        if (locked is null) throw new InvalidOperationException("The login user no longer exists.");

        var state = await dbContext.LoginSecurityStates.SingleOrDefaultAsync(
            item => item.UserId == userId, cancellationToken);
        if (state is not null) return state;

        state = LoginSecurityState.Create(userId);
        await dbContext.LoginSecurityStates.AddAsync(state, cancellationToken);
        return state;
    }
}
