using System.Text;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BranavaFC.Tests;

/// <summary>
/// Contrato dos substitutos usados quando a dependência está desabilitada no ambiente:
/// escrita finge sucesso, leitura falha explicitamente.
/// </summary>
public class NoOpInfrastructureTests
{
    private const string Environment = "Development";

    // ── Redis ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoOpRedisConnectionProvider_devolve_null_sem_abrir_conexao()
    {
        var sut = new NoOpRedisConnectionProvider(
            NullLogger<NoOpRedisConnectionProvider>.Instance,
            Environment);

        var connection = await sut.GetConnectionAsync(CancellationToken.None);

        // null é o contrato que IRedisConnectionProvider já usa para "indisponível",
        // então todos os consumidores existentes tratam esse caso.
        connection.Should().BeNull();
    }

    [Fact]
    public async Task NoOpMatchEventPublisher_devolve_id_sintetico_sem_tocar_no_banco()
    {
        var sut = new NoOpMatchEventPublisher(
            NullLogger<NoOpMatchEventPublisher>.Instance,
            Environment);

        var id = await sut.PublishAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            MatchEventType.Gol,
            secondsBeforeStart: 10,
            durationSeconds: 15,
            eventTime: DateTimeOffset.UtcNow,
            ct: CancellationToken.None);

        // Não recebe AppDbContext no construtor, então não há como gravar no outbox.
        id.Should().StartWith("dev:");
    }

    // ── Cloudflare R2 ────────────────────────────────────────────────────────

    [Fact]
    public void NoOpReplayUrlService_reporta_IsEnabled_falso()
    {
        CreateStorage().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void NoOpReplayUrlService_gera_url_sem_lancar()
    {
        // Chamado dentro de projeções de lista de clipes: uma exceção aqui derrubaria
        // o endpoint inteiro em vez de degradar um item.
        var url = CreateStorage().GeneratePresignedUrl("gols/clip.mp4");

        url.Should().Be("https://storage-disabled.local/goal-replays/gols/clip.mp4");
    }

    [Fact]
    public async Task NoOpReplayUrlService_upload_finge_sucesso_e_drena_o_stream()
    {
        var content = new MemoryStream(Encoding.UTF8.GetBytes("conteudo de video"));

        var etag = await CreateStorage().UploadObjectAsync(
            "gols/clip.mp4", content, "video/mp4", CancellationToken.None);

        etag.Should().Be("\"storage-disabled\"");
        content.Position.Should().Be(content.Length, "o corpo do request precisa ser drenado");
    }

    [Fact]
    public async Task NoOpReplayUrlService_delete_finge_sucesso()
    {
        var act = () => CreateStorage().DeleteObjectAsync("gols/clip.mp4", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NoOpReplayUrlService_leitura_lanca_DependencyDisabledException()
    {
        var act = () => CreateStorage().GetObjectStreamAsync("gols/clip.mp4", CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DependencyDisabledException>();
        exception.Which.Dependency.Should().Be("Cloudflare R2");
        exception.Which.EnvironmentName.Should().Be(Environment);
    }

    private static NoOpReplayUrlService CreateStorage()
        => new(NullLogger<NoOpReplayUrlService>.Instance, Environment);
}
