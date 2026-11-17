using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>Telão do Puskas: autor do gol salvo por clipe, nome curto e validações de grupo.</summary>
public class PuskasServiceTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static RankedReplayDto Ranked(Guid clipId, Guid matchId, int rank, int likes) =>
        new(clipId, matchId, "k.mp4", "https://x/k.mp4", "GolTimeA", DateTimeOffset.UtcNow, "21:10:00", DateTime.UtcNow,
            rank, likes, ["Caio"], "Azul", "#00f", "Branco", "#fff", 3, 2);

    private static (PuskasService svc, AppDbContext db, Guid group, Guid clip, PlayerEntity marlon, PlayerEntity pedro, PlayerEntity pedroB) Setup(int top = 10)
    {
        var db = CreateDb();
        var group = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var clip = new ReplayClipEntity(group, matchId, "goal-replays", "k.mp4", "video/mp4", "etag", DateTimeOffset.UtcNow, MatchEventType.GolTimeA);
        var marlon = new PlayerEntity("Marlon", null, group, 0, false);
        var pedro = new PlayerEntity("Pedro Roweder", null, group, 0, false);
        var pedroB = new PlayerEntity("Pedro Santos", null, group, 0, false);
        db.AddRange(clip, marlon, pedro, pedroB);
        db.SaveChanges();

        var ranking = new Mock<IReplayRankingService>();
        ranking.Setup(r => r.GetRankingAsync(group, true, null, null, 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReplayRankingDto>.Ok(new ReplayRankingDto("Bratnava FC", [2026], [Ranked(clip.Id, matchId, 1, 5)])));
        return (new PuskasService(db, ranking.Object), db, group, clip.Id, marlon, pedro, pedroB);
    }

    [Fact]
    public async Task Sem_autor_o_clipe_vem_sem_nome_e_com_os_jogadores_da_patota_como_opcao()
    {
        var (svc, _, group, _, _, _, _) = Setup();

        var result = await svc.GetAsync(group, 10, null, null, CancellationToken.None);

        var item = Assert.Single(result.Data!.Items);
        Assert.Null(item.ScorerPlayerId);
        Assert.Null(item.ScorerName);
        Assert.Equal(3, item.Candidates.Count); // partida sem escalação: todos da patota
        Assert.Equal(0, result.Data.Identified);
    }

    [Fact]
    public async Task Autor_salvo_aparece_com_nome_curto_ou_completo_se_houver_homonimo()
    {
        var (svc, _, group, clip, marlon, pedro, _) = Setup();

        Assert.True((await svc.SetScorerAsync(group, clip, marlon.Id, CancellationToken.None)).Success);
        var first = (await svc.GetAsync(group, 10, null, null, CancellationToken.None)).Data!;
        Assert.Equal("Marlon", first.Items[0].ScorerName);
        Assert.Equal(1, first.Identified);

        // Troca o autor: continua um registro só por clipe. Há outro "Pedro" na patota -> nome completo.
        Assert.True((await svc.SetScorerAsync(group, clip, pedro.Id, CancellationToken.None)).Success);
        var second = (await svc.GetAsync(group, 10, null, null, CancellationToken.None)).Data!;
        Assert.Equal("Pedro Roweder", second.Items[0].ScorerName);
    }

    [Fact]
    public async Task Limpar_o_autor_remove_o_registro()
    {
        var (svc, db, group, clip, marlon, _, _) = Setup();
        await svc.SetScorerAsync(group, clip, marlon.Id, CancellationToken.None);

        Assert.True((await svc.SetScorerAsync(group, clip, null, CancellationToken.None)).Success);

        Assert.Empty(db.ReplayClipScorers);
    }

    [Fact]
    public async Task Recusa_clipe_de_outra_patota_e_jogador_de_outra_patota()
    {
        var (svc, db, group, clip, marlon, _, _) = Setup();
        var outsider = new PlayerEntity("Forasteiro", null, Guid.NewGuid(), 0, false);
        db.Add(outsider);
        await db.SaveChangesAsync();

        var otherGroupClip = await svc.SetScorerAsync(Guid.NewGuid(), clip, marlon.Id, CancellationToken.None);
        var otherGroupPlayer = await svc.SetScorerAsync(group, clip, outsider.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, otherGroupClip.Status);
        Assert.Equal(ResultStatus.NotFound, otherGroupPlayer.Status);
        Assert.Empty(db.ReplayClipScorers);
    }
}
