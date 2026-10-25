using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

namespace TutorManagement.IntegrationTests;

public sealed class InvitationEmailLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Invitation_email_links_to_the_web_app_when_frontend_base_url_is_configured()
    {
        var links = new FrontendLinks(Provider("https://app.example.com/"));
        var url = links.InvitationUrl("abc+/=");
        Assert.Equal("https://app.example.com/accept-invitation?token=abc%2B%2F%3D", url);

        var invitation = CenterInvitation.CreateMember(Guid.NewGuid(), "gv@example.com", "GV@EXAMPLE.COM",
            CenterMemberRole.Teacher, "hash", Now.AddDays(2), Now, Guid.NewGuid());
        var message = CenterInvitationMessageFactory.Create(invitation, "Ánh Dương", "abc+/=", true, Now, url);
        Assert.Contains(url!, message.Body, StringComparison.Ordinal);
        Assert.Contains("làm giáo viên của Ánh Dương", message.Body, StringComparison.Ordinal);
        Assert.Contains("đăng nhập bằng email này", message.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://files.example.com")]
    public void Without_a_valid_base_url_the_email_explains_the_code(string? baseUrl)
    {
        Assert.Null(new FrontendLinks(Provider(baseUrl)).InvitationUrl("token"));
        var invitation = CenterInvitation.CreateOwner(Guid.NewGuid(), "o@example.com", "O@EXAMPLE.COM", "hash",
            Now.AddDays(2), Now, Guid.NewGuid());
        var body = CenterInvitationMessageFactory.Create(invitation, "Center", "token", false, Now).Body;
        Assert.Contains("trang Nhận lời mời", body, StringComparison.Ordinal);
        Assert.Contains("token", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/", body, StringComparison.Ordinal);
    }

    private static ServiceProvider Provider(string? baseUrl)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Frontend:BaseUrl"] = baseUrl }).Build();
        return new ServiceCollection().AddSingleton<IConfiguration>(configuration).BuildServiceProvider();
    }
}
