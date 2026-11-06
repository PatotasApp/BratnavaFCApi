using System.Security.Claims;
using BratnavaFC.Api.Controllers;
using BratnavaFC.Api.Realtime;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Conquistas;
using BratnavaFC.Domain.Dtos.Profile;
using BratnavaFC.Domain.Dtos.Share;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Compartilhamento por link: vitrine pública do perfil e da patota, resolução dos links
/// internos (partida, MVP, campeonato, enquete) e checagem de integrante nas rotas internas.
/// </summary>
public sealed class CompartilhamentoTests
{
    // ── Montagem ─────────────────────────────────────────────────────────────

    private static UserEntity User(string name, ProfileVisibility visibility = ProfileVisibility.Public,
        bool showNames = true, bool showZoeira = true)
    {
        var user = new UserEntity(name, name, "Silva", $"{name}@t.com", "hash", "11999999999",
            new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero));
        user.SetProfilePrivacy(visibility, showNames, showZoeira);
        return user;
    }

    private static IImageStorageService Images()
    {
        var images = new Mock<IImageStorageService>();
        images.SetupGet(x => x.PublicBaseUrl).Returns("https://img.test");
        images.Setup(x => x.BuildPublicUrl(It.IsAny<string>())).Returns<string>(k => "https://img.test/" + k);
        return images.Object;
    }

    private static ConquistaDto Marco(string id, int valor) => new() { Id = id, Nome = id, Valor = valor, Categoria = "Marco" };

    /// <summary>Conquistas fictícias: cada jogador com 10 jogos, 3 gols, 1 feito normal e 1 zoeira.</summary>
    private static IConquistaService Conquistas(AppDbContext db)
    {
        var mock = new Mock<IConquistaService>();
        mock.Setup(x => x.GetGroupConquistasAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((groupId, _) => Task.FromResult(new GroupConquistasDto
            {
                GroupId = groupId,
                Players = db.Players.Where(p => p.GroupId == groupId).ToList().Select(p => new PlayerConquistasDto
                {
                    PlayerId = p.Id,
                    UserId = p.UserId,
                    Marcos = [Marco("marco-presenca", 10), Marco("marco-gols", 3)],
                    Eventos =
                    [
                        new ConquistaDto { Id = "hat-trick", Nome = "Hat-trick", Categoria = "Feito", Count = 1 },
                        new ConquistaDto { Id = "frango", Nome = "Frango", Categoria = "Zoeira", Count = 2 },
                    ],
                }).ToList(),
            }));
        return mock.Object;
    }

    private static ProfileService Profiles(AppDbContext db) => new(db, Conquistas(db), Images());

    private static ClaimsPrincipal Principal(Guid? userId, string role = "User") =>
        userId is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()), new Claim(ClaimTypes.Role, role)], "Tests"));

    private static T WithUser<T>(T controller, Guid? userId, string role = "User") where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = Principal(userId, role) },
        };
        return controller;
    }

    private static PublicShareController ShareController(AppDbContext db, Guid? userId, string role = "User") =>
        WithUser(new PublicShareController(db, Profiles(db), new PublicShareService(db, Images())), userId, role);

    private static int Status(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => throw new InvalidOperationException(result.GetType().Name),
    };

    private static T Data<T>(IActionResult result)
    {
        var value = ((ObjectResult)result).Value!;
        return (T)value.GetType().GetProperty("data")!.GetValue(value)!;
    }

    private static string Reason(IActionResult result)
    {
        var value = ((ObjectResult)result).Value!;
        return (string)value.GetType().GetProperty("reason")!.GetValue(value)!;
    }

    private static MatchEntity Match(Guid groupId, MatchStatus status, int a = 0, int b = 0)
    {
        var match = new MatchEntity(groupId, DateTime.UtcNow.AddDays(-1), "Arena");
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.Status))!.SetValue(match, status);
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.TeamAGoals))!.SetValue(match, (int?)a);
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.TeamBGoals))!.SetValue(match, (int?)b);
        return match;
    }

    private sealed record Scenario(GroupEntity Group, UserEntity Member, UserEntity Outsider, MatchEntity Match,
        PollEntity Poll, ChampionshipEntity Championship);

    private static async Task<Scenario> SeedAsync(AppDbContext db, bool publicPage = true)
    {
        var member = User("ana");
        var outsider = User("bia");
        db.Users.AddRange(member, outsider);
        var group = new GroupEntity("Resenha FC", null, member.Id);
        db.Groups.Add(group);
        db.Players.Add(new PlayerEntity("Ana", member.Id, group.Id, 5, isGoalkeeper: false));
        db.Players.Add(new PlayerEntity("Convidado", null, group.Id, 5, isGoalkeeper: false, isGuest: true));
        var settings = new GroupSettingsEntity(group.Id, 10, 20, "Arena Central", DayOfWeek.Thursday, new TimeSpan(20, 0, 0));
        settings.SetPublicPageEnabled(publicPage);
        db.GroupSettings.Add(settings);
        var match = Match(group.Id, MatchStatus.Finalized, 3, 2);
        db.Matches.Add(match);
        db.Matches.Add(Match(group.Id, MatchStatus.Finalized, 1, 1));
        db.Matches.Add(Match(group.Id, MatchStatus.Acceptation, 0, 0));
        var poll = new PollEntity(group.Id, "Churrasco", null, false, false, member.Id, type: "event");
        db.Polls.Add(poll);
        var championship = new ChampionshipEntity(group.Id, "Copa", DateTime.UtcNow, "Arena");
        db.Championships.Add(championship);
        await db.SaveChangesAsync();
        return new Scenario(group, member, outsider, match, poll, championship);
    }

    // ── Perfil: privacidade e bloqueio ───────────────────────────────────────

    [Fact]
    public async Task Perfil_Publico_AbreSemLogin_SoComVitrine()
    {
        await using var db = DbContextFactory.Create(nameof(Perfil_Publico_AbreSemLogin_SoComVitrine));
        var s = await SeedAsync(db);

        var result = await ShareController(db, null).GetProfile(s.Member.Id, default);

        Status(result).Should().Be(200);
        var profile = Data<PublicProfileDto>(result);
        profile.Name.Should().Be("ana Silva");
        profile.Games.Should().Be(10);
        profile.Goals.Should().Be(3);
        profile.Age.Should().NotBeNull();
        profile.PatotasCount.Should().Be(1);
        profile.Patotas.Should().ContainSingle(x => x.GroupName == "Resenha FC");
        // Zoeira nunca entra na vitrine, mesmo com a pessoa mostrando no perfil interno.
        profile.Feitos.Should().ContainSingle(x => x.Id == "hat-trick");
        profile.Feitos.Should().NotContain(x => x.Categoria == "Zoeira");
        // A vitrine não tem números por patota nem dados de contato.
        typeof(PublicProfileDto).GetProperties().Select(p => p.Name).Should()
            .NotContain(["Email", "Phone", "BirthDate", "Conquistas"]);
        typeof(PublicProfilePatotaDto).GetProperties().Select(p => p.Name).Should()
            .BeEquivalentTo(["GroupId", "GroupName"]);
    }

    [Fact]
    public async Task Perfil_SemNomesLiberados_NaoMostraAsPatotas()
    {
        await using var db = DbContextFactory.Create(nameof(Perfil_SemNomesLiberados_NaoMostraAsPatotas));
        var s = await SeedAsync(db);
        s.Member.SetProfilePrivacy(ProfileVisibility.Public, showPatotaNames: false, showZoeiraAchievements: true);
        await db.SaveChangesAsync();

        var profile = Data<PublicProfileDto>(await ShareController(db, null).GetProfile(s.Member.Id, default));

        profile.Patotas.Should().BeEmpty();
        profile.PatotasCount.Should().Be(1);
    }

    [Fact]
    public async Task Perfil_UsuariosAutenticados_PedeLoginParaVisitante_EAbreParaLogado()
    {
        await using var db = DbContextFactory.Create(nameof(Perfil_UsuariosAutenticados_PedeLoginParaVisitante_EAbreParaLogado));
        var s = await SeedAsync(db);
        s.Member.SetProfilePrivacy(ProfileVisibility.AuthenticatedUsers, true, true);
        await db.SaveChangesAsync();

        var anon = await ShareController(db, null).GetProfile(s.Member.Id, default);
        Status(anon).Should().Be(401);
        Reason(anon).Should().Be("login_required");
        ((ObjectResult)anon).Value!.GetType().GetProperty("data").Should().BeNull();

        Status(await ShareController(db, s.Outsider.Id).GetProfile(s.Member.Id, default)).Should().Be(200);
    }

    [Fact]
    public async Task Perfil_SoMinhasPatotas_BloqueiaQuemNaoDivide()
    {
        await using var db = DbContextFactory.Create(nameof(Perfil_SoMinhasPatotas_BloqueiaQuemNaoDivide));
        var s = await SeedAsync(db);
        s.Member.SetProfilePrivacy(ProfileVisibility.SharedPatotas, true, true);
        var colleague = User("caio");
        db.Users.Add(colleague);
        db.Players.Add(new PlayerEntity("Caio", colleague.Id, s.Group.Id, 5, isGoalkeeper: false));
        await db.SaveChangesAsync();

        Reason(await ShareController(db, null).GetProfile(s.Member.Id, default)).Should().Be("login_required");
        var outsider = await ShareController(db, s.Outsider.Id).GetProfile(s.Member.Id, default);
        Status(outsider).Should().Be(403);
        Reason(outsider).Should().Be("shared_patotas");
        Status(await ShareController(db, colleague.Id).GetProfile(s.Member.Id, default)).Should().Be(200);
    }

    [Fact]
    public async Task Perfil_Privado_SoODonoEAdminDaPlataforma()
    {
        await using var db = DbContextFactory.Create(nameof(Perfil_Privado_SoODonoEAdminDaPlataforma));
        var s = await SeedAsync(db);
        s.Member.SetProfilePrivacy(ProfileVisibility.Private, true, true);
        await db.SaveChangesAsync();

        Reason(await ShareController(db, s.Outsider.Id).GetProfile(s.Member.Id, default)).Should().Be("private");
        Reason(await ShareController(db, null).GetProfile(s.Member.Id, default)).Should().Be("private");
        Status(await ShareController(db, s.Member.Id).GetProfile(s.Member.Id, default)).Should().Be(200);
        Status(await ShareController(db, s.Outsider.Id, "Admin").GetProfile(s.Member.Id, default)).Should().Be(200);
    }

    [Fact]
    public async Task Perfil_ComBloqueioNosDoisSentidos_RespondeComoInexistente()
    {
        await using var db = DbContextFactory.Create(nameof(Perfil_ComBloqueioNosDoisSentidos_RespondeComoInexistente));
        var s = await SeedAsync(db);
        db.UserBlocks.Add(new UserBlockEntity(s.Member.Id, s.Outsider.Id));
        await db.SaveChangesAsync();

        Status(await ShareController(db, s.Outsider.Id).GetProfile(s.Member.Id, default)).Should().Be(404);
        Status(await ShareController(db, s.Member.Id).GetProfile(s.Outsider.Id, default)).Should().Be(404);
        // Visitante sem login não tem bloqueio: perfil público continua visível.
        Status(await ShareController(db, null).GetProfile(s.Member.Id, default)).Should().Be(200);
    }

    [Fact]
    public async Task Perfil_Inexistente_404()
    {
        await using var db = DbContextFactory.Create(nameof(Perfil_Inexistente_404));
        Status(await ShareController(db, null).GetProfile(Guid.NewGuid(), default)).Should().Be(404);
    }

    [Fact]
    public async Task Perfil_ContaInativaSemRole_ETratadaComoVisitante()
    {
        await using var db = DbContextFactory.Create(nameof(Perfil_ContaInativaSemRole_ETratadaComoVisitante));
        var s = await SeedAsync(db);
        s.Member.SetProfilePrivacy(ProfileVisibility.AuthenticatedUsers, true, true);
        await db.SaveChangesAsync();
        var sut = ShareController(db, null);
        sut.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, s.Outsider.Id.ToString())], "Tests"));

        Reason(await sut.GetProfile(s.Member.Id, default)).Should().Be("login_required");
    }

    [Fact]
    public async Task PerfilInterno_ContinuaIgual_ComNivelPublico()
    {
        await using var db = DbContextFactory.Create(nameof(PerfilInterno_ContinuaIgual_ComNivelPublico));
        var s = await SeedAsync(db);

        var profile = await Profiles(db).GetPublicProfileAsync(s.Member.Id, s.Outsider.Id, false);

        profile.Patotas.Should().ContainSingle().Which.Games.Should().Be(10);
    }

    // ── Patota ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Patota_PaginaLigada_MostraSoAVitrine()
    {
        await using var db = DbContextFactory.Create(nameof(Patota_PaginaLigada_MostraSoAVitrine));
        var s = await SeedAsync(db);

        var result = Data<PublicGroupResponseDto>(await ShareController(db, null).GetGroup(s.Group.Id, default));

        result.Enabled.Should().BeTrue();
        result.IsMember.Should().BeFalse();
        result.Group!.Name.Should().Be("Resenha FC");
        result.Group.MembersCount.Should().Be(1); // convidado não conta
        result.Group.MatchesPlayed.Should().Be(2); // só finalizadas
        result.Group.GoalsScored.Should().Be(7);
        result.Group.DayOfWeek.Should().Be((int)DayOfWeek.Thursday);
        result.Group.KickoffTime.Should().Be("20:00");
        result.Group.PlaceName.Should().Be("Arena Central");
        result.Group.SinceYear.Should().Be(DateTime.UtcNow.Year);
    }

    [Fact]
    public async Task Patota_PaginaDesligada_NaoSoltaNada_MasAvisaIntegrante()
    {
        await using var db = DbContextFactory.Create(nameof(Patota_PaginaDesligada_NaoSoltaNada_MasAvisaIntegrante));
        var s = await SeedAsync(db, publicPage: false);

        var anon = Data<PublicGroupResponseDto>(await ShareController(db, null).GetGroup(s.Group.Id, default));
        anon.Enabled.Should().BeFalse();
        anon.Group.Should().BeNull();

        var member = Data<PublicGroupResponseDto>(await ShareController(db, s.Member.Id).GetGroup(s.Group.Id, default));
        member.Enabled.Should().BeFalse();
        member.IsMember.Should().BeTrue();
        member.Group.Should().BeNull();
    }

    [Fact]
    public async Task Patota_Inativa_404()
    {
        await using var db = DbContextFactory.Create(nameof(Patota_Inativa_404));
        var s = await SeedAsync(db);
        s.Group.Inactivate();
        await db.SaveChangesAsync();

        Status(await ShareController(db, null).GetGroup(s.Group.Id, default)).Should().Be(404);
    }

    [Fact]
    public async Task Patota_SemLinhaDeConfiguracoes_PaginaLigadaPorPadrao()
    {
        await using var db = DbContextFactory.Create(nameof(Patota_SemLinhaDeConfiguracoes_PaginaLigadaPorPadrao));
        var group = new GroupEntity("Sem Config", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var result = Data<PublicGroupResponseDto>(await ShareController(db, null).GetGroup(group.Id, default));

        result.Enabled.Should().BeTrue();
        result.Group!.DayOfWeek.Should().BeNull();
    }

    [Fact]
    public async Task ConfiguracoesDaPatota_GravaPaginaPublicaDesligada()
    {
        await using var db = DbContextFactory.Create(nameof(ConfiguracoesDaPatota_GravaPaginaPublicaDesligada));
        var group = new GroupEntity("Config", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        var service = new GroupSettingsService(db);

        var dto = new BratnavaFC.Domain.Dtos.UpsertGroupSettingsDto { MinPlayers = 10, MaxPlayers = 20, PublicPageEnabled = false };
        (await service.UpsertAsync(group.Id, dto, default)).Data!.PublicPageEnabled.Should().BeFalse();
        (await service.GetAsync(group.Id, default)).Data!.PublicPageEnabled.Should().BeFalse();

        dto.PublicPageEnabled = null; // não mexe
        (await service.UpsertAsync(group.Id, dto, default)).Data!.PublicPageEnabled.Should().BeFalse();
        dto.PublicPageEnabled = true;
        (await service.UpsertAsync(group.Id, dto, default)).Data!.PublicPageEnabled.Should().BeTrue();
    }

    // ── Links internos ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("match")]
    [InlineData("mvp")]
    [InlineData("championship")]
    [InlineData("poll")]
    public async Task LinkInterno_Integrante_RecebeARota(string kind)
    {
        await using var db = DbContextFactory.Create(nameof(LinkInterno_Integrante_RecebeARota) + kind);
        var s = await SeedAsync(db);
        var id = kind switch { "championship" => s.Championship.Id, "poll" => s.Poll.Id, _ => s.Match.Id };

        var target = Data<ShareTargetDto>(await ShareController(db, s.Member.Id).Resolve(kind, id, default));

        target.Access.Should().Be(ShareAccess.Member);
        target.GroupId.Should().Be(s.Group.Id);
        if (kind is "match" or "mvp") target.MatchStatus.Should().Be("Finalized");
        if (kind == "poll") target.PollType.Should().Be("event");
    }

    [Fact]
    public async Task LinkInterno_Visitante_PedeLogin_ESoVeNomeDaPatotaPublica()
    {
        await using var db = DbContextFactory.Create(nameof(LinkInterno_Visitante_PedeLogin_ESoVeNomeDaPatotaPublica));
        var s = await SeedAsync(db);

        var target = Data<ShareTargetDto>(await ShareController(db, null).Resolve("match", s.Match.Id, default));

        target.Access.Should().Be(ShareAccess.LoginRequired);
        target.GroupName.Should().Be("Resenha FC");
        target.MatchStatus.Should().BeNull();
    }

    [Fact]
    public async Task LinkInterno_NaoIntegrante_NaoVeDetalhes_NemPatotaComPaginaDesligada()
    {
        await using var db = DbContextFactory.Create(nameof(LinkInterno_NaoIntegrante_NaoVeDetalhes_NemPatotaComPaginaDesligada));
        var s = await SeedAsync(db, publicPage: false);

        var target = Data<ShareTargetDto>(await ShareController(db, s.Outsider.Id).Resolve("poll", s.Poll.Id, default));

        target.Access.Should().Be(ShareAccess.NotMember);
        target.GroupId.Should().BeNull();
        target.GroupName.Should().BeNull();
        target.PollType.Should().BeNull();
    }

    [Fact]
    public async Task LinkInterno_GodModeSemJogador_EIntegrante()
    {
        await using var db = DbContextFactory.Create(nameof(LinkInterno_GodModeSemJogador_EIntegrante));
        var s = await SeedAsync(db);

        var target = Data<ShareTargetDto>(await ShareController(db, s.Outsider.Id, "GodMode").Resolve("championship", s.Championship.Id, default));

        target.Access.Should().Be(ShareAccess.Member);
    }

    [Fact]
    public async Task LinkInterno_TipoDesconhecidoOuInexistente_404()
    {
        await using var db = DbContextFactory.Create(nameof(LinkInterno_TipoDesconhecidoOuInexistente_404));
        var s = await SeedAsync(db);
        var sut = ShareController(db, s.Member.Id);

        Status(await sut.Resolve("payment", s.Match.Id, default)).Should().Be(404);
        Status(await sut.Resolve("match", Guid.NewGuid(), default)).Should().Be(404);
        // id de enquete passado como partida não vaza nada
        Status(await sut.Resolve("match", s.Poll.Id, default)).Should().Be(404);
    }

    // ── Rotas internas: só integrante ────────────────────────────────────────

    private static MatchesController Matches(AppDbContext db, Guid userId) =>
        WithUser(new MatchesController(Mock.Of<IMatchService>(), db, Mock.Of<IMatchEventPublisher>(),
            Mock.Of<IReplayUrlService>(), Mock.Of<IHttpClientFactory>(), Mock.Of<IBetService>(), Mock.Of<IRealtimeNotifier>()), userId);

    [Fact]
    public async Task RotasInternasDaPartida_NaoIntegrante_Recebe403()
    {
        await using var db = DbContextFactory.Create(nameof(RotasInternasDaPartida_NaoIntegrante_Recebe403));
        var s = await SeedAsync(db);
        var sut = Matches(db, s.Outsider.Id);

        (await sut.GetMvpAsync(s.Group.Id, s.Match.Id, default)).Should().BeOfType<ForbidResult>();
        (await sut.GetDetails(s.Group.Id, s.Match.Id, default)).Result.Should().BeOfType<ForbidResult>();
        (await sut.GetGoals(s.Group.Id, s.Match.Id, default)).Should().BeOfType<ForbidResult>();
        (await sut.GetPlayerRecentMatches(s.Group.Id, Guid.NewGuid(), 3, false, default)).Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Enquetes_NaoIntegrante_Recebe403_EIntegrantePassa()
    {
        await using var db = DbContextFactory.Create(nameof(Enquetes_NaoIntegrante_Recebe403_EIntegrantePassa));
        var s = await SeedAsync(db);
        var polls = new Mock<IPollService>();
        polls.Setup(x => x.GetPollsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BratnavaFC.Domain.Common.Result<BratnavaFC.Domain.Dtos.PagedResultDto<BratnavaFC.Domain.Dtos.Polls.PollSummaryDto>>
                .Ok(new BratnavaFC.Domain.Dtos.PagedResultDto<BratnavaFC.Domain.Dtos.Polls.PollSummaryDto>()));
        PollsController Sut(Guid userId) =>
            WithUser(new PollsController(polls.Object, db, Mock.Of<IRealtimeNotifier>()), userId);

        (await Sut(s.Outsider.Id).GetPolls(s.Group.Id)).Should().BeOfType<ForbidResult>();
        (await Sut(s.Outsider.Id).GetPoll(s.Group.Id, s.Poll.Id, default)).Should().BeOfType<ForbidResult>();
        (await Sut(s.Member.Id).GetPolls(s.Group.Id)).Should().NotBeOfType<ForbidResult>();
    }
}
