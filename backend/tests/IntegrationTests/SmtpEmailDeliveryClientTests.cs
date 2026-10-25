using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

namespace TutorManagement.IntegrationTests;

public sealed class SmtpEmailDeliveryClientTests
{
    [Fact]
    public async Task Delivery_sends_a_valid_message_over_smtp()
    {
        await using var server = new LocalSmtpServer();
        await using var client = new SmtpEmailDeliveryClient(
            Options.Create(new EmailDeliveryOptions
            {
                Provider = EmailDeliveryProvider.Smtp,
                MaximumConcurrency = 1,
                Smtp = new SmtpEmailDeliveryOptions
                {
                    Host = IPAddress.Loopback.ToString(),
                    Port = server.Port,
                    Security = SmtpSecurityMode.None,
                    AuthenticationRequired = false,
                    FromAddress = "no-reply@example.com",
                    FromName = "Tutor Management",
                    TimeoutMilliseconds = 5000
                }
            }));
        var request = new EmailDeliveryRequest(
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            "owner@example.com",
            "Verify email",
            "Your verification token is 123456.",
            new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));

        await client.DeliverAsync(request, CancellationToken.None);
        var receivedMessage = await server.ReceivedMessage.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Contains("From: Tutor Management <no-reply@example.com>", receivedMessage);
        Assert.Contains("To: owner@example.com", receivedMessage);
        Assert.Contains("Subject: Verify email", receivedMessage);
        Assert.Contains("X-TutorManagement-Outbox-Id: 30000000000000000000000000000002", receivedMessage);
        Assert.Contains("Your verification token is 123456.", receivedMessage);
    }

    private sealed class LocalSmtpServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stoppingTokenSource = new();
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serverTask;
        private readonly TaskCompletionSource<string> _receivedMessage = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public LocalSmtpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = RunAsync(_stoppingTokenSource.Token);
        }

        public int Port { get; }

        public Task<string> ReceivedMessage => _receivedMessage.Task;

        public async ValueTask DisposeAsync()
        {
            await _stoppingTokenSource.CancelAsync();
            _listener.Stop();

            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when a test finishes before the client sends QUIT.
            }

            _stoppingTokenSource.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            await using var writer = new StreamWriter(
                stream,
                Encoding.ASCII,
                leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };

            await writer.WriteLineAsync("220 localhost TutorManagement test SMTP");
            var message = new StringBuilder();
            var readingData = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    return;
                }

                if (readingData)
                {
                    if (line == ".")
                    {
                        readingData = false;
                        _receivedMessage.TrySetResult(message.ToString());
                        await writer.WriteLineAsync("250 2.0.0 Message accepted");
                    }
                    else
                    {
                        message.AppendLine(line);
                    }

                    continue;
                }

                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-localhost");
                    await writer.WriteLineAsync("250 SIZE 10485760");
                }
                else if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 2.1.0 OK");
                }
                else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    readingData = true;
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                }
                else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 2.0.0 Bye");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 OK");
                }
            }
        }
    }
}
