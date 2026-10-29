using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Application.TeamGeneration.Championship;
using BratnavaFC.Application.TeamGeneration.MultiTeam;
using BratnavaFC.Domain.Championships;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Domain.Models;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BranavaFC.Tests;

/// <summary>Fases 1 e 2 do Modo Campeonato: fluxo completo pelo serviço, contra Postgres real.</summary>
[Trait("Category", "Postgres")]
public sealed class CampeonatoFluxoTests : IClassFixture<CampeonatoPostgresFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly CampeonatoPostgresFixture _fixture;

    public CampeonatoFluxoTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FicaQuemGanha_FluxoCompleto_ClassificaVotaEFinaliza()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(FicaQuemGanha_FluxoCompleto_ClassificaVotaEFinaliza));
        var (groupId, players) = await SeedGroupAsync(db, 9);
        var sut = new ChampionshipService(db, new MultiTeamGenerationService(new FakeStatsService([])));

        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Torneio de Natal", Day(), "Arena", null), Ct)).Id;
        Ok(await sut.OpenAcceptationAsync(groupId, id, Ct));
        foreach (var p in players.Take(8))
            Ok(await sut.RespondMyInviteAsync(groupId, id, p.UserId!.Value, accept: true, Ct));
        Ok(await sut.RespondMyInviteAsync(groupId, id, players[8].UserId!.Value, accept: false, Ct));

        var draw = Ok(await sut.DrawTeamsAsync(groupId, id, 3, Ct));
        draw.Teams.Should().HaveCount(3);
        draw.Teams.SelectMany(t => t.PlayerIds).Should().HaveCount(8).And.NotContain(players[8].Id);
        Ok(await sut.DefineTeamsAsync(groupId, id, draw, Ct));

        // Regras travam ao iniciar.
        Ok(await sut.StartAsync(groupId, id, Ct));
        (await sut.UpdateAsync(groupId, id, new UpdateChampionshipDto("X", Day(), "Arena", ChampionshipEntity.DefaultRules), Ct))
            .Success.Should().BeFalse();

        var teams = (await Details(sut, groupId, id)).Teams.Select(t => t.Id).ToList();
        var (a, b, c) = (teams[0], teams[1], teams[2]);

        // Jogo 1: A 2x0 B → fica A, entra C.
        var next = Ok(await sut.GetNextGameAsync(groupId, id, Ct))!;
        (next.TeamAId, next.TeamBId).Should().Be((a, b));
        next.WaitingTeamIds.Should().Equal(c);
        await PlayAsync(db, sut, groupId, id, a, b, goalsA: 2, goalsB: 0);

        next = Ok(await sut.GetNextGameAsync(groupId, id, Ct))!;
        new[] { next.TeamAId, next.TeamBId }.Should().BeEquivalentTo(new[] { a, c });

        // Jogo 2: C 1x0 A → fica C, entra B.
        await PlayAsync(db, sut, groupId, id, next.TeamAId, next.TeamBId,
            goalsA: next.TeamAId == c ? 1 : 0, goalsB: next.TeamBId == c ? 1 : 0);

        // Só um jogo por vez.
        next = Ok(await sut.GetNextGameAsync(groupId, id, Ct))!;
        Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(next.TeamAId, next.TeamBId), Ct));
        (await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(next.TeamAId, next.TeamBId), Ct))
            .Success.Should().BeFalse();
        var current = (await Details(sut, groupId, id)).CurrentGameId!.Value;
        Ok(await sut.EndGameAsync(groupId, id, current, new EndChampionshipGameDto(null), Ct)); // 0x0

        Ok(await sut.FinishAsync(groupId, id, Ct));

        var details = await Details(sut, groupId, id);
        details.Games.Should().HaveCount(3).And.OnlyContain(g => g.Status == ChampionshipGameStatus.Finished);
        details.Standings.Should().HaveCount(3);
        details.TopScorers.Sum(s => s.Goals).Should().Be(3);

        // MVP do campeonato: todos que jogaram votam no artilheiro do time A.
        var teamAPlayers = details.Teams.First(t => t.Id == a).PlayerIds;
        var mvp = details.TopScorers.First().PlayerId;
        foreach (var voter in players.Take(8).Where(p => p.Id != mvp))
            Ok(await sut.VoteAsync(groupId, id, voter.UserId!.Value, new ChampionshipVoteDto(mvp, null), Ct));
        (await sut.VoteAsync(groupId, id, players[8].UserId!.Value, new ChampionshipVoteDto(mvp, null), Ct))
            .Success.Should().BeFalse("quem não jogou não vota");

        Ok(await sut.FinalizeAsync(groupId, id, new FinalizeChampionshipDto(null), Ct));

        details = await Details(sut, groupId, id);
        details.Status.Should().Be(ChampionshipStatus.Finalized);
        details.MvpPlayerIds.Should().Equal(mvp);
        details.Teams.Should().OnlyContain(t => t.FinalPosition.HasValue);
        teamAPlayers.Should().NotBeEmpty();

        var summary = Ok(await sut.ListAsync(groupId, Ct)).Single();
        summary.GameCount.Should().Be(3);
        summary.ChampionTeamName.Should().NotBeNullOrEmpty();
        summary.RuleBadges.Should().Contain("Conta como 1 partida").And.Contain("MVP do campeonato");

        // Fase 0 continua valendo: nada disso aparece no histórico de partidas.
        db.ChangeTracker.Clear();
        (await db.Matches.CountAsync()).Should().Be(0);
        (await db.Matches.IgnoreQueryFilters().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task TodosContraTodosMaisFinal_EmpateNaFinalExigePenaltis_EColocacao()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(TodosContraTodosMaisFinal_EmpateNaFinalExigePenaltis_EColocacao));
        var (groupId, players) = await SeedGroupAsync(db, 8);
        var sut = new ChampionshipService(db, new MultiTeamGenerationService(new FakeStatsService([])));

        var rules = ChampionshipEntity.DefaultRules with
        {
            Format = ChampionshipFormat.RoundRobinWithFinal, WinnerStays = null, MvpMode = MvpMode.PerGame,
            Standings = ChampionshipEntity.DefaultRules.Standings with { Mode = StandingMode.TotalPoints }
        };
        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Com final", Day(), "Arena", rules), Ct)).Id;
        await SetupTeamsAsync(sut, groupId, id, players, teamCount: 4);

        // Fase de grupos: 6 jogos; o "time A" de cada jogo sugerido vence por 1x0.
        for (var i = 0; i < 6; i++)
        {
            var next = Ok(await sut.GetNextGameAsync(groupId, id, Ct))!;
            next.Reason.Should().StartWith("Proximo jogo da tabela");
            await PlayAsync(db, sut, groupId, id, next.TeamAId, next.TeamBId, goalsA: 1, goalsB: 0);
        }

        var standings = (await Details(sut, groupId, id)).Standings;
        var final = Ok(await sut.GetNextGameAsync(groupId, id, Ct))!;
        final.Reason.Should().StartWith("Final");
        new[] { final.TeamAId, final.TeamBId }.Should().BeEquivalentTo(standings.Take(2).Select(s => s.TeamId));

        // Final empatada: precisa dos pênaltis.
        var finalGame = Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(final.TeamAId, final.TeamBId), Ct)).GameId;
        (await sut.EndGameAsync(groupId, id, finalGame, new EndChampionshipGameDto(null), Ct)).Success
            .Should().BeFalse("empate na final precisa de vencedor");
        Ok(await sut.EndGameAsync(groupId, id, finalGame, new EndChampionshipGameDto(final.TeamBId), Ct));
        Ok(await sut.GetNextGameAsync(groupId, id, Ct)).Should().BeNull("tabela e final jogadas");

        Ok(await sut.FinishAsync(groupId, id, Ct));
        Ok(await sut.FinalizeAsync(groupId, id, new FinalizeChampionshipDto(null), Ct));

        var position = (await Details(sut, groupId, id)).Teams.ToDictionary(t => t.Id, t => t.FinalPosition);
        position[final.TeamBId].Should().Be(1, "venceu a final nos pênaltis");
        position[final.TeamAId].Should().Be(2);
        foreach (var other in standings.Skip(2))
            position[other.TeamId].Should().Be(other.Position, "os demais ficam pela classificação");
    }

    [Fact]
    public async Task RegrasInvalidas_SaoRecusadas_ECancelarApagaJogosAbertos()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(RegrasInvalidas_SaoRecusadas_ECancelarApagaJogosAbertos));
        var (groupId, players) = await SeedGroupAsync(db, 6);
        var sut = new ChampionshipService(db, new MultiTeamGenerationService(new FakeStatsService([])));

        var invalid = ChampionshipEntity.DefaultRules with { ResultMode = ResultMode.PerGame };
        var refused = await sut.CreateAsync(groupId, new CreateChampionshipDto("X", Day(), "Arena", invalid), Ct);
        refused.Success.Should().BeFalse();
        refused.Error.Should().Contain("Resultado por jogo");

        var pointsNotFirst = ChampionshipEntity.DefaultRules with
        {
            Standings = ChampionshipEntity.DefaultRules.Standings with
            {
                Tiebreakers = [TiebreakerCriteria.GoalDifference, TiebreakerCriteria.Points]
            }
        };
        (await sut.CreateAsync(groupId, new CreateChampionshipDto("X", Day(), "Arena", pointsNotFirst), Ct))
            .Error.Should().Contain("Pontos tem de ser o primeiro");

        var knockout = ChampionshipEntity.DefaultRules with { Format = (ChampionshipFormat)3 };
        (await sut.CreateAsync(groupId, new CreateChampionshipDto("X", Day(), "Arena", knockout), Ct))
            .Error.Should().Be("Formato invalido.", "o mata-mata foi descartado");

        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Y", Day(), "Arena", null), Ct)).Id;
        var teams = await SetupTeamsAsync(sut, groupId, id, players, teamCount: 3);
        Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(teams[0], teams[1]), Ct));

        Ok(await sut.CancelAsync(groupId, id, Ct));

        db.ChangeTracker.Clear();
        (await db.Matches.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.Championships.SingleAsync()).Status.Should().Be(ChampionshipStatus.Cancelled);
    }

    [Fact]
    public async Task VoltarEtapa_DesfazCadaEtapaEPermiteRefazer()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(VoltarEtapa_DesfazCadaEtapaEPermiteRefazer));
        var (groupId, players) = await SeedGroupAsync(db, 6);
        var sut = new ChampionshipService(db, new MultiTeamGenerationService(new FakeStatsService([])));
        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Volta", Day(), "Arena", null), Ct)).Id;

        (await sut.StepBackAsync(groupId, id, Ct)).Error.Should().Be("Nao ha etapa anterior.");

        // Presença → Criado: a lista sai inteira e pode ser aberta de novo.
        Ok(await sut.OpenAcceptationAsync(groupId, id, Ct));
        Ok(await sut.StepBackAsync(groupId, id, Ct));
        db.ChangeTracker.Clear();
        var details = await Details(sut, groupId, id);
        details.Status.Should().Be(ChampionshipStatus.Created);
        details.Players.Should().BeEmpty();

        // Times → Sorteio → Presença: os times somem, as presenças e os goleiros ficam.
        Ok(await sut.OpenAcceptationAsync(groupId, id, Ct));
        foreach (var p in players)
            Ok(await sut.RespondInviteAsync(groupId, id, p.Id, accept: true, Ct));
        Ok(await sut.DefineTeamsAsync(groupId, id, Ok(await sut.DrawTeamsAsync(groupId, id, 3, Ct)), Ct));
        Ok(await sut.StepBackAsync(groupId, id, Ct));
        db.ChangeTracker.Clear();
        details = await Details(sut, groupId, id);
        details.Status.Should().Be(ChampionshipStatus.TeamDraw);
        details.Teams.Should().BeEmpty();
        Ok(await sut.StepBackAsync(groupId, id, Ct));
        db.ChangeTracker.Clear();
        details = await Details(sut, groupId, id);
        details.Status.Should().Be(ChampionshipStatus.Acceptation);
        details.Players.Should().HaveCount(6).And.OnlyContain(p => p.TeamId == null
            && p.InviteResponse == InviteResponse.Accepted);
        details.Players.Count(p => p.IsGoalkeeper).Should().Be(3);

        // Em andamento → Times, só antes do 1º jogo: as regras voltam a ser editáveis.
        Ok(await sut.DefineTeamsAsync(groupId, id, Ok(await sut.DrawTeamsAsync(groupId, id, 3, Ct)), Ct));
        Ok(await sut.StartAsync(groupId, id, Ct));
        Ok(await sut.StepBackAsync(groupId, id, Ct));
        db.ChangeTracker.Clear();
        (await Details(sut, groupId, id)).RulesLocked.Should().BeFalse();
        Ok(await sut.UpdateAsync(groupId, id, new UpdateChampionshipDto("Volta 2", Day(), "Arena", ChampionshipEntity.DefaultRules), Ct));

        Ok(await sut.StartAsync(groupId, id, Ct));
        var teams = (await Details(sut, groupId, id)).Teams.Select(t => t.Id).ToList();
        await PlayAsync(db, sut, groupId, id, teams[0], teams[1], goalsA: 1, goalsB: 0);
        (await sut.StepBackAsync(groupId, id, Ct)).Error.Should().Be("So e possivel voltar antes do 1º jogo.");

        // Pós-jogo → Em andamento: os votos são descartados.
        Ok(await sut.FinishAsync(groupId, id, Ct));
        var voted = (await Details(sut, groupId, id)).Teams.First(t => t.Id == teams[0]).PlayerIds.First();
        var voter = players.First(p => p.Id != voted);
        Ok(await sut.VoteAsync(groupId, id, voter.UserId!.Value, new ChampionshipVoteDto(voted, null), Ct));
        Ok(await sut.StepBackAsync(groupId, id, Ct));
        db.ChangeTracker.Clear();
        details = Ok(await sut.GetAsync(groupId, id, voter.UserId, Ct));
        details.Status.Should().Be(ChampionshipStatus.InProgress);
        details.MyVotes.Should().BeEmpty();
        (await db.ChampionshipVotes.CountAsync()).Should().Be(0);

        // Finalizado → Pós-jogo: tira colocação e MVP; finalizar de novo funciona.
        Ok(await sut.FinishAsync(groupId, id, Ct));
        Ok(await sut.VoteAsync(groupId, id, voter.UserId!.Value, new ChampionshipVoteDto(voted, null), Ct));
        Ok(await sut.FinalizeAsync(groupId, id, new FinalizeChampionshipDto(null), Ct));
        (await Details(sut, groupId, id)).MvpPlayerIds.Should().Equal(voted);
        Ok(await sut.StepBackAsync(groupId, id, Ct));
        db.ChangeTracker.Clear();
        details = await Details(sut, groupId, id);
        details.Status.Should().Be(ChampionshipStatus.PostGame);
        details.MvpPlayerIds.Should().BeEmpty();
        details.Teams.Should().OnlyContain(t => t.FinalPosition == null);
        Ok(await sut.FinalizeAsync(groupId, id, new FinalizeChampionshipDto(null), Ct));
        (await Details(sut, groupId, id)).Status.Should().Be(ChampionshipStatus.Finalized);
    }

    [Fact]
    public async Task Sorteio_EtapaPropria_GoleirosAntes_MetodosDaPartida_EDepoisTimes()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(Sorteio_EtapaPropria_GoleirosAntes_MetodosDaPartida_EDepoisTimes));
        var (groupId, players) = await SeedGroupAsync(db, 9);
        var colors = await SeedColorsAsync(db, groupId, 3);
        var stats = new FakeStatsService([]);
        var sut = new ChampionshipService(db, new MultiTeamGenerationService(stats),
            teamDraw: new ChampionshipTeamDrawService(stats));
        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Sorteio", Day(), "Arena", null), Ct)).Id;
        Ok(await sut.OpenAcceptationAsync(groupId, id, Ct));
        foreach (var p in players)
            Ok(await sut.RespondInviteAsync(groupId, id, p.Id, accept: true, Ct));

        // Presença → Sorteio, ainda sem times.
        Ok(await sut.OpenTeamDrawAsync(groupId, id, Ct));
        var details = await Details(sut, groupId, id);
        details.Status.Should().Be(ChampionshipStatus.TeamDraw);
        details.Teams.Should().BeEmpty();

        // Goleiros primeiro: o seed marca 0, 1 e 2; troca o 2 pelo 3.
        Ok(await sut.SetPlayerGoalkeeperAsync(groupId, id, players[2].Id, false, Ct));
        Ok(await sut.SetPlayerGoalkeeperAsync(groupId, id, players[3].Id, true, Ct));
        var goalkeepers = new[] { players[0].Id, players[1].Id, players[3].Id };

        // Todos os métodos da partida, com 3 times: cada time sai com um goleiro.
        foreach (var strategy in new[] { StrategyType.Random, StrategyType.Algorithm, StrategyType.GroupByWins })
        {
            var draw = Ok(await sut.DrawTeamOptionsAsync(groupId, id,
                new ChampionshipDrawRequestDto(3, strategy, ColorIds: [colors[2], null, colors[0]]), Ct));
            draw.Options.Should().NotBeEmpty(strategy.ToString());
            foreach (var option in draw.Options)
            {
                option.Teams.Should().HaveCount(3);
                option.Teams.SelectMany(t => t.PlayerIds).Should().HaveCount(9).And.OnlyHaveUniqueItems();
                option.Teams.Select(t => t.GoalkeeperPlayerId).Should().BeEquivalentTo(goalkeepers, strategy.ToString());
                option.Teams[0].ColorId.Should().Be(colors[2]);
                option.Teams[2].ColorId.Should().Be(colors[0]);
                option.Teams[1].ColorId.Should().Be(colors[1], "a cor que faltava sai das restantes");
            }
        }

        // Por perfil sem notas: avisa em vez de montar times pela metade.
        (await sut.DrawTeamOptionsAsync(groupId, id, new ChampionshipDrawRequestDto(3, StrategyType.Profile), Ct))
            .Error.Should().StartWith("Por perfil precisa das notas");

        // Goleiros fora do sorteio: ficam sem time para o admin encaixar.
        var withoutGk = Ok(await sut.DrawTeamOptionsAsync(groupId, id,
            new ChampionshipDrawRequestDto(3, StrategyType.Random, IncludeGoalkeepers: false), Ct)).Options[0];
        withoutGk.UnassignedPlayerIds.Should().BeEquivalentTo(goalkeepers);

        // Com 2 times usa as estratégias da partida (formato de 2 times, direto no serviço de sorteio).
        var pool = players.Select(p => new BratnavaFC.Domain.Dtos.PlayerRequestDto(p.Id, p.Name, goalkeepers.Contains(p.Id))).ToList();
        var two = Ok(await new ChampionshipTeamDrawService(stats).GenerateAsync(pool, 2, StrategyType.Algorithm, true, ct: Ct));
        two[0].Teams.SelectMany(t => t.PlayerIds).Should().HaveCount(9);
        two[0].Teams.Should().OnlyContain(t => t.GoalkeeperPlayerId != null);

        // Manual: times vazios, todos sem time; salvar leva para Times e lá o admin monta.
        var manual = Ok(await sut.DrawTeamOptionsAsync(groupId, id, new ChampionshipDrawRequestDto(3, StrategyType.Manual), Ct)).Options.Single();
        manual.UnassignedPlayerIds.Should().HaveCount(9);
        Ok(await sut.DefineTeamsAsync(groupId, id, new DefineTeamsDto(manual.Teams), Ct));
        details = await Details(sut, groupId, id);
        details.Teams.Should().HaveCount(3).And.OnlyContain(t => t.PlayerIds.Count == 0);
        (await sut.StartAsync(groupId, id, Ct)).Error.Should().Contain("ainda nao tem jogadores");
        for (var i = 0; i < players.Count; i++)
            Ok(await sut.MovePlayerAsync(groupId, id, players[i].Id, details.Teams[i % 3].Id, Ct));
        db.ChangeTracker.Clear();
        details = await Details(sut, groupId, id);
        details.Players.Where(p => p.IsGoalkeeper).Select(p => p.PlayerId).Should().BeEquivalentTo(goalkeepers);

        // Times → Sorteio: os times saem, os goleiros marcados ficam. Sorteio → Presença.
        Ok(await sut.StepBackAsync(groupId, id, Ct));
        db.ChangeTracker.Clear();
        details = await Details(sut, groupId, id);
        details.Status.Should().Be(ChampionshipStatus.TeamDraw);
        details.Teams.Should().BeEmpty();
        details.Players.Where(p => p.IsGoalkeeper).Select(p => p.PlayerId).Should().BeEquivalentTo(goalkeepers);
        Ok(await sut.StepBackAsync(groupId, id, Ct));
        (await Details(sut, groupId, id)).Status.Should().Be(ChampionshipStatus.Acceptation);

        // De volta: sorteia, salva e inicia. Quem está no gol entra no jogo com IsGoalkeeper.
        Ok(await sut.OpenTeamDrawAsync(groupId, id, Ct));
        var best = Ok(await sut.DrawTeamOptionsAsync(groupId, id, new ChampionshipDrawRequestDto(3, StrategyType.Algorithm), Ct)).Options[0];
        Ok(await sut.DefineTeamsAsync(groupId, id, new DefineTeamsDto(best.Teams), Ct));
        Ok(await sut.StartAsync(groupId, id, Ct));
        var teams = (await Details(sut, groupId, id)).Teams;
        var gameId = Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(teams[0].Id, teams[1].Id), Ct)).GameId;
        db.ChangeTracker.Clear();
        var gamePlayers = await db.Matches.IgnoreQueryFilters().Where(m => m.Id == gameId).SelectMany(m => m.Players).ToListAsync();
        gamePlayers.Where(mp => mp.IsGoalkeeper).Select(mp => mp.PlayerId)
            .Should().BeEquivalentTo(best.Teams.Take(2).Select(t => t.GoalkeeperPlayerId!.Value));
    }

    [Fact]
    public async Task EventoVinculado_ComoNaPartida()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(EventoVinculado_ComoNaPartida));
        var (groupId, _) = await SeedGroupAsync(db, 4);
        var sut = new ChampionshipService(db, new MultiTeamGenerationService(new FakeStatsService([])));
        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Com evento", Day(), "Arena", null), Ct)).Id;

        (await sut.SetLinkedPollAsync(groupId, id, Guid.NewGuid(), Ct)).Success.Should().BeFalse();
        var poll = await db.Polls.Select(p => p.Id).FirstOrDefaultAsync();
        if (poll == Guid.Empty) return; // sem votação no seed: o caso de erro acima já cobre a validação
        Ok(await sut.SetLinkedPollAsync(groupId, id, poll, Ct));
    }

    [Fact]
    public async Task Convidado_NovoPeloNomeComoNaPartida_OuJaCadastrado()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(Convidado_NovoPeloNomeComoNaPartida_OuJaCadastrado));
        var (groupId, players) = await SeedGroupAsync(db, 4);
        var sut = new ChampionshipService(db, new MultiTeamGenerationService(new FakeStatsService([])));
        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Convidados", Day(), "Arena", null), Ct)).Id;
        Ok(await sut.OpenAcceptationAsync(groupId, id, Ct));

        // Novo, como na partida: criado na patota como convidado e já confirmado.
        Ok(await sut.AddGuestAsync(groupId, id, new AddChampionshipGuestDto(null, "  Zé da Pelada ", true, 4), Ct));
        db.ChangeTracker.Clear();
        var guest = await db.Players.AsNoTracking().SingleAsync(p => p.Name == "Zé da Pelada");
        (guest.IsGuest, guest.IsGoalkeeper, guest.GroupId).Should().Be((true, true, groupId));
        var details = await Details(sut, groupId, id);
        details.Players.Single(p => p.PlayerId == guest.Id)
            .Should().Match<ChampionshipParticipantDto>(p => p.IsGuest && p.InviteResponse == InviteResponse.Accepted);

        // Já cadastrado (ex.: convidado que volta): pelo id, sem duplicar.
        var outro = new PlayerEntity("Volta", null, groupId, 0m, false, isGuest: true);
        db.Players.Add(outro);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Ok(await sut.AddGuestAsync(groupId, id, new AddChampionshipGuestDto(outro.Id, null), Ct));
        db.ChangeTracker.Clear();
        (await Details(sut, groupId, id)).Players.Should().Contain(p => p.PlayerId == outro.Id);
        (await db.Players.CountAsync(p => p.Name == "Volta")).Should().Be(1);

        (await sut.AddGuestAsync(groupId, id, new AddChampionshipGuestDto(null, " "), Ct)).Error
            .Should().Be("Nome do convidado e obrigatorio.");
        (await sut.AddGuestAsync(groupId, id, new AddChampionshipGuestDto(null, "Nota", false, 6), Ct)).Success
            .Should().BeFalse();
    }

    [Fact]
    public async Task LanceParaCameras_GravaCampeonatoJogoTimeETempo()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(LanceParaCameras_GravaCampeonatoJogoTimeETempo));
        var (groupId, players) = await SeedGroupAsync(db, 6);
        var publisher = new FakeEventPublisher();
        var sut = new ChampionshipService(db, new MultiTeamGenerationService(new FakeStatsService([])), null, publisher);
        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Cameras", Day(), "Arena", null), Ct)).Id;
        var teams = await SetupTeamsAsync(sut, groupId, id, players, teamCount: 3);
        var gameId = Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(teams[0], teams[1]), Ct)).GameId;
        db.ChangeTracker.Clear();
        var startedAt = (await Details(sut, groupId, id)).Games.Single().StartedAt!.Value;

        var at = new DateTimeOffset(DateTime.SpecifyKind(startedAt, DateTimeKind.Utc)).AddSeconds(95);
        var goal = Ok(await sut.PublishGameEventAsync(groupId, id, gameId, players[0].UserId,
            new ChampionshipGameEventRequestDto(MatchEventType.GolTimeB, EventTime: at), Ct));
        goal.TeamId.Should().Be(teams[1]);
        goal.ElapsedSeconds.Should().Be(95);
        goal.QueueId.Should().Be("fila-1");

        var play = Ok(await sut.PublishGameEventAsync(groupId, id, gameId, null,
            new ChampionshipGameEventRequestDto(MatchEventType.Jogada), Ct));
        play.TeamId.Should().BeNull();

        // O recorte vai pelo canal das partidas, com o id do jogo.
        publisher.Calls.Should().HaveCount(2).And.OnlyContain(c => c.GroupId == groupId && c.MatchId == gameId);
        publisher.Calls[0].Type.Should().Be(MatchEventType.GolTimeB);

        db.ChangeTracker.Clear();
        var saved = await db.ChampionshipGameEvents.OrderByDescending(e => e.Type).ToListAsync(); // GolTimeB (4) antes de Jogada (2)
        saved.Should().HaveCount(2).And.OnlyContain(e => e.ChampionshipId == id && e.GameId == gameId);
        saved[0].Should().BeEquivalentTo(new { Type = MatchEventType.GolTimeB, TeamId = (Guid?)teams[1], ElapsedSeconds = (int?)95,
            CreatedByUserId = players[0].UserId });

        (await sut.PublishGameEventAsync(groupId, id, gameId, null,
            new ChampionshipGameEventRequestDto(MatchEventType.Gol), Ct)).Success.Should().BeFalse("só GolTimeA, GolTimeB ou Jogada");
        (await sut.PublishGameEventAsync(groupId, id, Guid.NewGuid(), null,
            new ChampionshipGameEventRequestDto(MatchEventType.Jogada), Ct)).Success.Should().BeFalse("jogo de outro campeonato");

        Ok(await sut.EndGameAsync(groupId, id, gameId, new EndChampionshipGameDto(null), Ct));
        (await sut.PublishGameEventAsync(groupId, id, gameId, null,
            new ChampionshipGameEventRequestDto(MatchEventType.Jogada), Ct)).Error.Should().Be("Esse jogo nao esta em andamento.");
        publisher.Calls.Should().HaveCount(2);
    }

    private sealed class FakeEventPublisher : BratnavaFC.Infrastructure.Redis.IMatchEventPublisher
    {
        public List<(Guid GroupId, Guid MatchId, MatchEventType Type)> Calls { get; } = [];

        public Task<string> PublishAsync(Guid groupId, Guid matchId, MatchEventType type, int secondsBeforeStart,
            int durationSeconds, DateTimeOffset? eventTime = null, CancellationToken ct = default)
        {
            Calls.Add((groupId, matchId, type));
            return Task.FromResult($"fila-{Calls.Count}");
        }
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    private static DateTime Day() => new(2026, 12, 20, 13, 0, 0, DateTimeKind.Utc);

    private static T Ok<T>(Result<T> result)
    {
        result.Success.Should().BeTrue(result.Error);
        return result.Data!;
    }

    private static void Ok(Result result) => result.Success.Should().BeTrue(result.Error);

    private static async Task<ChampionshipDetailsDto> Details(ChampionshipService sut, Guid groupId, Guid id) =>
        Ok(await sut.GetAsync(groupId, id, null, Ct));

    private static async Task<List<Guid>> SetupTeamsAsync(
        ChampionshipService sut, Guid groupId, Guid id, IReadOnlyList<PlayerEntity> players, int teamCount)
    {
        Ok(await sut.OpenAcceptationAsync(groupId, id, Ct));
        foreach (var p in players)
            Ok(await sut.RespondInviteAsync(groupId, id, p.Id, accept: true, Ct));
        Ok(await sut.DefineTeamsAsync(groupId, id, Ok(await sut.DrawTeamsAsync(groupId, id, teamCount, Ct)), Ct));
        Ok(await sut.StartAsync(groupId, id, Ct));
        return (await Details(sut, groupId, id)).Teams.Select(t => t.Id).ToList();
    }

    /// <summary>Cria o jogo, marca os gols com o primeiro jogador de cada time e encerra.</summary>
    private static async Task PlayAsync(
        AppDbContext db, ChampionshipService sut, Guid groupId, Guid id, Guid teamA, Guid teamB, int goalsA, int goalsB)
    {
        var gameId = Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(teamA, teamB), Ct)).GameId;
        var details = await Details(sut, groupId, id);
        var scorerA = details.Teams.Single(t => t.Id == teamA).PlayerIds.First();
        var scorerB = details.Teams.Single(t => t.Id == teamB).PlayerIds.First();

        for (var i = 0; i < goalsA; i++)
            Ok(await sut.AddGoalAsync(groupId, id, gameId, new ChampionshipGoalDto(scorerA, null, false, null), Ct));
        for (var i = 0; i < goalsB; i++)
            Ok(await sut.AddGoalAsync(groupId, id, gameId, new ChampionshipGoalDto(scorerB, null, false, null), Ct));

        Ok(await sut.EndGameAsync(groupId, id, gameId, new EndChampionshipGameDto(null), Ct));
        db.ChangeTracker.Clear();
    }

    private static async Task<List<Guid>> SeedColorsAsync(AppDbContext db, Guid groupId, int count)
    {
        var colors = Enumerable.Range(0, count)
            .Select(i => new TeamColorEntity(groupId, $"Cor {i}", $"#00000{i}"))
            .ToList();
        db.TeamColors.AddRange(colors);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return colors.Select(c => c.Id).ToList();
    }

    private static async Task<(Guid groupId, List<PlayerEntity> players)> SeedGroupAsync(AppDbContext db, int count)
    {
        var group = new GroupEntity("Bratnava FC", null, Guid.NewGuid());
        db.Groups.Add(group);

        var players = new List<PlayerEntity>();
        for (var i = 0; i < count; i++)
        {
            var user = new UserEntity($"jogador{i}", $"Jogador{i}", "Teste", $"jogador{i}@t.com", "hash", null, null);
            db.Users.Add(user);
            players.Add(new PlayerEntity($"Jogador {i}", user.Id, group.Id, 3m + i % 3, isGoalkeeper: i < 3));
        }

        db.Players.AddRange(players);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (group.Id, players);
    }
}
