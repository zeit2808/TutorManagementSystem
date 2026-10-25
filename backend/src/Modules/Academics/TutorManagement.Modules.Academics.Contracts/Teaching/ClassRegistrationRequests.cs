namespace TutorManagement.Modules.Academics.Contracts.Teaching;

public sealed record SubmitClassApplicationsRequest(string FullName, string ParentName, string ParentPhone,
    IReadOnlyList<Guid> ClassIds);
public sealed record SetClassRegistrationRequest(bool Open, int? ExpectedVersion);
public sealed record RejectClassApplicationRequest(string Reason);
