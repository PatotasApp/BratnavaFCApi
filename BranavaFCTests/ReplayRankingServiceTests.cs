using System.Security.Claims;
using BratnavaFC.Api.Controllers;
using BratnavaFC.Api.Realtime;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Redis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using FluentAssertions;
using Moq;
using Xunit;

namespace BranavaFC.Tests;

public sealed class ReplayRankingServiceTests
{
    private static readonly Guid GroupId = Guid.NewGuid();

    private static ReplayRankingService CreateSut(AppDbContext db)
    {
        var urls = new Mock<IReplayUrlService>();
        urls.Setup(u => u.GeneratePresignedUrl(It.IsAny<string>())).Returns<string>(k => "https://r2/" + k);
        return new ReplayRankingService(db, urls.Object);
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    private static MatchEntity Match(AppDbContext db, DateTime playedAt, int? a = null, int? b = null, Guid? groupId = null)
    {
        var m = new MatchEntity(groupId ?? GroupId, playedAt, "Arena");
        Set(m, nameof(MatchEntity.TeamAGoals), a);
        Set(m, nameof(MatchEntity.TeamBGoals), b);
        db.Matches.Add(m);
        return m;
    }

    private static ReplayClipEntity Clip(AppDbContext db, MatchEntity match, string key = "clip.mp4",
        MatchEventType type = MatchEventType.Gol, DateTimeOffset? recordedAt = null)
    {
        var c = new ReplayClipEntity(match.GroupId, match.Id, "goal-replays", key, "video/mp4", "etag",
            recordedAt ?? new DateTimeOffset(match.PlayedAt, TimeSpan.Zero), type);
        db.ReplayClips.Add(c);
        return c;
    }

    private static void Like(AppDbContext db, ReplayClipEntity clip, int count)
    {
        for (var i = 0; i < count; i++) db.ReplayLikes.Add(new ReplayLikeEntity(clip.Id, Guid.NewGuid()));
    }

    private static void Like(AppDbContext db, ReplayClipEntity clip, UserEntity user, int minutesAgo)
    {
        var like = new ReplayLikeEntity(clip.Id, user.Id);
        Set(like, nameof(ReplayLikeEntity.CreatedAt), DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));
        db.ReplayLikes.Add(like);
    }

    private static UserEntity User(AppDbContext db, string userName, string first, string last)
    {
        var u = new UserEntity(userName, first, last, $"{userName}@x.com", "hash", null, null);
        db.Users.Add(u);
        return u;
    }

    [Fact]
    public async Task Ranking_EmpatesDividemAColocacao_EEntramNoCorte()
    {
        await using var db = DbContextFactory.Create(nameof(Ranking_EmpatesDividemAColocacao_EEntramNoCorte));
        var match = Match(db, new DateTime(2026, 5, 27, 21, 0, 0));
        var c5 = Clip(db, match, "g/m/gols/210000_Gol_FULL.mp4");
        var c3a = Clip(db, match, "g/m/gols/210100_Gol_FULL.mp4");
        var c3b = Clip(db, match, "g/m/gols/210200_Gol_FULL.mp4");
        var c2 = Clip(db, match, "g/m/gols/210300_Gol_FULL.mp4");
        var c1 = Clip(db, match, "g/m/gols/210400_Gol_FULL.mp4");
        Clip(db, match, "g/m/gols/210500_Gol_FULL.mp4"); // sem curtidas: nunca entra
        Like(db, c5, 5); Like(db, c3a, 3); Like(db, c3b, 3); Like(db, c2, 2); Like(db, c1, 1);
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var top2 = (await sut.GetRankingAsync(GroupId, true, null, null, 1, 2, CancellationToken.None)).Data!;
        top2.Items.Select(i => (i.Id, i.Rank)).Should().Equal((c5.Id, 1), (c3a.Id, 2), (c3b.Id, 2));

        var top3 = (await sut.GetRankingAsync(GroupId, true, null, null, 1, 3, CancellationToken.None)).Data!;
        top3.Items.Should().HaveCount(3, "o próximo depois de dois empatados em 2º é o 4º");

        var all = (await sut.GetRankingAsync(GroupId, true, null, null, 1, 0, CancellationToken.None)).Data!;
        all.Items.Select(i => i.Rank).Should().Equal(1, 2, 2, 4, 5);

        var min2 = (await sut.GetRankingAsync(GroupId, true, null, null, 2, 0, CancellationToken.None)).Data!;
        min2.Items.Select(i => i.LikeCount).Should().Equal(5, 3, 3, 2);
    }

    [Fact]
    public async Task Ranking_SoGols_DeixaJogadasDeFora()
    {
        await using var db = DbContextFactory.Create(nameof(Ranking_SoGols_DeixaJogadasDeFora));
        var match = Match(db, new DateTime(2026, 7, 8, 21, 0, 0));
        var jogada = Clip(db, match, type: MatchEventType.Jogada);
        var golA = Clip(db, match, type: MatchEventType.GolTimeA);
        var golB = Clip(db, match, type: MatchEventType.GolTimeB);
        Like(db, jogada, 4); Like(db, golA, 2); Like(db, golB, 1);
        var other = Match(db, new DateTime(2026, 7, 8, 21, 0, 0), groupId: Guid.NewGuid());
        Like(db, Clip(db, other), 9); // outra patota
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var goals = (await sut.GetRankingAsync(GroupId, true, null, null, 1, 0, CancellationToken.None)).Data!;
        goals.Items.Select(i => i.EventType).Should().Equal("GolTimeA", "GolTimeB");

        var everything = (await sut.GetRankingAsync(GroupId, false, null, null, 1, 0, CancellationToken.None)).Data!;
        everything.Items.Select(i => i.Id).Should().Equal(jogada.Id, golA.Id, golB.Id);
    }

    [Fact]
    public async Task Ranking_PeriodoPelaDataDaPartida_ENaoPelaImportacao()
    {
        await using var db = DbContextFactory.Create(nameof(Ranking_PeriodoPelaDataDaPartida_ENaoPelaImportacao));
        var old = Match(db, new DateTime(2025, 12, 20, 21, 0, 0));
        var aug = Match(db, new DateTime(2026, 8, 18, 15, 0, 0));
        var oct = Match(db, new DateTime(2026, 10, 27, 15, 0, 0));
        var cOld = Clip(db, old);
        var cAug = Clip(db, aug);
        // Importado dois dias depois: RecordedAt é o horário da importação.
        var cOct = Clip(db, oct, recordedAt: new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        Like(db, cOld, 1); Like(db, cAug, 1); Like(db, cOct, 1);
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var season = (await sut.GetRankingAsync(GroupId, true, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1), 1, 0, CancellationToken.None)).Data!;
        season.Items.Select(i => i.Id).Should().BeEquivalentTo([cAug.Id, cOct.Id]);
        season.Years.Should().Equal(2026, 2025);

        var since = (await sut.GetRankingAsync(GroupId, true, new DateTime(2026, 10, 27), null, 1, 0, CancellationToken.None)).Data!;
        since.Items.Should().ContainSingle().Which.MatchPlayedAt.Should().Be(new DateTime(2026, 10, 27, 15, 0, 0));
    }

    [Fact]
    public async Task Ranking_PartidaComum_TrazPlacarCoresHorarioEQuemCurtiu()
    {
        await using var db = DbContextFactory.Create(nameof(Ranking_PartidaComum_TrazPlacarCoresHorarioEQuemCurtiu));
        var preto = new TeamColorEntity(GroupId, "Preto", "#000000");
        var laranja = new TeamColorEntity(GroupId, "Laranja", "#FA810A");
        db.TeamColors.AddRange(preto, laranja);
        var match = Match(db, new DateTime(2026, 9, 29, 21, 0, 0), 14, 9);
        Set(match, nameof(MatchEntity.TeamAColorId), laranja.Id);
        Set(match, nameof(MatchEntity.TeamBColorId), preto.Id);
        var clip = Clip(db, match, "g/m/gols/211813_GolTimeA_FULL.mp4", MatchEventType.GolTimeA);

        var matheus = User(db, "matheus", "Matheus", "Silva");
        var luisA = User(db, "luis", "Luis", "Mello");
        var luisB = User(db, "luis2", "Luis", "Souza");
        var semJogador = User(db, "caio", "Caio", "Lima");
        db.Players.Add(new PlayerEntity("Matheus", matheus.Id, GroupId, 3m, false));
        db.Players.Add(new PlayerEntity("Luis Mello", luisA.Id, GroupId, 3m, false));
        db.Players.Add(new PlayerEntity("Luis Souza", luisB.Id, GroupId, 3m, false));
        Like(db, clip, luisA, 30); Like(db, clip, matheus, 40); Like(db, clip, semJogador, 20);
        await db.SaveChangesAsync();

        var item = (await CreateSut(db).GetRankingAsync(GroupId, true, null, null, 1, 0, CancellationToken.None)).Data!.Items.Single();

        item.Rank.Should().Be(1);
        item.LikeCount.Should().Be(3);
        item.ClipTime.Should().Be("21:18:13");
        item.VideoUrl.Should().Be("https://r2/g/m/gols/211813_GolTimeA_FULL.mp4");
        (item.TeamAName, item.TeamAColorHex, item.TeamBName, item.TeamBColorHex).Should().Be(("Laranja", "#FA810A", "Preto", "#000000"));
        (item.TeamAGoals, item.TeamBGoals).Should().Be((14, 9));
        item.LikedBy.Should().Equal("Matheus", "Luis Mello", "Caio"); // ordem das curtidas; Luis repete o primeiro nome
        item.ChampionshipId.Should().BeNull();
    }

    [Fact]
    public async Task Ranking_JogoDeCampeonato_TrazTimesPlacarEDataDoCampeonato()
    {
        await using var db = DbContextFactory.Create(nameof(Ranking_JogoDeCampeonato_TrazTimesPlacarEDataDoCampeonato));
        var azul = new TeamColorEntity(GroupId, "Azul", "#00CBF8");
        db.TeamColors.Add(azul);
        var championship = new ChampionshipEntity(GroupId, "Copa da Patota", new DateTime(2026, 10, 27, 14, 0, 0), "Arena");
        db.Championships.Add(championship);
        var teamA = new ChampionshipTeamEntity(championship.Id, "Azul", azul.Id, 1);
        var teamB = new ChampionshipTeamEntity(championship.Id, "Laranja", null, 2);
        db.ChampionshipTeams.AddRange(teamA, teamB);

        // O jogo guarda o instante UTC em que foi criado; a data exibida é a do campeonato.
        var game = new MatchEntity(GroupId, new DateTime(2026, 10, 27, 18, 30, 0), "Arena");
        game.AssignToChampionship(championship.Id);
        Set(game, nameof(MatchEntity.ChampionshipTeamAId), teamA.Id);
        Set(game, nameof(MatchEntity.ChampionshipTeamBId), teamB.Id);
        Set(game, nameof(MatchEntity.ChampionshipGameOrder), 3);
        Set(game, nameof(MatchEntity.TeamAGoals), 4);
        Set(game, nameof(MatchEntity.TeamBGoals), 3);
        db.Matches.Add(game);
        var clip = Clip(db, game, "g/m/gols/154300_GolTimeA_FULL.mp4", MatchEventType.GolTimeA,
            new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        Like(db, clip, 3);
        await db.SaveChangesAsync();

        var item = (await CreateSut(db).GetRankingAsync(GroupId, true, new DateTime(2026, 10, 27), new DateTime(2026, 10, 28), 1, 0, CancellationToken.None))
            .Data!.Items.Single();

        item.MatchPlayedAt.Should().Be(new DateTime(2026, 10, 27, 14, 0, 0));
        item.ClipTime.Should().Be("15:43:00");
        (item.TeamAName, item.TeamAColorHex, item.TeamBName, item.TeamBColorHex).Should().Be(("Azul", "#00CBF8", "Laranja", null));
        (item.TeamAGoals, item.TeamBGoals).Should().Be((4, 3));
        (item.ChampionshipId, item.ChampionshipName, item.ChampionshipGameOrder).Should().Be((championship.Id, "Copa da Patota", 3));
    }

    [Fact]
    public async Task Ranking_SemClipes_RetornaVazio()
    {
        await using var db = DbContextFactory.Create(nameof(Ranking_SemClipes_RetornaVazio));
        var group = new GroupEntity("Bratnava FC", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var result = await CreateSut(db).GetRankingAsync(group.Id, true, null, null, 1, 10, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
        result.Data!.Years.Should().BeEmpty();
        result.Data!.GroupName.Should().Be("Bratnava FC");
    }

    [Theory]
    [InlineData("g/m/gols/211313_GolTimeA_FULL.mp4", "21:13:13")]
    [InlineData("211313_Gol.mp4", "21:13:13")]
    [InlineData("g/m/gols/5b2c0f3e-1d2a-4f7e-9c1b-2a6d1f0e9a11.mp4", null)]
    [InlineData("g/m/gols/251313_Gol_FULL.mp4", null)]
    [InlineData("g/m/211313/clip.mp4", null)]
    public void ParseClipTime_LeOHorarioDoNomeDoArquivo(string key, string? expected) =>
        ReplayRankingService.ParseClipTime(key).Should().Be(expected);

    [Theory]
    [InlineData("Luis Mello", "Luis Mello")]
    [InlineData("Matheus Silva", "Matheus")]
    [InlineData("Pedro", "Pedro")]
    public void ShortName_PrimeiroNomeSalvoQuandoRepete(string name, string expected) =>
        ReplayRankingService.ShortName(name, ["Luis Mello", "Luis Souza", "Matheus Silva", "Pedro"]).Should().Be(expected);
}

/// <summary>O ranking de curtidos (e o modo telão) é só do admin da patota.</summary>
public sealed class ReplayRankingEndpointTests
{
    private static (MatchesController Sut, Mock<IReplayRankingService> Ranking) CreateSut(
        AppDbContext db, Guid userId, string role)
    {
        var sut = new MatchesController(Mock.Of<IMatchService>(), db, Mock.Of<IMatchEventPublisher>(),
            Mock.Of<IReplayUrlService>(), Mock.Of<IHttpClientFactory>(), Mock.Of<IBetService>(), Mock.Of<IRealtimeNotifier>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)], "Tests")),
                },
            },
        };
        var ranking = new Mock<IReplayRankingService>();
        ranking.Setup(r => r.GetRankingAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReplayRankingDto>.Ok(new ReplayRankingDto("Patota", [], [])));
        return (sut, ranking);
    }

    private static Task<IActionResult> Call(MatchesController sut, Mock<IReplayRankingService> ranking, Guid groupId) =>
        sut.GetLikedReplaysRanking(groupId, ranking.Object, true, null, null, 1, 10, CancellationToken.None);

    [Fact]
    public async Task Ranking_MembroQueNaoEAdminDaPatota_Recebe403()
    {
        await using var db = DbContextFactory.Create(nameof(Ranking_MembroQueNaoEAdminDaPatota_Recebe403));
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        db.Players.Add(new PlayerEntity("Membro", userId, groupId, 3m, false));
        // Admin de OUTRA patota não vale aqui.
        db.GroupAdmins.Add(new GroupAdminEntity { UserId = userId, GroupId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        var (sut, ranking) = CreateSut(db, userId, "User");

        var result = await Call(sut, ranking, groupId);

        result.Should().BeOfType<ForbidResult>();
        ranking.Verify(r => r.GetRankingAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ranking_AdminDaPatota_Recebe200()
    {
        await using var db = DbContextFactory.Create(nameof(Ranking_AdminDaPatota_Recebe200));
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        db.GroupAdmins.Add(new GroupAdminEntity { UserId = userId, GroupId = groupId });
        await db.SaveChangesAsync();
        var (sut, ranking) = CreateSut(db, userId, "User");

        var result = await Call(sut, ranking, groupId);

        result.Should().NotBeOfType<ForbidResult>();
        ((IStatusCodeActionResult)result).StatusCode.Should().Be(200);
        ranking.Verify(r => r.GetRankingAsync(groupId, true, null, null, 1, 10, It.IsAny<CancellationToken>()), Times.Once);
    }
}
