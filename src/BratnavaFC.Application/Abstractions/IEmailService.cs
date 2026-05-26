namespace BratnavaFC.Application.Abstractions;

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string code);
}
