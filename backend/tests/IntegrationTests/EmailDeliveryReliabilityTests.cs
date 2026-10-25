using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Email;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Email;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>Real sending: retries wait instead of burning every attempt, bad addresses stop at once, texts are Vietnamese.</summary>
public sealed class EmailDeliveryReliabilityTests
{
    [Fact]
    public void Failed_attempt_waits_before_the_next_try()
    {
        var now = DateTimeOffset.UtcNow;
        var message = EmailOutboxMessage.Create("a@example.invalid", "S", "B", now);
        message.Claim(Guid.NewGuid(), now.AddMinutes(15));
        message.MarkFailed("Connection refused", now.AddMinutes(1));
        Assert.Null(message.ProcessingId);
        Assert.Equal(now.AddMinutes(1), message.ProcessingUntilUtc);
        Assert.Equal(1, message.AttemptCount);

        Assert.Equal(TimeSpan.FromMinutes(1), EmailOutboxProcessor.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(5), EmailOutboxProcessor.RetryDelay(2));
        Assert.Equal(TimeSpan.FromMinutes(15), EmailOutboxProcessor.RetryDelay(3));
        Assert.Equal(TimeSpan.FromHours(1), EmailOutboxProcessor.RetryDelay(4));
    }

    [Fact]
    public void Undeliverable_message_is_never_retried_and_skipped_one_keeps_its_reason()
    {
        var now = DateTimeOffset.UtcNow;
        var bad = EmailOutboxMessage.Create("a@example.invalid", "S", "B", now);
        bad.MarkUndeliverable("550 mailbox unavailable", 5);
        Assert.Equal(5, bad.AttemptCount);
        Assert.Null(bad.ProcessingUntilUtc);
        Assert.Null(bad.ProcessedAtUtc);

        var stale = EmailOutboxMessage.Create("a@example.invalid", "S", "123456", now);
        stale.MarkSkipped("Không gửi: mã đã hết hạn.", now);
        Assert.Equal(now, stale.ProcessedAtUtc);
        Assert.Equal(string.Empty, stale.Body);
        Assert.Equal("Không gửi: mã đã hết hạn.", stale.LastError);
    }

    [Fact]
    public void Invitation_and_verification_emails_are_vietnamese_with_links()
    {
        var now = DateTimeOffset.UtcNow;
        var invitation = CenterInvitation.CreateMember(Guid.NewGuid(), "moi@example.invalid", "MOI@EXAMPLE.INVALID",
            CenterMemberRole.Teacher, "hash", now.AddHours(48), now, Guid.NewGuid());
        var mail = CenterInvitationMessageFactory.Create(invitation, "Ánh Sao", "TOKEN123", accountExists: false, now,
            "https://app.example.com/accept-invitation?token=TOKEN123");
        Assert.Equal("Lời mời tham gia Ánh Sao — Quản Lý Dạy Thêm", mail.Subject);
        Assert.Contains("làm giáo viên của Ánh Sao", mail.Body, StringComparison.Ordinal);
        Assert.Contains("https://app.example.com/accept-invitation?token=TOKEN123", mail.Body, StringComparison.Ordinal);
        Assert.Contains("TOKEN123", mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/", mail.Body, StringComparison.Ordinal);

        var links = new FrontendLinks(new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Frontend:BaseUrl"] = "https://app.example.com/" }).Build())
            .BuildServiceProvider());
        Assert.Equal("https://app.example.com/verify-email?token=a%2Bb", links.VerifyEmailUrl("a+b"));
    }

    [Fact]
    public async Task Smtp_rejection_of_the_recipient_is_permanent()
    {
        await using var server = new RejectingSmtpServer();
        await using var client = new SmtpEmailDeliveryClient(Options.Create(new EmailDeliveryOptions
        {
            Provider = EmailDeliveryProvider.Smtp,
            MaximumConcurrency = 1,
            Smtp = new SmtpEmailDeliveryOptions
            {
                Host = IPAddress.Loopback.ToString(), Port = server.Port, Security = SmtpSecurityMode.None,
                AuthenticationRequired = false, FromAddress = "no-reply@example.com", TimeoutMilliseconds = 5000
            }
        }));
        var request = new EmailDeliveryRequest(Guid.NewGuid(), "nobody@example.com", "Thử", "Nội dung", DateTimeOffset.UtcNow);

        var error = await Assert.ThrowsAsync<PermanentEmailDeliveryException>(() => client.DeliverAsync(request, CancellationToken.None));
        Assert.Contains("550", error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<PermanentEmailDeliveryException>(() => client.DeliverAsync(
            request with { Recipient = "not an address" }, CancellationToken.None));
    }

    [Fact]
    public async Task Pooled_connection_dropped_by_the_server_is_replaced_without_failing_the_message()
    {
        await using var server = new DroppingSmtpServer();
        await using var client = new SmtpEmailDeliveryClient(Options.Create(new EmailDeliveryOptions
        {
            Provider = EmailDeliveryProvider.Smtp,
            MaximumConcurrency = 1,
            Smtp = new SmtpEmailDeliveryOptions
            {
                Host = IPAddress.Loopback.ToString(), Port = server.Port, Security = SmtpSecurityMode.None,
                AuthenticationRequired = false, FromAddress = "no-reply@example.com", TimeoutMilliseconds = 5000
            }
        }));
        var request = new EmailDeliveryRequest(Guid.NewGuid(), "a@example.com", "Một", "Nội dung", DateTimeOffset.UtcNow);
        await client.DeliverAsync(request, CancellationToken.None);
        await server.FirstSessionClosed.WaitAsync(TimeSpan.FromSeconds(5));
        await client.DeliverAsync(request with { MessageId = Guid.NewGuid(), Subject = "Hai" }, CancellationToken.None);
        Assert.Equal(2, server.Accepted);
    }

    [LocalPostgresFact]
    public async Task Outbox_retries_later_gives_up_on_rejection_and_admin_sees_it()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var tag = Guid.NewGuid().ToString("N")[..10];
        string Address(string kind) => $"{kind}-{tag}@example.invalid";
        // Created long ago so this batch (size 3, oldest first) claims exactly these rows and nobody else's.
        var old = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var now = DateTimeOffset.UtcNow;
        var admin = User.CreateExternal(Address("admin"), now);
        try
        {
            await using (var db = new IdentityDbContext(options))
            {
                db.EmailOutboxMessages.AddRange(
                    EmailOutboxMessage.Create(Address("ok"), "OK", "b", old),
                    EmailOutboxMessage.Create(Address("down"), "Down", "b", old.AddSeconds(1)),
                    EmailOutboxMessage.Create(Address("bad"), "Bad", "b", old.AddSeconds(2)));
                db.Users.Add(admin);
                await db.SaveChangesAsync();
            }

            var settings = Options.Create(new EmailDeliveryOptions { BatchSize = 3, MaximumAttempts = 5 });
            await using (var db = new IdentityDbContext(options))
            {
                var processor = new EmailOutboxProcessor(db, new ScriptedClient(), settings, new FixedClock(now),
                    NullLogger<EmailOutboxProcessor>.Instance);
                var result = await processor.ProcessPendingAsync(CancellationToken.None);
                Assert.Equal(3, result.SelectedCount);
                Assert.Equal(1, result.ProcessedCount);
                Assert.Equal(2, result.FailedCount);
            }

            await using (var db = new IdentityDbContext(options))
            {
                var rows = await db.EmailOutboxMessages.AsNoTracking()
                    .Where(x => x.Recipient.EndsWith($"-{tag}@example.invalid")).ToDictionaryAsync(x => x.Subject);
                Assert.NotNull(rows["OK"].ProcessedAtUtc);
                Assert.Equal(1, rows["Down"].AttemptCount);
                Assert.Null(rows["Down"].ProcessedAtUtc);
                Assert.InRange(rows["Down"].ProcessingUntilUtc!.Value, now.AddMinutes(1).AddMilliseconds(-1), now.AddMinutes(1).AddMilliseconds(1));
                Assert.Equal(5, rows["Bad"].AttemptCount);
                Assert.Contains("550", rows["Bad"].LastError, StringComparison.Ordinal);
            }

            await using (var db = new IdentityDbContext(options))
            {
                var service = new PlatformEmailService(db, new FixedClock(now), new ConfigurationBuilder().Build());
                var queued = await service.QueueTestAsync(admin.Id, null, CancellationToken.None);
                Assert.True(queued.IsSuccess);
                Assert.Equal(admin.Email, queued.Value.Recipient);
                Assert.Equal("Pending", queued.Value.Status);
                Assert.Equal(PlatformEmailErrors.TooSoon,
                    (await service.QueueTestAsync(admin.Id, Address("other"), CancellationToken.None)).Error);
                Assert.Equal(PlatformEmailErrors.InvalidRecipient,
                    (await new PlatformEmailService(db, new FixedClock(now.AddMinutes(1)), new ConfigurationBuilder().Build())
                        .QueueTestAsync(admin.Id, "not-an-email", CancellationToken.None)).Error);
                var overview = await service.OverviewAsync(CancellationToken.None);
                Assert.Contains(overview.Recent, x => x.Id == queued.Value.Id && x.Status == "Pending");
                Assert.True(overview.Failed >= 1);
                Assert.True(overview.Retrying >= 1);
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM iam.email_outbox_messages WHERE recipient LIKE @pattern;
                DELETE FROM iam.users WHERE id = @admin;
                """, connection);
            cleanup.Parameters.AddWithValue("pattern", $"%-{tag}@example.invalid");
            cleanup.Parameters.AddWithValue("admin", admin.Id);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class ScriptedClient : IEmailDeliveryClient
    {
        public Task DeliverAsync(EmailDeliveryRequest request, CancellationToken cancellationToken) =>
            request.Recipient.StartsWith("down-", StringComparison.Ordinal)
                ? throw new IOException("Connection refused")
                : request.Recipient.StartsWith("bad-", StringComparison.Ordinal)
                    ? throw new PermanentEmailDeliveryException("550 mailbox unavailable")
                    : Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>Accepts one message per connection, then closes the socket without a word (like an idle timeout).</summary>
    private sealed class DroppingSmtpServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly System.Net.Sockets.TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly TaskCompletionSource _firstClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _task;
        private int _accepted;

        public DroppingSmtpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _task = RunAsync(_stop.Token);
        }

        public int Port { get; }
        public int Accepted => _accepted;
        public Task FirstSessionClosed => _firstClosed.Task;

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try { await _task; } catch (OperationCanceledException) { } catch (System.Net.Sockets.SocketException) { }
            _stop.Dispose();
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(token);
                await using (var stream = client.GetStream())
                {
                    using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, false, leaveOpen: true);
                    await using var writer = new StreamWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
                    await writer.WriteLineAsync("220 localhost test");
                    var data = false;
                    while (await reader.ReadLineAsync(token) is { } line)
                    {
                        if (data)
                        {
                            if (line != ".") continue;
                            Interlocked.Increment(ref _accepted);
                            await writer.WriteLineAsync("250 Message accepted");
                            break;
                        }
                        if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("250-localhost");
                            await writer.WriteLineAsync("250 SIZE 10485760");
                        }
                        else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                        {
                            data = true;
                            await writer.WriteLineAsync("354 Go ahead");
                        }
                        else await writer.WriteLineAsync("250 OK");
                    }
                }
                client.Close();
                _firstClosed.TrySetResult();
            }
        }
    }

    /// <summary>Accepts the session but answers 550 to every RCPT TO.</summary>
    private sealed class RejectingSmtpServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly System.Net.Sockets.TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _task;

        public RejectingSmtpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _task = RunAsync(_stop.Token);
        }

        public int Port { get; }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try { await _task; } catch (OperationCanceledException) { } catch (System.Net.Sockets.SocketException) { }
            _stop.Dispose();
        }

        private async Task RunAsync(CancellationToken token)
        {
            using var client = await _listener.AcceptTcpClientAsync(token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, false, leaveOpen: true);
            await using var writer = new StreamWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 localhost test");
            while (!token.IsCancellationRequested && await reader.ReadLineAsync(token) is { } line)
            {
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-localhost");
                    await writer.WriteLineAsync("250 SIZE 10485760");
                }
                else if (line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                    await writer.WriteLineAsync("550 5.1.1 Mailbox unavailable");
                else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 Bye");
                    return;
                }
                else await writer.WriteLineAsync("250 OK");
            }
        }
    }
}
