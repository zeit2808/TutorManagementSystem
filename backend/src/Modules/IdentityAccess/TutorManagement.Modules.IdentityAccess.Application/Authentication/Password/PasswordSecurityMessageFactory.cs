using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;

public static class PasswordSecurityMessageFactory
{
    public static EmailOutboxMessage CreateResetOtp(User user, Guid tokenId, string otp, DateTimeOffset nowUtc) =>
        EmailOutboxMessage.CreateForSecurityToken(
            user.Email,
            $"Mã đặt lại mật khẩu — {EmailBranding.AppName}",
            $"Mã đặt lại mật khẩu của bạn là {otp}. Mã có hiệu lực 10 phút. Chỉ mã mới nhất có hiệu lực. Nếu bạn không yêu cầu, hãy bỏ qua email này.",
            tokenId,
            nowUtc);

    public static EmailOutboxMessage CreatePasswordChanged(User user, DateTimeOffset nowUtc) =>
        EmailOutboxMessage.Create(
            user.Email,
            $"Mật khẩu đã thay đổi — {EmailBranding.AppName}",
            "Mật khẩu tài khoản của bạn vừa được thay đổi. Nếu không phải bạn, hãy liên hệ quản trị viên ngay.",
            nowUtc);
}
