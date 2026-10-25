namespace TutorManagement.Modules.Academics.Application.Subjects;

public sealed class SubjectDuplicateException(string constraintName, Exception innerException)
    : Exception("A subject with the same code or name already exists.", innerException)
{
    public string ConstraintName { get; } = constraintName;
}

public sealed class SubjectConcurrencyException(Exception innerException)
    : Exception("The subject was changed concurrently.", innerException);

internal static class SubjectWriteErrors
{
    public static TutorManagement.SharedKernel.Primitives.Error FromDuplicate(SubjectDuplicateException exception) =>
        exception.ConstraintName switch
        {
            "ux_subjects_center_code" => SubjectErrors.CodeAlreadyExists,
            "ux_subjects_center_name" => SubjectErrors.NameAlreadyExists,
            _ => throw exception
        };
}
