using Microsoft.Extensions.Options;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

namespace TutorManagement.IntegrationTests;

public sealed class FileEmailDeliveryClientTests
{
    [Fact]
    public async Task Delivery_writes_the_expected_local_email_file()
    {
        var pickupDirectory = Path.Combine(
            Path.GetTempPath(),
            "TutorManagement.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var client = new FileEmailDeliveryClient(
                Options.Create(new EmailDeliveryOptions
                {
                    PickupDirectory = pickupDirectory
                }));
            var request = new EmailDeliveryRequest(
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                "owner@example.com",
                "Center invitation",
                "Invitation body",
                new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));

            await client.DeliverAsync(request, CancellationToken.None);

            var file = Assert.Single(Directory.GetFiles(pickupDirectory, "*.txt"));
            var content = await File.ReadAllTextAsync(file);
            Assert.Contains("To: owner@example.com", content, StringComparison.Ordinal);
            Assert.Contains("Subject: Center invitation", content, StringComparison.Ordinal);
            Assert.Contains("Invitation body", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(pickupDirectory))
            {
                Directory.Delete(pickupDirectory, recursive: true);
            }
        }
    }
}
