using brevo_csharp.Api;
using brevo_csharp.Client;
using brevo_csharp.Model;

namespace Orbital.Service
{
    public class EmailService
    {
        private readonly TransactionalEmailsApi _api;
        private readonly ILogger<EmailService> _logger;
        private readonly string _fromEmail;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _logger = logger;

            var config = new brevo_csharp.Client.Configuration();
            config.ApiKey.Add("api-key", configuration["Brevo:ApiKey"]);
            _api = new TransactionalEmailsApi(config);
            _fromEmail = configuration["Brevo:FromEmail"] ?? "vibeound@gmail.com";
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                var sendEmail = new SendSmtpEmail
                {
                    To = new List<SendSmtpEmailTo> { new SendSmtpEmailTo(toEmail) },
                    Subject = subject,
                    HtmlContent = body,
                    Sender = new SendSmtpEmailSender("Orbital", _fromEmail)
                };

                var result = await _api.SendTransacEmailAsync(sendEmail);
                _logger.LogInformation("✅ Email sent to {Email}. MessageId: {MessageId}", toEmail, result.MessageId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to send email to {Email}", toEmail);
                return false;
            }
        }
    }
}