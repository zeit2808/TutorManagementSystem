using System.ComponentModel.DataAnnotations;

namespace TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

public sealed class GoogleLoginRequest
{
    [Required]
    [StringLength(16384, MinimumLength = 20)]
    public string IdToken { get; set; } = string.Empty;
}

public sealed class LinkGoogleRequest
{
    [Required]
    [StringLength(16384, MinimumLength = 20)]
    public string IdToken { get; set; } = string.Empty;

    [Required]
    [StringLength(512, MinimumLength = 1)]
    public string CurrentPassword { get; set; } = string.Empty;
}
