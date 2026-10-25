namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public sealed record IssuedOneTimeToken(string Value, string Hash);

public interface IOneTimeTokenService
{
    IssuedOneTimeToken Create();

    string Hash(string token);
}
