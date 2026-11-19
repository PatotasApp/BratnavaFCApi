using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>Votação do Puskas pelo QR code: até 3 gols por nome, um envio por nome, apuração com empate.</summary>
public class PuskasVotingServiceTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record Ctx(PuskasVotingService Svc, AppDbContext Db, Guid Group, PuskasPollEntity Poll, List<ReplayClipEntity> Clips);

    private static Ctx Setup(bool open = true, int clipCount = 5)
    {
        var db = CreateDb();
        var group = Guid.NewGuid();
        var clips = Enumerable.Range(0, clipCount)
            .Select(i => new ReplayClipEntity(group, Guid.NewGuid(), "b", $"k{i}.mp4", "video/mp4", "e", DateTimeOffset.UtcNow, MatchEventType.GolTimeA))
            .ToList();
        var player = new PlayerEntity("Marlon", null, group, 0, false);
        var poll = new PuskasPollEntity(group, clips.Select(c => c.Id));
        poll.SetOpen(open);
        db.AddRange(clips);
        db.Add(player);
        db.AddRange(clips.Select(c => new ReplayClipScorerEntity(group, c.Id, player.Id)));
        db.Add(poll);
        db.SaveChanges();

        var urls = new Mock<IReplayUrlService>();
        urls.Setup(u => u.GeneratePresignedUrl(It.IsAny<string>())).Returns<string>(k => $"https://x/{k}");
        var puskas = new Mock<IPuskasService>();
        return new Ctx(new PuskasVotingService(db, puskas.Object, urls.Object), db, group, poll, clips);
    }

    private static Task<Result> Vote(Ctx c, string name, params int[] idx) =>
        c.Svc.SubmitVoteAsync(c.Poll.Code, name, idx.Select(i => c.Clips[i].Id).ToList(), CancellationToken.None);

    [Fact]
    public async Task Voto_com_ate_3_gols_e_salvo_e_aparece_na_apuracao_com_empate()
    {
        var c = Setup();
        Assert.True((await Vote(c, "Ana", 0, 1, 2)).Success);
        Assert.True((await Vote(c, "Bruno", 1, 2)).Success);
        Assert.True((await Vote(c, "Carla", 2)).Success);

        var results = (await c.Svc.GetResultsAsync(c.Group, CancellationToken.None)).Data!;

        Assert.Equal(3, results.TotalVoters);
        Assert.Equal([3, 2, 1, 0, 0], results.Items.Select(i => i.Votes).ToArray());
        Assert.Equal([1, 2, 3, 4, 4], results.Items.Select(i => i.Rank).ToArray()); // empate pula colocação
        Assert.Equal(["Ana", "Bruno", "Carla"], results.Items[0].Voters);
        Assert.Equal("Marlon", results.Items[0].ScorerName);
    }

    [Fact]
    public async Task Recusa_mais_de_3_nenhum_gol_nome_vazio_e_gol_de_fora_da_votacao()
    {
        var c = Setup();
        Assert.False((await Vote(c, "Ana", 0, 1, 2, 3)).Success);
        Assert.False((await Vote(c, "Ana")).Success);
        Assert.False((await Vote(c, "   ", 0)).Success);
        var outsider = await c.Svc.SubmitVoteAsync(c.Poll.Code, "Ana", [Guid.NewGuid()], CancellationToken.None);
        Assert.False(outsider.Success);
        Assert.Empty(c.Db.PuskasVotes);
    }

    [Fact]
    public async Task Mesmo_nome_nao_vota_duas_vezes_mesmo_com_acento_e_caixa_diferentes()
    {
        var c = Setup();
        Assert.True((await Vote(c, "José  Silva", 0)).Success);

        var again = await Vote(c, "jose silva", 1);

        Assert.False(again.Success);
        Assert.Equal(ResultStatus.Conflict, again.Status);
        Assert.Single(c.Db.PuskasVotes);
    }

    [Fact]
    public async Task Votacao_encerrada_ou_codigo_errado_nao_aceita_voto()
    {
        var closed = Setup(open: false);
        var r = await Vote(closed, "Ana", 0);
        Assert.Equal(ResultStatus.Conflict, r.Status);

        var open = Setup();
        var wrong = await open.Svc.SubmitVoteAsync("codigo-errado", "Ana", [open.Clips[0].Id], CancellationToken.None);
        Assert.Equal(ResultStatus.NotFound, wrong.Status);
    }

    [Fact]
    public async Task Folha_publica_nao_traz_curtidas_nem_votos_e_so_com_codigo_valido()
    {
        var c = Setup();
        var sheet = (await c.Svc.GetSheetAsync(c.Poll.Code, CancellationToken.None)).Data!;
        Assert.Equal(5, sheet.Items.Count);
        Assert.All(sheet.Items, i => Assert.Equal("Marlon", i.ScorerName));
        Assert.Equal(3, sheet.MaxVotes);

        Assert.Equal(ResultStatus.NotFound, (await c.Svc.GetSheetAsync("x", CancellationToken.None)).Status);
    }

    private static PuskasDto Top(Guid group, params (ReplayClipEntity Clip, bool HasScorer)[] items) =>
        new("G", 10, items.Count(i => i.HasScorer), items.Select((i, n) => new PuskasClipDto(
            new RankedReplayDto(i.Clip.Id, i.Clip.MatchId, "k", "u", "GolTimeA", DateTimeOffset.UtcNow, null, null, n + 1, 5, [], null, null, null, null, null, null),
            i.HasScorer ? Guid.NewGuid() : null, i.HasScorer ? "X" : null, [])).ToList());

    [Fact]
    public async Task Abrir_votacao_so_com_os_gols_ja_identificados_e_acrescenta_novos_depois_dos_votos()
    {
        var db = CreateDb();
        var group = Guid.NewGuid();
        var clips = Enumerable.Range(0, 3)
            .Select(i => new ReplayClipEntity(group, Guid.NewGuid(), "b", $"k{i}.mp4", "video/mp4", "e", DateTimeOffset.UtcNow, MatchEventType.GolTimeA)).ToList();
        db.AddRange(clips);
        db.SaveChanges();
        var puskas = new Mock<IPuskasService>();
        var urls = new Mock<IReplayUrlService>();
        var svc = new PuskasVotingService(db, puskas.Object, urls.Object);

        // Nenhum identificado: recusa.
        puskas.Setup(p => p.GetAsync(group, 10, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PuskasDto>.Ok(Top(group, (clips[0], false), (clips[1], false))));
        Assert.False((await svc.OpenPollAsync(group, true, CancellationToken.None)).Success);

        // 2 de 3 identificados: a votação nasce só com eles.
        puskas.Setup(p => p.GetAsync(group, 10, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PuskasDto>.Ok(Top(group, (clips[0], true), (clips[1], true), (clips[2], false))));
        var opened = await svc.OpenPollAsync(group, true, CancellationToken.None);
        Assert.True(opened.Success);
        Assert.Equal(2, opened.Data!.ClipCount);

        // Com voto computado, identificar o 3º só acrescenta; nada sai.
        var poll = await db.PuskasPolls.SingleAsync();
        db.PuskasVotes.Add(new PuskasVoteEntity(poll.Id, clips[0].Id, "Ana", "ana"));
        await db.SaveChangesAsync();
        puskas.Setup(p => p.GetAsync(group, 10, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PuskasDto>.Ok(Top(group, (clips[1], true), (clips[2], true))));
        var again = await svc.OpenPollAsync(group, true, CancellationToken.None);
        Assert.Equal(3, again.Data!.ClipCount);
    }

    [Theory]
    [InlineData(true, new[] { 5, 4, 3, 2, 1 })]
    [InlineData(false, new[] { 1, 2, 3, 4, 5 })]
    public async Task Folha_segue_a_ordem_do_telao_e_traz_o_numero_de_cada_gol(bool countdown, int[] expected)
    {
        var c = Setup();
        c.Poll.SetClips(c.Clips.Select(x => x.Id), countdown);
        await c.Db.SaveChangesAsync();

        var sheet = (await c.Svc.GetSheetAsync(c.Poll.Code, CancellationToken.None)).Data!;

        Assert.Equal(expected, sheet.Items.Select(i => i.Number).ToArray());
        // O número é a posição do gol no ranking (clip 0 = nº 1), independente da ordem de exibição.
        Assert.All(sheet.Items, i => Assert.Equal(c.Clips.FindIndex(x => x.Id == i.ClipId) + 1, i.Number));
    }

    [Fact]
    public void Votacao_criada_no_formato_antigo_continua_legivel()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var poll = new PuskasPollEntity(Guid.NewGuid(), ids);
        typeof(PuskasPollEntity).GetProperty(nameof(PuskasPollEntity.ClipIdsJson))!
            .SetValue(poll, System.Text.Json.JsonSerializer.Serialize(ids));

        Assert.Equal(ids, poll.ClipIds);
        Assert.True(poll.Countdown);
    }
}

