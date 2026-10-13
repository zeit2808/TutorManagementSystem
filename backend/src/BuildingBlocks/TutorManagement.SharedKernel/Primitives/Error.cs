namespace TutorManagement.SharedKernel.Primitives;

public sealed record Error(string Code, string Message, string? Field = null)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}
