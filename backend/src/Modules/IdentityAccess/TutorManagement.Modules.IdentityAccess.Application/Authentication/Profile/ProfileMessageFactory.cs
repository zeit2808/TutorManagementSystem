using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Profile;

public static class ProfileMessageFactory
{
    public static EmailOutboxMessage CreateEmailChangeOtp(string newEmail, Guid tokenId, string otp, DateTimeOffset nowUtc) =>
        EmailOutboxMessage.CreateForSecurityToken(
            newEmail,
            $"Mã xác nhận email mới — {EmailBranding.AppName}",
            $"Mã xác nhận đổi email đăng nhập của bạn là {otp}. Mã có hiệu lực 10 phút và chỉ mã mới nhất có hiệu lực. Nếu bạn không yêu cầu, hãy bỏ qua email này.",
            tokenId,
            nowUtc);

    public static EmailOutboxMessage CreateEmailChanged(string previousEmail, string newEmail, DateTimeOffset nowUtc) =>
        EmailOutboxMessage.Create(
            previousEmail,
            $"Email đăng nhập đã thay đổi — {EmailBranding.AppName}",
            $"Email đăng nhập của tài khoản vừa được đổi sang {Mask(newEmail)}. Từ nay thư hệ thống sẽ gửi tới địa chỉ mới. Nếu không phải bạn, hãy liên hệ quản trị viên ngay.",
            nowUtc);

    /// <summary>"an***@gmail.com": enough to recognise the address without disclosing it to whoever reads the old inbox.</summary>
    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var local = email[..at];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}***{email[at..]}";
    }
}
