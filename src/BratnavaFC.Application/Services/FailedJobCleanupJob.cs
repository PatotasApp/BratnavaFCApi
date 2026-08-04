using BratnavaFC.Application.Abstractions;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Apaga jobs falhos com mais de 5 dias.
///
/// Existe porque <c>FailedState.IsFinal</c> é <c>false</c>: job falho é retentável, então o
/// Hangfire nunca lhe aplica o JobExpirationTimeout e ele fica no Redis para sempre
/// carregando mensagem de exceção e stack trace. Era o único crescimento sem limite do
/// storage, e o plano gratuito do Upstash dá 256 MB.
///
/// Usa Delete (que desagua em DeletedState) em vez de um IApplyStateFilter com ExpireJob:
/// o filter expiraria o hash do job mas deixaria o id órfão no sorted set "failed", inflando
/// a contagem de falhas do dashboard sem nada por trás. DeletedState.IsFinal é true, então a
/// transição remove do set E aplica o TTL de 12h — libera o espaço inteiro.
///
/// Só toca Redis. Não acorda o compute do Neon.
/// </summary>
public sealed class FailedJobCleanupJob : IFailedJobCleanupJob
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(5);

    // O monitoring devolve os falhos do mais recente para o mais antigo, então os candidatos
    // estão no fim da lista. Paginamos para não carregar tudo de uma vez.
    private const int PageSize = 200;

    private readonly JobStorage _storage;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<FailedJobCleanupJob> _logger;

    public FailedJobCleanupJob(
        JobStorage storage,
        IBackgroundJobClient jobs,
        ILogger<FailedJobCleanupJob> logger)
    {
        _storage = storage;
        _jobs = jobs;
        _logger = logger;
    }

    public Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("FailedJobCleanupJob: starting");

        var monitoring = _storage.GetMonitoringApi();
        var total = monitoring.FailedCount();
        var cutoff = DateTime.UtcNow - Retention;

        // Primeiro só coletamos os ids a apagar, sem chamar Delete dentro do loop de
        // paginação. FailedJobs lê o sorted set "failed" por posição (rank), e Delete
        // transiciona o job para DeletedState — o que remove o id do set na hora. Apagar
        // durante a paginação encolhe o set sob os nossos pés: o próximo "from" pula
        // exatamente tantas entradas quantas acabamos de apagar, e parte delas nunca é
        // vista. Separar coleta de exclusão elimina esse acoplamento entre índice e estado.
        var toDelete = new List<string>();

        for (var from = 0; from < total; from += PageSize)
        {
            ct.ThrowIfCancellationRequested();

            var page = monitoring.FailedJobs(from, PageSize);
            if (page.Count == 0)
                break;

            foreach (var (jobId, dto) in page)
            {
                // FailedAt nulo significa que o storage não tem a data — sem base para
                // decidir, preservamos.
                if (dto?.FailedAt is not { } failedAt || failedAt >= cutoff)
                    continue;

                toDelete.Add(jobId);
            }
        }

        var deleted = 0;
        foreach (var jobId in toDelete)
        {
            ct.ThrowIfCancellationRequested();

            if (_jobs.Delete(jobId))
                deleted++;
        }

        _logger.LogInformation("FailedJobCleanupJob: completed — deleted {Deleted}/{Total} job(s) falho(s) com mais de {Days} dias", deleted, total, Retention.TotalDays);

        return Task.CompletedTask;
    }
}
