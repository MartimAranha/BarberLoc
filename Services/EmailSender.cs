using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace WebApplication1.Services
{
    /// <summary>
    /// SMTP email sender powered by MailKit + MimeKit.
    /// Configuration is read from the "Smtp" section of appsettings.json.
    ///
    /// Required appsettings.json keys:
    ///   Smtp:Host        — SMTP server hostname  (e.g. sandbox.smtp.mailtrap.io)
    ///   Smtp:Port        — SMTP port             (e.g. 587)
    ///   Smtp:Username    — SMTP login username
    ///   Smtp:Password    — SMTP login password
    ///   Smtp:FromAddress — Sender email address  (e.g. noreply@barberloc.pt)
    ///   Smtp:FromName    — Sender display name   (e.g. BarberLoc)
    /// </summary>
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration config, ILogger<EmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            // ── Read SMTP settings from configuration ─────────────────────────
            var host        = _config["Smtp:Host"]        ?? throw new InvalidOperationException("Smtp:Host is not configured.");
            var port        = int.Parse(_config["Smtp:Port"] ?? "587");
            var username    = _config["Smtp:Username"]    ?? throw new InvalidOperationException("Smtp:Username is not configured.");
            var password    = _config["Smtp:Password"]    ?? throw new InvalidOperationException("Smtp:Password is not configured.");
            var fromAddress = _config["Smtp:FromAddress"] ?? "noreply@barberloc.pt";
            var fromName    = _config["Smtp:FromName"]    ?? "BarberLoc";

            // ── Build the MimeMessage ──────────────────────────────────────────
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            // Provide both HTML and a plain-text fallback for email clients that
            // don't render HTML (best practice for deliverability).
            var bodyBuilder = new BodyBuilder
            {
                HtmlBody  = htmlBody,
                TextBody  = "Por favor, utilize um cliente de email que suporte HTML para ver esta mensagem."
            };
            message.Body = bodyBuilder.ToMessageBody();

            // ── Connect, authenticate, send, disconnect ────────────────────────
            using var client = new SmtpClient();
            try
            {
                _logger.LogInformation("Sending email to {Email} via {Host}:{Port}", toEmail, host, port);

                // SecureSocketOptions.Auto: MailKit negotiates the best security the
                // server offers. Mailtrap sandbox (port 2525) uses plain SMTP with
                // optional STARTTLS — Auto handles this correctly. Use StartTls only
                // when the server mandates STARTTLS on connect (e.g. port 587 on Gmail).
                await client.ConnectAsync(host, port, SecureSocketOptions.Auto);
                await client.AuthenticateAsync(username, password);
                await client.SendAsync(message);

                _logger.LogInformation("Email sent successfully to {Email}", toEmail);
            }
            catch (Exception ex)
            {
                // Log and re-throw so the caller can decide how to surface the error.
                _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
                throw;
            }
            finally
            {
                // Always disconnect cleanly, even if send failed.
                await client.DisconnectAsync(quit: true);
            }
        }
    }
}
