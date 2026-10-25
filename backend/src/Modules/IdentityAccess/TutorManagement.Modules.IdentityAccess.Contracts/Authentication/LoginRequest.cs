using System.ComponentModel.DataAnnotations;

namespace TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

public sealed class LoginRequest
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}
