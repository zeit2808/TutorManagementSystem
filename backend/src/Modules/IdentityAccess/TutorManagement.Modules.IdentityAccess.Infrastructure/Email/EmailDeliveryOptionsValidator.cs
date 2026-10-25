using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

public sealed class EmailDeliveryOptionsValidator : IValidateOptions<EmailDeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (!Enum.IsDefined(options.Provider))
        {
            errors.Add("EmailDelivery:Provider must be either File or Smtp.");
        }

        if (options.BatchSize is < 1 or > 500)
        {
            errors.Add("EmailDelivery:BatchSize must be between 1 and 500.");
        }

        if (options.MaximumConcurrency is < 1 or > 32)
        {
            errors.Add("EmailDelivery:MaximumConcurrency must be between 1 and 32.");
        }

        if (options.MaximumAttempts is < 1 or > 20)
        {
            errors.Add("EmailDelivery:MaximumAttempts must be between 1 and 20.");
        }

        if (options.PollingIntervalMilliseconds is < 250 or > 60000)
        {
            errors.Add("EmailDelivery:PollingIntervalMilliseconds must be between 250 and 60000.");
        }

        if (options.Provider == EmailDeliveryProvider.File)
        {
            if (string.IsNullOrWhiteSpace(options.PickupDirectory))
            {
                errors.Add("EmailDelivery:PickupDirectory is required for the File provider.");
            }

            return errors.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(errors);
        }

        ValidateSmtp(options.Smtp, errors);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateSmtp(
        SmtpEmailDeliveryOptions smtp,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            errors.Add("EmailDelivery:Smtp:Host is required for the Smtp provider.");
        }

        if (smtp.Port is < 1 or > 65535)
        {
            errors.Add("EmailDelivery:Smtp:Port must be between 1 and 65535.");
        }

        if (!Enum.IsDefined(smtp.Security))
        {
            errors.Add("EmailDelivery:Smtp:Security is invalid.");
        }

        if (smtp.AuthenticationRequired && string.IsNullOrWhiteSpace(smtp.Username))
        {
            errors.Add("EmailDelivery:Smtp:Username is required when authentication is enabled.");
        }

        if (smtp.AuthenticationRequired && string.IsNullOrWhiteSpace(smtp.Password))
        {
            errors.Add("EmailDelivery:Smtp:Password is required when authentication is enabled.");
        }

        if (!IsValidEmailAddress(smtp.FromAddress))
        {
            errors.Add("EmailDelivery:Smtp:FromAddress must be a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(smtp.FromName))
        {
            errors.Add("EmailDelivery:Smtp:FromName is required.");
        }

        if (smtp.TimeoutMilliseconds is < 1000 or > 120000)
        {
            errors.Add("EmailDelivery:Smtp:TimeoutMilliseconds must be between 1000 and 120000.");
        }
    }

    private static bool IsValidEmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
