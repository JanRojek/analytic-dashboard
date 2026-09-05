using System.Collections.Concurrent;
using AnalyticDashboard.Application.Auth.Email;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class TestEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<TestEmailMessage> _messages = new();

    private Exception? _nextException;

    public IReadOnlyCollection<TestEmailMessage> Messages =>
        _messages.ToArray();

    public void FailNextSend(
        Exception exception)
    {
        Interlocked.Exchange(
            ref _nextException,
            exception
        );
    }

    public Task SendAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var exception = Interlocked.Exchange(
            ref _nextException,
            null
        );

        if (exception is not null)
        {
            return Task.FromException(
                exception
            );
        }

        _messages.Enqueue(
            new TestEmailMessage(
                recipientEmail,
                subject,
                htmlBody
            )
        );

        return Task.CompletedTask;
    }
}

public sealed record TestEmailMessage(
    string RecipientEmail,
    string Subject,
    string HtmlBody
);
