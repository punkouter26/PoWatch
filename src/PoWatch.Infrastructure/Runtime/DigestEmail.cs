using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Contracts;

namespace PoWatch.Infrastructure.Runtime;

/// <summary>
/// The weekly digest goes out through Azure Communication Services Email when
/// <c>Digest:Email:ConnectionString</c> and <c>Digest:Email:Sender</c> are both set; otherwise the
/// digest stays in-app and Settings says so.
/// </summary>
public static class DigestEmail
{
    public static IServiceCollection AddPoWatchDigestEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration["Digest:Email:ConnectionString"];
        var sender = configuration["Digest:Email:Sender"];
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(sender))
            services.AddSingleton<IDigestSender, NoDigestSender>();
        else
            services.AddSingleton<IDigestSender>(new AcsDigestSender(new EmailClient(connection), sender));
        return services;
    }

    private sealed class NoDigestSender : IDigestSender
    {
        public bool IsEnabled => false;

        public Task SendAsync(string to, string subject, string html, string text, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No email sender is configured.");
    }

    private sealed class AcsDigestSender(EmailClient client, string sender) : IDigestSender
    {
        public bool IsEnabled => true;

        public async Task SendAsync(string to, string subject, string html, string text, CancellationToken cancellationToken)
        {
            var message = new EmailMessage(sender, to, new EmailContent(subject) { Html = html, PlainText = text });
            // Started, not Completed: delivery is the service's job; the send is accepted or it throws.
            await client.SendAsync(WaitUntil.Started, message, cancellationToken);
        }
    }
}
