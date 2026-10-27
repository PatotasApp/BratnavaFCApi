using System.Text.Json;
using System.Text.Json.Nodes;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BranavaFC.Tests;

/// <summary>Um Postgres descartável por classe; cada teste cria o próprio banco.</summary>
public sealed class CampeonatoPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    // Criado só no InitializeAsync: Build() já valida o Docker, e falhar no construtor
    // esconde o motivo atrás de "constructor parameters did not have matching fixture data".
    private static PostgreSqlContainer BuildContainer() => new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("bratnava_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => (_postgres = BuildContainer()).StartAsync();
    public Task DisposeAsync() => _postgres is null ? Task.CompletedTask : _postgres.DisposeAsync().AsTask();

    public async Task<AppDbContext> CreateDatabaseAsync(string name)
    {
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = name.ToLowerInvariant()
        }.ConnectionString;

        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        await db.Database.MigrateAsync();
        return db;
    }
}

/// <summary>
/// Fase 0 do Modo Campeonato: jogos de campeonato não podem aparecer em nenhuma
/// estatística, histórico ou lista de partidas comuns. O teste tira um retrato de
/// todas as saídas, cria um campeonato com jogos finalizados e em andamento, e
/// exige que o retrato continue idêntico.
/// </summary>
[Trait("Category", "Postgres")]
public sealed class CampeonatoBlindagemTests : IClassFixture<CampeonatoPostgresFixture>
{
    private static readonly HashSet<string> VolatileKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProjectedAt", "AwardedAt", "CreateDate", "UpdateDate", "GeneratedAt"
    };

    private readonly CampeonatoPostgresFixture _fixture;

    public CampeonatoBlindagemTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task JogosDeCampeonato_NaoAlteramNenhumaEstatisticaNemListagem()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(JogosDeCampeonato_NaoAlteramNenhumaEstatisticaNemListagem));
        var (group, players) = await SeedRegularHistoryAsync(db);

        var before = await SnapshotAsync(db, group.Id, players);

        await SeedChampionshipAsync(db, group.Id, players, finalizedGames: 3, openGames: 1);

        var after = await SnapshotAsync(db, group.Id, players);

        // Lista as saídas que mudaram (comparar uma a uma com Should().Be quebra a
        // formatação do FluentAssertions por causa das chaves do JSON).
        var changed = before.Keys.Where(key => after[key] != before[key]).ToList();
        changed.Should().BeEmpty("jogos de campeonato não podem alterar nenhuma saída comum (mudaram: " + string.Join(", ", changed) + ")");

        // Os jogos continuam no banco: só ficam invisíveis para as consultas comuns.
        (await db.Matches.IgnoreQueryFilters().CountAsync(m => m.ChampionshipId != null)).Should().Be(4);
    }

    /// <summary>
    /// Retrato "golden" das estatísticas de uma patota sem campeonato, gravado antes da Fase 3.
    /// A Fase 3 refatora os leitores para uma fonte única; sem campeonato, nada pode mudar.
    /// Para regravar de propósito: UPDATE_GOLDEN=1 dotnet test --filter EstatisticasSemCampeonato.
    /// </summary>
    [Fact]
    public async Task EstatisticasSemCampeonato_IguaisAoRetratoGolden()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(EstatisticasSemCampeonato_IguaisAoRetratoGolden));
        var (group, players) = await SeedRegularHistoryAsync(db);

        var snapshot = await SnapshotAsync(db, group.Id, players);
        var matches = await db.Matches.AsNoTracking().OrderBy(m => m.PlayedAt).ToListAsync();

        // Ids mudam a cada execução: troca cada Guid por um rótulo estável.
        var labels = new Dictionary<string, string> { [group.Id.ToString()] = "G" };
        foreach (var p in players) labels[p.Id.ToString()] = "P:" + p.Name;
        foreach (var m in matches) labels[m.Id.ToString()] = "M:" + m.PlayedAt.ToString("MM-dd");

        // "conquistas" depende da temporada atual (ano corrente) e fica fora do retrato.
        var normalized = snapshot
            .Where(kv => kv.Key != "conquistas")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key} = {Canonical(GuidPattern.Replace(kv.Value, g => labels.GetValueOrDefault(g.Value, "?")))}")
            .ToList();
        var actual = string.Join("\n", normalized) + "\n";

        var path = Path.Combine(GoldenDir(), "estatisticas-sem-campeonato.txt");
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual);
        }

        var expected = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var diff = actualLines.Where((line, i) => i >= expectedLines.Length || expectedLines[i] != line)
            .Select(line => line.Split(" = ")[0]).ToList();
        diff.Should().BeEmpty("as estatísticas sem campeonato não podem mudar (saídas diferentes: " + string.Join(", ", diff) + ")");
        actualLines.Length.Should().Be(expectedLines.Length);
    }

    /// <summary>
    /// Ordena arrays e chaves de forma canônica: listas ordenadas por Guid mudariam de ordem
    /// a cada execução. O retrato compara valores, não a ordem das listas.
    /// </summary>
    private static string Canonical(string json)
    {
        static JsonNode? Sort(JsonNode? node) => node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => KeyValuePair.Create(kv.Key, Sort(kv.Value?.DeepClone())))),
            JsonArray arr => new JsonArray(arr.Select(n => Sort(n?.DeepClone()))
                .OrderBy(n => n?.ToJsonString() ?? "null", StringComparer.Ordinal).ToArray()),
            _ => node?.DeepClone()
        };
        return Sort(JsonNode.Parse(json))?.ToJsonString() ?? "null";
    }

    private static readonly System.Text.RegularExpressions.Regex GuidPattern =
        new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    /// <summary>Pasta Golden no código-fonte (não no bin), para o arquivo ir para o git.</summary>
    private static string GoldenDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BranavaFC.Tests.csproj")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Projeto de testes não encontrado."), "Golden");
    }

    [Fact]
    public async Task JogosDeCampeonatoAbertos_NaoContamNoLimiteDePartidasSimultaneas()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(JogosDeCampeonatoAbertos_NaoContamNoLimiteDePartidasSimultaneas));
        var (group, players) = await SeedRegularHistoryAsync(db);

        await SeedChampionshipAsync(db, group.Id, players, finalizedGames: 0,
            openGames: BratnavaFC.Domain.Constants.MatchConstants.MaxSimultaneousActiveMatches);

        var sut = CreateMatchService(db);
        var result = await sut.Create(group.Id, new MatchEntity(group.Id, Day(28), "Arena"), CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task ExcluirPatota_ApagaTambemCampeonatosESeusJogos()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(ExcluirPatota_ApagaTambemCampeonatosESeusJogos));
        var (group, players) = await SeedRegularHistoryAsync(db);
        await SeedChampionshipAsync(db, group.Id, players, finalizedGames: 2, openGames: 1);
        db.ChangeTracker.Clear();

        var sut = new GroupService(db, NullLogger<GroupService>.Instance, new Mock<IRepositoryBase<GroupEntity>>().Object,
            Mock.Of<IPushService>(), TestImageStorage.Create());
        await sut.DeleteManyAsync([group.Id], CancellationToken.None);

        (await db.Groups.AnyAsync(g => g.Id == group.Id)).Should().BeFalse();
        (await db.Championships.AnyAsync()).Should().BeFalse();
        (await db.Matches.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.MatchPlayers.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ExcluirJogador_ComHistoricoSoEmCampeonato_ERecusado()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(ExcluirJogador_ComHistoricoSoEmCampeonato_ERecusado));
        var (group, players) = await SeedRegularHistoryAsync(db);

        var onlyChampionship = new PlayerEntity("So Torneio", null, group.Id, 3m, false);
        db.Players.Add(onlyChampionship);
        await db.SaveChangesAsync();

        var championship = new ChampionshipEntity(group.Id, "Torneio", Day(22, 13), "Arena");
        db.Championships.Add(championship);
        await db.SaveChangesAsync();
        await FinalizedMatchAsync(db, group.Id, Day(22, 14), championship.Id, [players[0], onlyChampionship], [players[1], players[2]],
            goals: [(onlyChampionship, null, false)], mvp: onlyChampionship);
        db.ChangeTracker.Clear();

        var repo = new Mock<IRepositoryBase<PlayerEntity>>();
        repo.Setup(r => r.GetByIdIncludingInactiveAsync(onlyChampionship.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(onlyChampionship);
        var sut = new PlayerService(repo.Object, NullLogger<PlayerService>.Instance, db, Mock.Of<IPushService>(),
            Mock.Of<IMatchService>(), TestImageStorage.Create(), Mock.Of<IGroupService>());

        var result = await sut.DeleteAsync(onlyChampionship.Id, CancellationToken.None);

        result.Success.Should().BeFalse("quem jogou campeonato tem histórico e só pode ser desativado");
        repo.Verify(r => r.Remove(It.IsAny<PlayerEntity>()), Times.Never);
    }

    // =========================
    // Retrato das saídas
    // =========================

    private static async Task<Dictionary<string, string>> SnapshotAsync(
        AppDbContext db, Guid groupId, IReadOnlyList<PlayerEntity> players)
    {
        db.ChangeTracker.Clear();
        var ct = CancellationToken.None;
        var snap = new Dictionary<string, string>();

        var matches = CreateMatchService(db);
        snap["historico"] = Json(await matches.GetHistoryAsync(groupId, 100, 0, ct));
        snap["proximas"] = Json(await matches.GetUpcomingAsync(groupId, ct));
        foreach (var p in players)
        {
            snap[$"historico:{p.Name}"] = Json(await matches.GetHistoryAsync(groupId, 100, 0, ct, p.Id));
            snap[$"recentes:{p.Name}"] = Json(await matches.GetPlayerRecentMatchesAsync(groupId, p.Id, 20, ct));
            snap[$"historico-jogador:{p.Name}"] = Json(await matches.GetPlayerHistoryAsync(groupId, p.Id, null, ct));
        }

        var stats = new PlayerStatsService(db);
        snap["visual"] = Json(await stats.GetVisualReportAsync(groupId));
        snap["visual-sem-convidados"] = Json(await stats.GetVisualReportAsync(groupId, includeGuests: false));
        snap["spotlight"] = Json(await stats.GetSpotlightReportAsync(groupId));
        snap["sorteio"] = Json(await stats.EnrichPlayersAsync(
            players.Select(p => new PlayerRequestDto(p.Id, p.Name, p.IsGoalkeeper)).ToList()));

        var teamBuilder = new TeamBuilderService(db);
        snap["team-builder"] = Json(await teamBuilder.GetStatsAsync(
            groupId, players.Take(3).Select(p => p.Id).ToList(), ct));

        var projection = new ConquistaProjectionService(db);
        await projection.RebuildGroupAsync(groupId, ct);
        db.ChangeTracker.Clear();
        snap["contribuicoes"] = Json(await db.PlayerMatchStatContributions.AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .OrderBy(x => x.PlayedAt).ThenBy(x => x.PlayerId)
            .Select(x => new { x.MatchId, x.PlayerId, x.Season, x.Result, x.Games, x.Wins, x.Goals, x.Assists, x.Mvps, x.OwnGoals, x.CleanSheets })
            .ToListAsync(ct));
        snap["projecoes"] = Json(await db.PlayerStatProjections.AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .OrderBy(x => x.PlayerId).ThenBy(x => x.Season)
            .Select(x => new { x.PlayerId, x.Season, x.Games, x.Wins, x.Goals, x.Assists, x.Mvps, x.CleanSheets, x.UnbeatenFiveRuns, x.FiveWinRuns })
            .ToListAsync(ct));
        snap["conquistas"] = Json(await new ConquistaService(db, projection).GetGroupConquistasAsync(groupId, ct));

        return snap;
    }

    private static string Json<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value);
        StripVolatile(node);
        return node?.ToJsonString() ?? "null";
    }

    private static void StripVolatile(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).Where(VolatileKeys.Contains).ToList())
                    obj.Remove(key);
                foreach (var (_, child) in obj)
                    StripVolatile(child);
                break;
            case JsonArray arr:
                foreach (var child in arr)
                    StripVolatile(child);
                break;
        }
    }

    private static MatchService CreateMatchService(AppDbContext db)
        => new(db, new Mock<IRepositoryBase<MatchEntity>>().Object, Mock.Of<IPushService>(), Mock.Of<IReplayUrlService>(),
            Mock.Of<IBetService>(), Mock.Of<INotificationScheduler>(), TestImageStorage.Create());

    // =========================
    // Seeds
    // =========================

    private static DateTime Day(int day, int hour = 22) => new(2026, 10, day, hour, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Patota com 8 jogadores (1 goleiro, 1 convidado) e 6 partidas finalizadas
    /// cobrindo gol, assistência, gol contra, MVP, empate e jogador que não apareceu.
    /// Mais 1 partida aberta, para a lista de próximas partidas.
    /// </summary>
    private static async Task<(GroupEntity group, List<PlayerEntity> players)> SeedRegularHistoryAsync(AppDbContext db)
    {
        var group = new GroupEntity("Bratnava FC", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var players = new List<PlayerEntity>
        {
            new("Goleiro", null, group.Id, 3m, isGoalkeeper: true),
            new("Artilheiro", null, group.Id, 5m, false),
            new("Armador", null, group.Id, 4m, false),
            new("Zagueiro", null, group.Id, 3m, false),
            new("Volante", null, group.Id, 3m, false),
            new("Ponta", null, group.Id, 4m, false),
            new("Lateral", null, group.Id, 2m, false),
            new("Convidado", null, group.Id, 3m, false, isGuest: true),
        };
        db.Players.AddRange(players);
        await db.SaveChangesAsync();

        var (gk, art, arm, zag, vol, pon, lat, conv) =
            (players[0], players[1], players[2], players[3], players[4], players[5], players[6], players[7]);

        await FinalizedMatchAsync(db, group.Id, Day(1), null, [gk, art, arm, zag], [vol, pon, lat, conv],
            goals: [(art, arm, false), (art, null, false), (pon, lat, false)], mvp: art);
        await FinalizedMatchAsync(db, group.Id, Day(4), null, [art, vol, lat, conv], [gk, arm, zag, pon],
            goals: [(arm, pon, false), (zag, null, true)], mvp: arm);
        await FinalizedMatchAsync(db, group.Id, Day(8), null, [gk, art, pon, lat], [arm, zag, vol, conv],
            goals: [], mvp: gk);
        await FinalizedMatchAsync(db, group.Id, Day(11), null, [gk, art, arm, vol], [zag, pon, lat, conv],
            goals: [(art, arm, false), (art, arm, false), (art, null, false), (lat, pon, false)], mvp: art,
            didNotPlay: [conv]);
        await FinalizedMatchAsync(db, group.Id, Day(15), null, [zag, pon, lat, art], [gk, arm, vol, conv],
            goals: [(vol, arm, false)], mvp: vol);
        await FinalizedMatchAsync(db, group.Id, Day(18), null, [gk, zag, vol, pon], [art, arm, lat, conv],
            goals: [(pon, zag, false), (art, arm, false)], mvp: pon);

        var open = new MatchEntity(group.Id, Day(29), "Arena");
        db.Matches.Add(open);
        await db.SaveChangesAsync();

        return (group, players);
    }

    private static async Task SeedChampionshipAsync(
        AppDbContext db, Guid groupId, IReadOnlyList<PlayerEntity> players, int finalizedGames, int openGames)
    {
        // O retrato limpa o ChangeTracker; recarrega os jogadores para não reinserir o grafo.
        var tracked = await db.Players.Where(p => p.GroupId == groupId).ToDictionaryAsync(p => p.Id);
        players = players.Select(p => tracked[p.Id]).ToList();

        var championship = new ChampionshipEntity(groupId, "Torneio de Teste", Day(22, 13), "Arena");
        db.Championships.Add(championship);
        await db.SaveChangesAsync();

        var (gk, art, arm, zag, vol, pon) = (players[0], players[1], players[2], players[3], players[4], players[5]);

        for (var i = 0; i < finalizedGames; i++)
        {
            await FinalizedMatchAsync(db, groupId, Day(22, 14 + i), championship.Id, [gk, art, arm], [zag, vol, pon],
                goals: [(art, arm, false), (art, null, false), (art, arm, false), (zag, vol, false)], mvp: art);
        }

        for (var i = 0; i < openGames; i++)
        {
            var open = new MatchEntity(groupId, Day(22, 18 + i), "Arena");
            open.AssignToChampionship(championship.Id);
            db.Matches.Add(open);
        }
        await db.SaveChangesAsync();
    }

    private static async Task FinalizedMatchAsync(
        AppDbContext db, Guid groupId, DateTime playedAt, Guid? championshipId,
        IReadOnlyList<PlayerEntity> teamA, IReadOnlyList<PlayerEntity> teamB,
        IReadOnlyList<(PlayerEntity scorer, PlayerEntity? assist, bool ownGoal)> goals,
        PlayerEntity mvp,
        IReadOnlyList<PlayerEntity>? didNotPlay = null)
    {
        var all = teamA.Concat(teamB).ToList();
        var match = new MatchEntity(groupId, playedAt, "Arena");
        if (championshipId.HasValue) match.AssignToChampionship(championshipId.Value);

        // A partida inteira é montada em memória e salva de uma vez: filhos com Guid já
        // preenchido, adicionados a uma entidade rastreada, viram UPDATE no Postgres.
        foreach (var p in all)
            match.AddPlayer(new MatchPlayerEntity(p.Id), p);

        match.OpenAcceptation();
        foreach (var p in all) match.AcceptInvite(p.Id);
        match.GoToMatchMaking();
        match.AssignTeams(teamA.Select(p => p.Id).ToArray(), teamB.Select(p => p.Id).ToArray());
        match.Start();
        match.End();
        match.GoToPostGame();

        var byPlayer = match.Players.ToDictionary(mp => mp.PlayerId);
        foreach (var (scorer, assist, ownGoal) in goals)
            match.AddGoalByMatchPlayer(byPlayer[scorer.Id].Id, assist is null ? null : byPlayer[assist.Id].Id, null, ownGoal);
        if (goals.Count == 0)
            match.SetScore(0, 0);

        foreach (var voter in all.Where(p => !p.IsGuest && p.Id != mvp.Id))
            match.CreateVote(byPlayer[voter.Id].Id, byPlayer[mvp.Id].Id);

        foreach (var p in didNotPlay ?? [])
            byPlayer[p.Id].SetDidNotPlay(true);

        match.FinalizeByVotes();
        db.Matches.Add(match);
        await db.SaveChangesAsync();
    }
}

/// <summary>
/// Trava de arquitetura da Fase 0: ignorar o filtro de jogos de campeonato só é
/// permitido em pontos revisados. Um IgnoreQueryFilters novo em outro arquivo pode
/// fazer jogos de campeonato vazarem para estatísticas sem respeitar as regras.
/// </summary>
public sealed class CampeonatoIgnoreQueryFiltersTests
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/BratnavaFC.Application/Services/GroupService.cs",   // exclusão de patota
        "src/BratnavaFC.Application/Services/PlayerService.cs",  // exclusão de jogador
        "src/BratnavaFC.Application/Services/ChampionshipService.cs", // única porta dos jogos (GamesQuery)
        // GetByIdIncludingInactiveAsync (anterior à Fase 0): hoje só chamado para patota,
        // jogador e usuário. Não usar com MatchEntity sem rever este teste.
        "src/BratnavaFC.Infrastructure/Repositories/RepositoryBase.cs",
        "src/BratnavaFC.Infrastructure/Data/Configurations/MatchEntityConfiguration.cs", // só no comentário do filtro
    };

    [Fact]
    public void IgnoreQueryFilters_SoAparecemEmArquivosRevisados()
    {
        var root = FindRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("IgnoreQueryFilters"))
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !Allowed.Contains(f))
            .ToList();

        offenders.Should().BeEmpty(
            "IgnoreQueryFilters expõe jogos de campeonato; revise o uso e inclua o arquivo na lista permitida (encontrado em: "
            + string.Join(", ", offenders) + ")");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BratnavaFC.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("BratnavaFC.sln não encontrado.");
    }
}
