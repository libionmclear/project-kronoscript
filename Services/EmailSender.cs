using Microsoft.AspNetCore.Identity.UI.Services;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace MyStoryTold.Services;

public class EmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration config, ILogger<EmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        await SendDiagnosticAsync(email, subject, htmlMessage);
    }

    /// <summary>
    /// Same as SendEmailAsync but returns a structured diagnostic so admin tools
    /// can show the actual SendGrid response (or the missing-API-key reason).
    /// </summary>
    public async Task<EmailSendResult> SendDiagnosticAsync(string email, string subject, string htmlMessage)
    {
        var apiKey = _config["SendGrid:ApiKey"];
        var fromEmail = _config["SendGrid:FromEmail"] ?? "noreply@kronoscript.net";
        var fromName = _config["SendGrid:FromName"] ?? "Kronoscript";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var reason = "SendGrid API key not configured (SendGrid:ApiKey is empty). " +
                         "Set SendGrid__ApiKey in Azure App Service > Configuration > Application Settings.";
            _logger.LogWarning("Email NOT sent to {Email}: {Reason}", email, reason);
            return new EmailSendResult(false, 0, reason, fromEmail);
        }

        try
        {
            var client = new SendGridClient(apiKey);
            var msg = new SendGridMessage
            {
                From = new EmailAddress(fromEmail, fromName),
                Subject = subject,
                HtmlContent = htmlMessage
            };
            msg.AddTo(new EmailAddress(email));

            var response = await client.SendEmailAsync(msg);
            var statusCode = (int)response.StatusCode;
            var body = response.Body != null ? await response.Body.ReadAsStringAsync() : "";

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent to {Email} (status {Status})", email, statusCode);
                return new EmailSendResult(true, statusCode, "Accepted by SendGrid", fromEmail);
            }
            else
            {
                _logger.LogError("SendGrid failed for {Email}: status {Status}, body: {Body}", email, statusCode, body);
                return new EmailSendResult(false, statusCode, $"SendGrid rejected the send. {body}", fromEmail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EmailSender threw for {Email}", email);
            return new EmailSendResult(false, -1, "Exception: " + ex.Message, fromEmail);
        }
    }
}

public record EmailSendResult(bool Success, int StatusCode, string Message, string From);
