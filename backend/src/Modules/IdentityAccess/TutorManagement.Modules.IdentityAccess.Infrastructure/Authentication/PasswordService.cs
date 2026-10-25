using Microsoft.AspNetCore.Identity;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Authentication;

public sealed class PasswordService : IPasswordService
{
    private static readonly object PasswordSubject = new();

    private readonly PasswordHasher<object> _passwordHasher = new();
    private readonly string _dummyPasswordHash;

    public PasswordService()
    {
        _dummyPasswordHash = _passwordHasher.HashPassword(
            PasswordSubject,
            "Dummy-password-used-only-for-timing-protection");
    }

    public string Hash(string password) => _passwordHasher.HashPassword(PasswordSubject, password);

    public PasswordVerification Verify(string passwordHash, string providedPassword)
    {
        var result = _passwordHasher.VerifyHashedPassword(
            PasswordSubject,
            passwordHash,
            providedPassword);

        return new PasswordVerification(
            result != PasswordVerificationResult.Failed,
            result == PasswordVerificationResult.SuccessRehashNeeded);
    }

    public void PerformDummyVerification(string providedPassword) =>
        _ = _passwordHasher.VerifyHashedPassword(
            PasswordSubject,
            _dummyPasswordHash,
            providedPassword);
}
