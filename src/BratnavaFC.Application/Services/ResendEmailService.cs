using BratnavaFC.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Resend;

namespace BratnavaFC.Application.Services;

public sealed class ResendEmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(IResend resend, ILogger<ResendEmailService> logger)
    {
        _resend = resend;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string code)
    {
        try
        {
            var message = new EmailMessage
            {
                From = "onboarding@resend.dev",
                To = { toEmail },
                Subject = "Código de recuperação de senha - BratnavaFC",
                HtmlBody = $"<p>Seu código de recuperação é: <strong style=\"font-size:24px;letter-spacing:4px\">{code}</strong></p><p>Válido por <strong>15 minutos</strong>.</p><p>Se você não solicitou a recuperação de senha, ignore este email.</p>",
            };

            await _resend.EmailSendAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar email de recuperação para {Email}", toEmail);
        }
    }
}
