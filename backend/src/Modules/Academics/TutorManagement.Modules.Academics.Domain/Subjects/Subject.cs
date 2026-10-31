using TutorManagement.SharedKernel.Domain;
using System.Text;

namespace TutorManagement.Modules.Academics.Domain.Subjects;

public sealed class Subject : AggregateRoot<Guid>
{
    private Subject() : base(Guid.Empty) { }

    private Subject(Guid centerId, string code, string name, string? description,
        Guid actorId, DateTimeOffset nowUtc) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        SetDetails(code, name, description);
        CreatedByUserId = actorId;
        UpdatedByUserId = actorId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid CenterId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public Guid? ArchivedByUserId { get; private set; }
    public int Version { get; private set; }
    public bool IsActive => ArchivedAtUtc is null;

    public static Subject Create(Guid centerId, string code, string name, string? description,
        Guid actorId, DateTimeOffset nowUtc)
    {
        if (centerId == Guid.Empty || actorId == Guid.Empty)
            throw new ArgumentException("Center and actor are required.");
        return new Subject(centerId, code, name, description, actorId, nowUtc);
    }

    /// <summary>Renames or re-describes the subject; its generated code (D9) never changes.</summary>
    public bool Update(string name, string? description, Guid actorId, DateTimeOffset nowUtc)
    {
        if (!IsActive) throw new InvalidOperationException("An archived subject cannot be edited.");
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        ValidateDetails(Code, name, description);
        var normalizedName = NormalizeName(name);
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (Name == normalizedName && Description == trimmedDescription)
            return false;

        SetDetails(Code, name, description);
        UpdatedByUserId = actorId;
        UpdatedAtUtc = nowUtc;
        Version++;
        return true;
    }

    public bool Archive(Guid actorId, DateTimeOffset nowUtc)
    {
        if (!IsActive) return false;
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        ArchivedAtUtc = nowUtc;
        ArchivedByUserId = actorId;
        UpdatedByUserId = actorId;
        UpdatedAtUtc = nowUtc;
        Version++;
        return true;
    }

    public bool Reactivate(Guid actorId, DateTimeOffset nowUtc)
    {
        if (IsActive) return false;
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        ArchivedAtUtc = null;
        ArchivedByUserId = null;
        UpdatedByUserId = actorId;
        UpdatedAtUtc = nowUtc;
        Version++;
        return true;
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static string NormalizeName(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Normalize(NormalizationForm.FormC);

    public static bool IsValidCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var normalized = NormalizeCode(code);
        return normalized.Length is >= 2 and <= 20 &&
               normalized.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && NormalizeName(name).Length is >= 2 and <= 100 &&
        !name.Any(char.IsControl);

    public static bool IsValidDescription(string? description) =>
        description is null || description.Trim().Length <= 500;

    public static bool AreDetailsValid(string? code, string? name, string? description) =>
        IsValidCode(code) && IsValidName(name) && IsValidDescription(description);

    private void SetDetails(string code, string name, string? description)
    {
        ValidateDetails(code, name, description);
        Code = NormalizeCode(code);
        NormalizedCode = Code;
        Name = NormalizeName(name);
        NormalizedName = Name.ToUpperInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    private static void ValidateDetails(string code, string name, string? description)
    {
        if (!AreDetailsValid(code, name, description))
            throw new ArgumentException("Subject details are invalid.");
    }
}
