using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BratnavaFC.Infrastructure;

/// <summary>
/// Fonte única da verdade para "os jobs em background estão ligados neste processo?".
///
/// Três wirings precisam concordar: o storage e o servidor do Hangfire, o
/// <c>IBackgroundJobClient</c> que o NotificationScheduler consome, e o dashboard mais o
/// agendamento dos recorrentes no pipeline. Se divergirem, o erro só aparece em runtime —
/// criar uma partida lançaria por não resolver <c>IBackgroundJobClient</c>, ou o boot
/// morreria pedindo <c>IRecurringJobManager</c> a um container que não o tem.
///
/// O critério é a presença da RedisConnection porque o storage passou a ser Redis. Isso
/// desliga o Hangfire em Development e também no app de dev do Fly, que roda como
/// Production mas não tem Redis provisionado — sem precisar de flag nova.
/// </summary>
public static class BackgroundJobsGate
{
    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
        => !environment.IsDevelopment()
           && !string.IsNullOrWhiteSpace(configuration.GetConnectionString("RedisConnection"));
}
