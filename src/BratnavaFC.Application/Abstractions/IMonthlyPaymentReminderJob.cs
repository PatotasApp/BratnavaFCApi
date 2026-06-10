namespace BratnavaFC.Application.Abstractions;

public interface IMonthlyPaymentReminderJob
{
    Task ExecuteAsync(CancellationToken ct = default);
}
