namespace TutorManagement.Modules.Organizations.Application.Centers;

public sealed class OrganizationDuplicateException(
    string constraintName,
    Exception innerException)
    : Exception("An organization record violates a unique constraint.", innerException)
{
    public string ConstraintName { get; } = constraintName;
}
