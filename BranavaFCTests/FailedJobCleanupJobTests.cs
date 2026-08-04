using BratnavaFC.Application.Services;
using FluentAssertions;
using Hangfire;
using Hangfire.States;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// FailedState.IsFinal é false, então o Hangfire nunca expira job falho. Sem esta limpeza
/// eles acumulam para sempre no Redis carregando stack trace.
/// </summary>
public class FailedJobCleanupJobTests
{
    private const int Retention = 5;

    [Fact]
    public async Task Deleta_job_falho_mais_antigo_que_a_retencao()
    {
        var jobs = NewClient();
        var job = await RunWith(jobs, ("job-velho", DateTime.UtcNow.AddDays(-(Retention + 1))));

        // Delete é extension method sobre IBackgroundJobClient e o Moq não intercepta
        // extension — ela desagua em ChangeState(id, DeletedState, null). Mesmo desvio
        // documentado em NotificationSchedulerTests.
        jobs.Verify(j => j.ChangeState("job-velho", It.IsAny<DeletedState>(), null), Times.Once);
    }

    [Fact]
    public async Task Preserva_job_falho_dentro_da_janela()
    {
        var jobs = NewClient();
        await RunWith(jobs, ("job-novo", DateTime.UtcNow.AddDays(-(Retention - 1))));

        jobs.Verify(
            j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Ignora_job_sem_FailedAt_sem_lancar()
    {
        var jobs = NewClient();

        // FailedAt é DateTime? — nulo acontece quando o storage não tem o dado.
        var act = async () => await RunWith(jobs, ("job-sem-data", null));

        await act.Should().NotThrowAsync();
        jobs.Verify(
            j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Lista_vazia_e_no_op()
    {
        var jobs = NewClient();
        await RunWith(jobs);

        jobs.Verify(
            j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Apaga_todos_os_expirados_mesmo_com_mais_de_uma_pagina()
    {
        // Reproduz o cenário do bug: um lote maior que uma página (PageSize=200 no job),
        // com os expirados posicionados de forma que apagar-durante-a-paginação encolheria
        // o set "failed" e faria o próximo "from" pular entradas ainda não vistas.
        // 300 não-expirados nos índices 0-299, seguidos por 200 expirados nos índices
        // 300-499 — exatamente a distribuição em que a versão com delete-dentro-do-loop
        // perde a segunda metade dos expirados.
        const int NaoExpiradosCount = 300;
        const int ExpiradosCount = 200;

        var backing = new List<KeyValuePair<string, FailedJobDto>>();
        for (var i = 0; i < NaoExpiradosCount; i++)
            backing.Add(new KeyValuePair<string, FailedJobDto>(
                $"job-novo-{i}",
                new FailedJobDto { FailedAt = DateTime.UtcNow.AddDays(-(Retention - 1)) }));
        for (var i = 0; i < ExpiradosCount; i++)
            backing.Add(new KeyValuePair<string, FailedJobDto>(
                $"job-velho-{i}",
                new FailedJobDto { FailedAt = DateTime.UtcNow.AddDays(-(Retention + 1)) }));

        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()))
            .Callback<string, IState, string>((id, _, _) =>
            {
                // Simula o comportamento real do Redis: DeletedState remove o id do sorted
                // set "failed" imediatamente. Um mock que ignorasse isso não pegaria o bug.
                if (backing.RemoveAll(kv => kv.Key == id) == 0)
                    throw new InvalidOperationException($"job {id} apagado mais de uma vez ou inexistente");
            })
            .Returns(true);

        var monitoring = new Mock<IMonitoringApi>();
        monitoring.Setup(m => m.FailedCount()).Returns(backing.Count);
        monitoring
            .Setup(m => m.FailedJobs(It.IsAny<int>(), It.IsAny<int>()))
            .Returns((int from, int count) => new JobList<FailedJobDto>(
                from >= backing.Count
                    ? Enumerable.Empty<KeyValuePair<string, FailedJobDto>>()
                    : backing.Skip(from).Take(count)));

        var storage = new Mock<JobStorage>();
        storage.Setup(s => s.GetMonitoringApi()).Returns(monitoring.Object);

        var job = new FailedJobCleanupJob(
            storage.Object, jobs.Object, NullLogger<FailedJobCleanupJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);

        for (var i = 0; i < ExpiradosCount; i++)
        {
            jobs.Verify(
                j => j.ChangeState($"job-velho-{i}", It.IsAny<DeletedState>(), null),
                Times.Once);
        }

        for (var i = 0; i < NaoExpiradosCount; i++)
        {
            jobs.Verify(
                j => j.ChangeState($"job-novo-{i}", It.IsAny<IState>(), It.IsAny<string>()),
                Times.Never);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Mock<IBackgroundJobClient> NewClient()
    {
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()))
            .Returns(true);
        return jobs;
    }

    private static async Task<FailedJobCleanupJob> RunWith(
        Mock<IBackgroundJobClient> jobs,
        params (string Id, DateTime? FailedAt)[] failed)
    {
        var list = new JobList<FailedJobDto>(
            failed.Select(f => new KeyValuePair<string, FailedJobDto>(
                f.Id, new FailedJobDto { FailedAt = f.FailedAt })));

        var monitoring = new Mock<IMonitoringApi>();
        monitoring.Setup(m => m.FailedCount()).Returns(failed.Length);
        monitoring.Setup(m => m.FailedJobs(It.IsAny<int>(), It.IsAny<int>())).Returns(list);

        var storage = new Mock<JobStorage>();
        storage.Setup(s => s.GetMonitoringApi()).Returns(monitoring.Object);

        var job = new FailedJobCleanupJob(
            storage.Object, jobs.Object, NullLogger<FailedJobCleanupJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);
        return job;
    }
}
