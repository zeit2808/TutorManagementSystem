namespace TutorManagement.IntegrationTests;

/// <summary>
/// Tests that bill tuition in the shared local database run one at a time: the reminder run scans every center,
/// so another test adding overdue charges at the same moment would change its counts.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TuitionDatabase
{
    public const string Name = "Tuition database";
}
