using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Subjects;

public static class SubjectErrors
{
    public static readonly Error NotFound = new("subject_not_found", "The subject was not found in this center.");
    public static readonly Error InvalidDetails = new("invalid_subject_details", "The subject details are invalid.");
    public static readonly Error CodeAlreadyExists = new("subject_code_already_exists",
        "A subject with this code already exists in this center.", "code");
    public static readonly Error NameAlreadyExists = new("subject_name_already_exists",
        "A subject with this name already exists in this center.", "name");
    public static readonly Error AlreadyArchived = new("subject_already_archived", "The subject is already archived.");
    public static readonly Error AlreadyActive = new("subject_already_active", "The subject is already active.");
    public static readonly Error ArchivedCannotEdit = new("subject_archived_cannot_edit",
        "Reactivate the subject before editing it.");
    public static readonly Error ConcurrentChange = new("subject_concurrent_change",
        "The subject was changed by another request. Reload and try again.");
    public static readonly Error HasActiveClasses = new("subject_has_active_classes",
        "Archive the subject's active classes before archiving the subject.");
}
