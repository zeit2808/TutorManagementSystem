namespace TutorManagement.Modules.IdentityAccess.Application.Authentication;

public sealed class IdentityReferenceException(string constraintName, Exception innerException)
    : Exception("An identity reference no longer exists.", innerException)
{
    public string ConstraintName { get; } = constraintName;
}
