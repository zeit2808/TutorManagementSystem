using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Domain;

/// <summary>The automatic code families of the Academics module (D9); each prefix is unique across the system.</summary>
public static class AcademicsCodes
{
    public static readonly CodeKind Student = new("HS");
    public static readonly CodeKind Teacher = new("GV");
    public static readonly CodeKind Class = new("LH");
    public static readonly CodeKind Subject = new("MH");
}
