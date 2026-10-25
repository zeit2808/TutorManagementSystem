namespace TutorManagement.Modules.IdentityAccess.Application.Authentication;

public sealed class IdentityDuplicateException(
    string constraintName,
    Exception innerException)
    : Exception("An identity record violates a unique constraint.", innerException)
{
    public string ConstraintName { get; } = constraintName;
}
