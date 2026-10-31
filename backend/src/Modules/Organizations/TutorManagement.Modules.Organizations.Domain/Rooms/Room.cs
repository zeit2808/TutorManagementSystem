using System.Text;
using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Organizations.Domain.Rooms;

public sealed class Room : AggregateRoot<Guid>
{
    private Room() : base(Guid.Empty) { }

    private Room(Guid centerId, string code, string name, string? locationNote,
        Guid actorId, DateTimeOffset nowUtc) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        SetDetails(code, name, locationNote);
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
    public string? LocationNote { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public Guid? ArchivedByUserId { get; private set; }
    public int Version { get; private set; }
    public bool IsActive => ArchivedAtUtc is null;

    public static Room Create(Guid centerId, string code, string name, string? locationNote,
        Guid actorId, DateTimeOffset nowUtc)
    {
        if (centerId == Guid.Empty || actorId == Guid.Empty) throw new ArgumentException("Center and actor are required.");
        return new Room(centerId, code, name, locationNote, actorId, nowUtc);
    }

    /// <summary>Renames the room or changes its location note; its generated code (D9) never changes.</summary>
    public bool Update(string name, string? locationNote, Guid actorId, DateTimeOffset nowUtc)
    {
        if (!IsActive) throw new InvalidOperationException("An archived room cannot be edited.");
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        ValidateDetails(Code, name, locationNote);
        var normalizedName = NormalizeName(name);
        var trimmedLocation = string.IsNullOrWhiteSpace(locationNote) ? null : locationNote.Trim();
        if (Name == normalizedName && LocationNote == trimmedLocation)
            return false;

        SetDetails(Code, name, locationNote);
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

    public static bool IsValidLocationNote(string? note) =>
        note is null || (note.Trim().Length <= 200 && !note.Any(char.IsControl));

    public static bool AreDetailsValid(string? code, string? name, string? locationNote) =>
        IsValidCode(code) && IsValidName(name) && IsValidLocationNote(locationNote);

    private void SetDetails(string code, string name, string? locationNote)
    {
        ValidateDetails(code, name, locationNote);
        Code = NormalizeCode(code);
        NormalizedCode = Code;
        Name = NormalizeName(name);
        NormalizedName = Name.ToUpperInvariant();
        LocationNote = string.IsNullOrWhiteSpace(locationNote) ? null : locationNote.Trim();
    }

    private static void ValidateDetails(string code, string name, string? locationNote)
    {
        if (!AreDetailsValid(code, name, locationNote))
            throw new ArgumentException("Room details are invalid.");
    }
}
