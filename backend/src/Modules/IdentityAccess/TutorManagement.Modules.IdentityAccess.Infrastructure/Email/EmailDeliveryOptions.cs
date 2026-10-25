namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

public sealed class EmailDeliveryOptions
{
    public const string SectionName = "EmailDelivery";

    public EmailDeliveryProvider Provider { get; set; } = EmailDeliveryProvider.File;

    public string PickupDirectory { get; set; } = ".local/emails";

    public SmtpEmailDeliveryOptions Smtp { get; set; } = new();

    public int BatchSize { get; set; } = 50;

    public int MaximumConcurrency { get; set; } = 4;

    public int MaximumAttempts { get; set; } = 5;

    public int PollingIntervalMilliseconds { get; set; } = 2000;

    public TimeSpan PollingInterval => TimeSpan.FromMilliseconds(PollingIntervalMilliseconds);
}

public enum EmailDeliveryProvider
{
    File,
    Smtp
}

public sealed class SmtpEmailDeliveryOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public SmtpSecurityMode Security { get; set; } = SmtpSecurityMode.StartTls;

    public bool AuthenticationRequired { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Quản Lý Dạy Thêm";

    public int TimeoutMilliseconds { get; set; } = 30000;
}

public enum SmtpSecurityMode
{
    Auto,
    StartTls,
    StartTlsWhenAvailable,
    SslOnConnect,
    None
}
