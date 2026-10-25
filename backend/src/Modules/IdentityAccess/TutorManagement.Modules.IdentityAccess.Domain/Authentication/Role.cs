using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public sealed class Role : Entity<Guid>
{
    private Role()
        : base(Guid.Empty)
    {
    }

    public Role(Guid id, string name, string normalizedName)
        : base(id)
    {
        Name = name;
        NormalizedName = normalizedName;
    }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;
}
