using System.Net;
using System.Net.Sockets;
using System.Text;

using AnalyticDashboard.Application.Auth.Email;
using AnalyticDashboard.Infrastructure.Auth.Email;

using Microsoft.Extensions.Options;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class SmtpEmailSenderTests
{
    [Theory]
    [InlineData(SmtpEncryptionMode.None)]
    [InlineData(SmtpEncryptionMode.StartTls)]
    public async Task Send_ShouldAllowLocalPlaintextOnlyWhenExplicitlyConfigured(SmtpEncryptionMode mode)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var cancellationToken = timeout.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sender = CreateSender(port, mode);

        var sending = sender.SendAsync(
            "recipient@example.com",
            "Transport test",
            "<p>Hello</p>",
            cancellationToken
        );
        using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.NewLine = "\r\n";
        writer.AutoFlush = true;
        await writer.WriteLineAsync("220 localhost SMTP test".AsMemory(), cancellationToken);
        Assert.StartsWith("EHLO ", await reader.ReadLineAsync(cancellationToken));
        await writer.WriteLineAsync("250 localhost".AsMemory(), cancellationToken);

        if (mode == SmtpEncryptionMode.StartTls)
        {
            var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() => sending);
            Assert.IsType<NotSupportedException>(exception.InnerException);
            Assert.Null(await reader.ReadLineAsync(cancellationToken));
            return;
        }

        Assert.StartsWith("MAIL FROM:", await reader.ReadLineAsync(cancellationToken));
        await writer.WriteLineAsync("250 OK".AsMemory(), cancellationToken);
        Assert.StartsWith("RCPT TO:", await reader.ReadLineAsync(cancellationToken));
        await writer.WriteLineAsync("250 OK".AsMemory(), cancellationToken);
        Assert.Equal("DATA", await reader.ReadLineAsync(cancellationToken));
        await writer.WriteLineAsync("354 Send message".AsMemory(), cancellationToken);
        string? line;
        do
        {
            line = await reader.ReadLineAsync(cancellationToken);
            Assert.NotNull(line);
        } while (line != ".");
        await writer.WriteLineAsync("250 Accepted".AsMemory(), cancellationToken);
        Assert.Equal("QUIT", await reader.ReadLineAsync(cancellationToken));
        await writer.WriteLineAsync("221 Bye".AsMemory(), cancellationToken);

        await sending;
    }

    [Fact]
    public async Task Send_ShouldStartTlsHandshakeImmediately_WhenSslOnConnectIsRequired()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var sender = CreateSender(
            ((IPEndPoint)listener.LocalEndpoint).Port,
            SmtpEncryptionMode.SslOnConnect
        );

        var sending = sender.SendAsync(
            "recipient@example.com",
            "Transport test",
            "<p>Hello</p>",
            timeout.Token
        );
        using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
        await using var stream = connection.GetStream();
        var firstByte = new byte[1];
        Assert.Equal(1, await stream.ReadAsync(firstByte, timeout.Token));
        Assert.Equal(0x16, firstByte[0]);
        connection.Close();

        await Assert.ThrowsAsync<EmailDeliveryException>(() => sending);
    }

    private static SmtpEmailSender CreateSender(int port, SmtpEncryptionMode mode)
    {
        return new SmtpEmailSender(Options.Create(new SmtpOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Username = "",
            Password = "",
            FromEmail = "sender@example.com",
            FromName = "Aperture",
            EncryptionMode = mode
        }));
    }
}
