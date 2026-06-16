namespace WebApplication1.Services
{
    /// <summary>
    /// Abstraction over the transactional email provider.
    /// Swap the implementation (SMTP, SendGrid, SES, etc.) without touching callers.
    /// </summary>
    public interface IEmailSender
    {
        /// <summary>
        /// Sends a transactional email asynchronously.
        /// </summary>
        /// <param name="toEmail">Recipient email address.</param>
        /// <param name="subject">Email subject line.</param>
        /// <param name="htmlBody">Full HTML body of the email.</param>
        Task SendEmailAsync(string toEmail, string subject, string htmlBody);
    }
}
