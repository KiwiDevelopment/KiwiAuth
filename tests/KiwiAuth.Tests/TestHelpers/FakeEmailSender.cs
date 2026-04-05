using KiwiAuth.Services;

namespace KiwiAuth.Tests.TestHelpers;

public class FakeEmailSender : IEmailSender
{
    public record SentEmail(string To, string Subject, string Body);

    private readonly List<SentEmail> _emails = [];

    public IReadOnlyList<SentEmail> SentEmails => _emails;

    public Task SendAsync(string to, string subject, string htmlBody)
    {
        _emails.Add(new SentEmail(to, subject, htmlBody));
        return Task.CompletedTask;
    }

    public SentEmail? LastTo(string email) =>
        _emails.LastOrDefault(e => e.To == email);
}
