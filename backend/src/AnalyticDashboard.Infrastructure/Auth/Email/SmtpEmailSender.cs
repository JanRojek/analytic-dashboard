using AnalyticDashboard.Application.Auth.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Net.Sockets;

namespace AnalyticDashboard.Infrastructure.Auth.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var message = new MimeMessage();

        message.From.Add(
            new MailboxAddress(
                _options.FromName,
                _options.FromEmail
            )
        );

        message.To.Add(
            MailboxAddress.Parse(recipientEmail)
        );

        message.Subject = subject;

        message.Body = new TextPart("html")
        {
            Text = htmlBody
        };

        using var client = new SmtpClient();

        var socketOptions = _options.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.None;

        try
        {
            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                socketOptions,
                cancellationToken
            );

            if (!string.IsNullOrWhiteSpace(_options.Username)
                && !string.IsNullOrWhiteSpace(_options.Password))
            {
                await client.AuthenticateAsync(
                    _options.Username,
                    _options.Password,
                    cancellationToken
                );
            }

            await client.SendAsync(
                message,
                cancellationToken
            );

            await client.DisconnectAsync(
                true,
                cancellationToken
            );
        }
        catch (SocketException exception)
        {
            throw new EmailDeliveryException(
                "Failed to connect to the SMTP server.",
                exception
            );
        }
        catch (SslHandshakeException exception)
        {
            throw new EmailDeliveryException(
                "A TLS handshake error occurred while connecting to the SMTP server.",
                exception
            );
        }
        catch (SmtpCommandException exception)
        {
            throw new EmailDeliveryException(
                "The SMTP server rejected the email.",
                exception
            );
        }
        catch (SmtpProtocolException exception)
        {
            throw new EmailDeliveryException(
                "An SMTP protocol error occurred while sending the email.",
                exception
            );
        }
        catch (IOException exception)
        {
            throw new EmailDeliveryException(
                "An I/O error occurred while sending the email.",
                exception
            );
        }
    }
}
