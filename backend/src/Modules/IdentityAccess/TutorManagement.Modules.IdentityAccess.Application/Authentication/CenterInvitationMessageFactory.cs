using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication;

public static class CenterInvitationMessageFactory
{
    public static EmailOutboxMessage Create(
        CenterInvitation invitation,
        string centerName,
        string invitationToken,
        DateTimeOffset createdAtUtc) =>
        Create(invitation, centerName, invitationToken, accountExists: false, createdAtUtc);

    public static EmailOutboxMessage Create(
        CenterInvitation invitation,
        string centerName,
        string invitationToken,
        bool accountExists,
        DateTimeOffset createdAtUtc,
        string? invitationUrl = null)
    {
        var nl = Environment.NewLine;
        var role = invitation.Role switch
        {
            CenterMemberRole.CenterOwner => "chủ trung tâm",
            CenterMemberRole.CenterAdmin => "quản lý",
            CenterMemberRole.Teacher => "giáo viên",
            _ => "nhân viên"
        };
        var howTo = invitationUrl is not null
            ? (accountExists
                ? $"Bạn đã có tài khoản. Mở link sau, đăng nhập bằng email này rồi bấm Chấp nhận:{nl}{invitationUrl}"
                : $"Mở link sau để tạo tài khoản và tham gia:{nl}{invitationUrl}")
            : accountExists
                ? "Bạn đã có tài khoản: đăng nhập bằng email này, mở trang Nhận lời mời và nhập mã bên dưới."
                : "Mở trang Nhận lời mời của ứng dụng và nhập mã bên dưới để tạo tài khoản.";
        return EmailOutboxMessage.Create(
            invitation.Email,
            $"Lời mời tham gia {centerName} — {EmailBranding.AppName}",
            $"Xin chào,{nl}{nl}" +
            $"Bạn được mời làm {role} của {centerName} trên {EmailBranding.AppName}.{nl}{nl}" +
            $"{howTo}{nl}{nl}" +
            $"Lời mời có hiệu lực 48 giờ và chỉ dùng được một lần. Mã lời mời:{nl}{nl}{invitationToken}{nl}{nl}" +
            "Nếu bạn không biết về lời mời này, hãy bỏ qua email.",
            createdAtUtc);
    }
}
