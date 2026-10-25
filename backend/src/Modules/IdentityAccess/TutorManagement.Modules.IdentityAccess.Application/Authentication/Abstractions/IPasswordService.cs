namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public sealed record PasswordVerification(bool Succeeded, bool NeedsRehash);

public interface IPasswordService
{
    string Hash(string password);

    PasswordVerification Verify(string passwordHash, string providedPassword);

    void PerformDummyVerification(string providedPassword);
}
