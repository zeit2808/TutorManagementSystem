using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

public sealed partial class FileEmailDeliveryClient(
    IOptions<EmailDeliveryOptions> options) : IEmailDeliveryClient
{
    private readonly EmailDeliveryOptions _options = options.Value;

    public async Task DeliverAsync(
        EmailDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pickupDirectory = Path.GetFullPath(_options.PickupDirectory);
        Directory.CreateDirectory(pickupDirectory);

        var safeRecipient = UnsafeFileNameCharacters().Replace(request.Recipient, "_");
        var fileName = $"{request.CreatedAtUtc:yyyyMMddHHmmssfff}_{request.MessageId:N}_{safeRecipient}.txt";
        var content = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"To: {request.Recipient}")
            .AppendLine(CultureInfo.InvariantCulture, $"Subject: {request.Subject}")
            .AppendLine(CultureInfo.InvariantCulture, $"Created-At-Utc: {request.CreatedAtUtc:O}")
            .AppendLine()
            .Append(request.Body)
            .ToString();

        await File.WriteAllTextAsync(
            Path.Combine(pickupDirectory, fileName),
            content,
            Encoding.UTF8,
            cancellationToken);
    }

    [GeneratedRegex(@"[^A-Za-z0-9@._-]")]
    private static partial Regex UnsafeFileNameCharacters();
}
