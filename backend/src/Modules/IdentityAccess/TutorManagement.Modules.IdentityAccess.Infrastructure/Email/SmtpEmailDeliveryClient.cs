using System.Collections.Concurrent;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

public sealed class SmtpEmailDeliveryClient(
    IOptions<EmailDeliveryOptions> options) : IEmailDeliveryClient, IAsyncDisposable
{
    private readonly SmtpEmailDeliveryOptions _options = options.Value.Smtp;
    private readonly ConcurrentBag<SmtpClient> _clients = [];
    private readonly SemaphoreSlim _connectionSlots = new(
        options.Value.MaximumConcurrency,
        options.Value.MaximumConcurrency);

    public async Task DeliverAsync(
        EmailDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        MimeMessage message;
        try
        {
            message = CreateMessage(request);
        }
        catch (ParseException exception)
        {
            throw new PermanentEmailDeliveryException($"Địa chỉ người nhận không hợp lệ: {request.Recipient}", exception);
        }

        await _connectionSlots.WaitAsync(cancellationToken);
        SmtpClient? client = null;
        try
        {
            var reused = _clients.TryTake(out client) && client.IsConnected;
            if (!reused)
            {
                client?.Dispose();
                client = await CreateConnectedClientAsync(cancellationToken);
            }

            try
            {
                await SendAsync(client!, message, request, cancellationToken);
            }
            catch (Exception exception) when (reused && IsStaleConnection(exception))
            {
                // Providers (Gmail included) drop idle sessions; a pooled client still believes it is connected.
                // That is not the message's fault: retry once on a fresh connection instead of waiting for back-off.
                client!.Dispose();
                client = null;
                client = await CreateConnectedClientAsync(cancellationToken);
                await SendAsync(client, message, request, cancellationToken);
            }

            _clients.Add(client!);
            client = null;
        }
        catch (PermanentEmailDeliveryException)
        {
            // The session is still usable after a rejected recipient; keep it for the next message.
            if (client is { IsConnected: true })
            {
                _clients.Add(client);
                client = null;
            }
            throw;
        }
        finally
        {
            // A failed SMTP operation can leave the session unusable. Discard it;
            // the next delivery creates a fresh authenticated connection.
            client?.Dispose();
            _connectionSlots.Release();
        }
    }

    private static async Task SendAsync(SmtpClient client, MimeMessage message, EmailDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.SendAsync(message, cancellationToken);
        }
        catch (SmtpCommandException exception) when (IsPermanent(exception))
        {
            throw new PermanentEmailDeliveryException(
                $"Máy chủ thư từ chối người nhận {request.Recipient}: {(int)exception.StatusCode} {exception.Message}", exception);
        }
    }

    private static bool IsStaleConnection(Exception exception) =>
        exception is SmtpProtocolException or ServiceNotConnectedException or IOException;

    internal MimeMessage CreateMessage(EmailDeliveryRequest request)
    {
        var message = new MimeMessage
        {
            Date = request.CreatedAtUtc,
            Subject = request.Subject,
            Body = new TextPart(TextFormat.Plain)
            {
                Text = request.Body
            }
        };

        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(request.Recipient));
        message.Headers.Add("X-TutorManagement-Outbox-Id", request.MessageId.ToString("N"));
        return message;
    }

    public async ValueTask DisposeAsync()
    {
        while (_clients.TryTake(out var client))
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(quit: true);
                }
            }
            catch (Exception)
            {
                // Host shutdown must continue even when an SMTP server is gone.
            }
            finally
            {
                client.Dispose();
            }
        }

        _connectionSlots.Dispose();
    }

    private async Task<SmtpClient> CreateConnectedClientAsync(
        CancellationToken cancellationToken)
    {
        var client = new SmtpClient
        {
            Timeout = _options.TimeoutMilliseconds,
            CheckCertificateRevocation = true
        };

        try
        {
            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                MapSecurityMode(_options.Security),
                cancellationToken);

            if (_options.AuthenticationRequired)
            {
                // Password-based SMTP accounts cannot use OAuth mechanisms.
                client.AuthenticationMechanisms.Remove("XOAUTH2");
                client.AuthenticationMechanisms.Remove("OAUTHBEARER");
                await client.AuthenticateAsync(
                    _options.Username,
                    _options.Password,
                    cancellationToken);
            }

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>5xx about the recipient (550 mailbox unavailable, 553 bad address…) will fail the same way every time.</summary>
    internal static bool IsPermanent(SmtpCommandException exception) =>
        exception.ErrorCode == SmtpErrorCode.RecipientNotAccepted && (int)exception.StatusCode >= 500;

    private static SecureSocketOptions MapSecurityMode(SmtpSecurityMode securityMode) =>
        securityMode switch
        {
            SmtpSecurityMode.Auto => SecureSocketOptions.Auto,
            SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
            SmtpSecurityMode.StartTlsWhenAvailable => SecureSocketOptions.StartTlsWhenAvailable,
            SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SmtpSecurityMode.None => SecureSocketOptions.None,
            _ => throw new ArgumentOutOfRangeException(
                nameof(securityMode),
                securityMode,
                "Unsupported SMTP security mode.")
        };
}
