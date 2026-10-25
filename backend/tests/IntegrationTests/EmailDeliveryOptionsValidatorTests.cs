using TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

namespace TutorManagement.IntegrationTests;

public sealed class EmailDeliveryOptionsValidatorTests
{
    private readonly EmailDeliveryOptionsValidator _validator = new();

    [Fact]
    public void File_provider_accepts_a_valid_pickup_directory()
    {
        var options = new EmailDeliveryOptions
        {
            Provider = EmailDeliveryProvider.File,
            PickupDirectory = ".local/emails"
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Smtp_provider_accepts_a_secure_authenticated_configuration()
    {
        var options = CreateValidSmtpOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Smtp_provider_rejects_missing_credentials_and_sender()
    {
        var options = CreateValidSmtpOptions();
        options.Smtp.Username = string.Empty;
        options.Smtp.Password = string.Empty;
        options.Smtp.FromAddress = string.Empty;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Username", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Password", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("FromAddress", StringComparison.Ordinal));
    }

    private static EmailDeliveryOptions CreateValidSmtpOptions() =>
        new()
        {
            Provider = EmailDeliveryProvider.Smtp,
            Smtp = new SmtpEmailDeliveryOptions
            {
                Host = "smtp.example.com",
                Port = 587,
                Security = SmtpSecurityMode.StartTls,
                AuthenticationRequired = true,
                Username = "smtp-user",
                Password = "smtp-secret",
                FromAddress = "no-reply@example.com",
                FromName = "Tutor Management",
                TimeoutMilliseconds = 30000
            }
        };
}
