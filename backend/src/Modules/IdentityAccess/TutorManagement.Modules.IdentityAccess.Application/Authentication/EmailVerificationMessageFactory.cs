using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication;

public static class EmailVerificationMessageFactory
{
    public static EmailOutboxMessage Create(
        User user,
        string verificationToken,
        Guid securityTokenId,
        DateTimeOffset createdAtUtc,
        string? verifyUrl = null)
    {
        var nl = Environment.NewLine;
        var howTo = verifyUrl is not null
            ? $"Mở link sau trong 24 giờ để xác minh:{nl}{verifyUrl}{nl}{nl}Nếu link không mở được, vào trang Xác minh email và nhập mã:"
            : "Vào trang Xác minh email của ứng dụng trong 24 giờ và nhập mã:";
        return EmailOutboxMessage.CreateForSecurityToken(
            user.Email,
            $"Xác minh email — {EmailBranding.AppName}",
            $"Chào {user.Username},{nl}{nl}{howTo}{nl}{nl}{verificationToken}{nl}{nl}" +
            "Chỉ mã mới nhất có hiệu lực. Nếu bạn không yêu cầu, hãy bỏ qua email này.",
            securityTokenId,
            createdAtUtc);
    }
}
