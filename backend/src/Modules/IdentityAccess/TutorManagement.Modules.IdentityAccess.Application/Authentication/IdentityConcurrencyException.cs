namespace TutorManagement.Modules.IdentityAccess.Application.Authentication;

public sealed class IdentityConcurrencyException(Exception innerException)
    : Exception("Identity data changed concurrently.", innerException);
