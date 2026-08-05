using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BratnavaFC.Infrastructure;

/// <summary>
/// Controla se o <c>ReplayStreamConsumerService</c> roda neste processo.
///
/// O consumidor estaciona um <c>XREADGROUP ... BLOCK 30s</c> em loop: quando o BLOCK expira
/// sem mensagem ele volta e refaz a chamada, o que dá ~2 comandos/min mesmo com o stream
/// vazio — perto de 86k comandos/mês no orçamento de 500k do Upstash, gastos sem processar
/// nada. Hoje o pipeline de replays não passa mais pelo Redis: o publisher grava em
/// ReplayEventOutbox e um processo externo lê o outbox direto do banco.
///
/// Por isso o padrão aqui é DESLIGADO — só liga com a flag explicitamente em "true". Se
/// fosse o contrário, esquecer a chave numa config qualquer religaria o gasto em silêncio.
///
/// Para religar sem deploy, basta a env var (Fly secret) <c>REPLAY_CONSUMER_ENABLED=true</c>.
/// </summary>
public static class ReplayConsumerGate
{
    public const string ConfigKey = "ReplayEvents:ConsumerEnabled";
    public const string EnvVarName = "REPLAY_CONSUMER_ENABLED";

    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
            return false;

        var raw = Environment.GetEnvironmentVariable(EnvVarName)
                  ?? configuration[ConfigKey];

        return bool.TryParse(raw, out var enabled) && enabled;
    }
}
