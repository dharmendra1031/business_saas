using System.Net;
using System.Net.Mail;

namespace BusinessSaas.Api.Features;

public interface IOwnerInvitationMailer
{
    bool IsConfigured { get; }
    Task SendAsync(string email, string link, CancellationToken cancellationToken);
}

// Fail closed without an authenticated TLS SMTP relay and an explicit HTTPS public origin.
// No mail credentials or invitation tokens are written to logs or API responses.
public sealed class SmtpOwnerInvitationMailer(IConfiguration configuration) : IOwnerInvitationMailer
{
    private string Host => configuration["OwnerInvitations:SmtpHost"]?.Trim() ?? "";
    private string User => configuration["OwnerInvitations:SmtpUser"]?.Trim() ?? "";
    private string Password => configuration["OwnerInvitations:SmtpPassword"] ?? "";
    private string From => configuration["OwnerInvitations:From"]?.Trim() ?? "";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(User) &&
        !string.IsNullOrWhiteSpace(Password) && MailAddress.TryCreate(From, out _) &&
        int.TryParse(configuration["OwnerInvitations:SmtpPort"], out var port) &&
        port is > 0 and <= 65535;

    public async Task SendAsync(string email, string link, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Invitation email delivery is not configured.");
        using var message = new MailMessage(new MailAddress(From), new MailAddress(email)) {
            Subject = "Business SaaS — set up your Owner account",
            Body = "You have been invited to create a Business SaaS Owner account.\n\n" +
                "Use this secure link within 48 hours:\n" + link + "\n\n" +
                "If you did not expect this invitation, ignore this email.",
            IsBodyHtml = false
        };
        using var client = new SmtpClient(Host, int.Parse(configuration["OwnerInvitations:SmtpPort"]!)) {
            EnableSsl = true,
            Credentials = new NetworkCredential(User, Password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
