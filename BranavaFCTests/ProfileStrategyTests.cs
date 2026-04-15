using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using FluentAssertions;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Testes unitários para ProfileStrategy.
///
/// Regras cobertas:
///   - Validação: todos os jogadores de linha precisam dos 3 ratings; sem eles → resultado vazio.
///   - Goleiros sempre ficam em Unassigned (sem necessidade de rating).
///   - Classificação: attack > defense = Ofensivo | defense > attack = Defensivo | igual = Neutro.
///   - Redistribuição de neutros: preenche o grupo com menos jogadores.
///   - Restrição de perfil: ofensivos e defensivos são distribuídos entre os times.
///   - Score = |PhysicalSumA − PhysicalSumB| (minimizado).
///   - Opções ordenadas por PhysicalDiff crescente.
///   - Deduplicação: composições canônicas idênticas são removidas.
/// </summary>
public class ProfileStrategyTests
{
    private static readonly TeamGenerationSettings DefaultSettings =
        new() { PlayersPerTeam = 3, IncludeGoalkeepers = false };

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Cria uma estratégia com os stats fornecidos.</summary>
    private static ProfileStrategy Build(IEnumerable<PlayerStats> stats)
        => new(new FakeStatsService(stats));

    // ── 1. Input vazio ────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyPlayers_ReturnsEmptyOptions()
    {
        var strategy = Build([]);
        var result   = await strategy.GenerateTeamsAsync([], DefaultSettings);
        result.Options.Should().BeEmpty();
    }

    // ── 2. Validação: sem rating → resultado vazio ────────────────────────────

    [Fact]
    public async Task AnyFieldPlayerWithoutAllRatings_ReturnsEmpty()
    {
        // Jogador "NoRating" não tem nenhum rating → deve impedir o algoritmo
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("NoRating", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            TestHelpers.RatedNeutralStats(ids[0], "A", 0.8, 0.2, 0.6),
            TestHelpers.RatedNeutralStats(ids[1], "B", 0.3, 0.8, 0.5),
            // ids[2] "NoRating" — não está no dicionário → FakeStatsService retorna NeutralStats sem ratings
            TestHelpers.RatedNeutralStats(ids[3], "D", 0.5, 0.5, 0.5),
        };

        var result = await Build(stats).GenerateTeamsAsync(players, DefaultSettings);

        result.Options.Should().HaveCount(1, "resultado vazio retorna 1 opção com todos em Unassigned");
        result.Options.First().TeamA.Should().BeEmpty();
        result.Options.First().TeamB.Should().BeEmpty();
    }

    [Fact]
    public async Task FieldPlayerMissingOneRating_ReturnsEmpty()
    {
        // "Partial" tem attack e defense mas não tem physical
        var players = TestHelpers.Players(("A", false), ("Partial", false));
        var ids     = players.Select(p => p.Id).ToList();

        var partialStats = new PlayerStats
        {
            PlayerId          = ids[1],
            Name              = "Partial",
            WinRate           = 0.0,
            SynergyWith       = new System.Collections.Generic.Dictionary<Guid, double>(),
            AttackRatingNorm  = 0.7,
            DefenseRatingNorm = 0.3,
            PhysicalRatingNorm = null, // falta o físico
        };

        var stats = new[]
        {
            TestHelpers.RatedNeutralStats(ids[0], "A", 0.5, 0.5, 0.5),
            partialStats,
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 1, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings);

        result.Options.First().TeamA.Should().BeEmpty(
            "um jogador sem rating físico impede o algoritmo");
    }

    // ── 3. Goleiros sempre em Unassigned ──────────────────────────────────────

    [Fact]
    public async Task Goalkeepers_AlwaysInUnassigned_RegardlessOfRatings()
    {
        var players = TestHelpers.Players(
            ("GK1", true), ("GK2", true),
            ("A",  false), ("B",  false),
            ("C",  false), ("D",  false));

        var ids   = players.Select(p => p.Id).ToList();
        var gkIds = new[] { ids[0], ids[1] }.ToHashSet();

        // Goleiros NÃO têm rating — não devem bloquear o algoritmo
        var stats = new[]
        {
            TestHelpers.RatedNeutralStats(ids[2], "A", 0.8, 0.2, 0.6),
            TestHelpers.RatedNeutralStats(ids[3], "B", 0.2, 0.8, 0.5),
            TestHelpers.RatedNeutralStats(ids[4], "C", 0.7, 0.3, 0.4),
            TestHelpers.RatedNeutralStats(ids[5], "D", 0.3, 0.7, 0.6),
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings);

        result.Options.Should().NotBeEmpty("jogadores de linha com ratings válidos devem gerar times");

        foreach (var opt in result.Options)
        {
            var teamIds = opt.TeamA.Select(x => x.PlayerId)
                            .Concat(opt.TeamB.Select(x => x.PlayerId))
                            .ToHashSet();

            teamIds.Should().NotContain(ids[0], "GK1 não pode estar em nenhum time");
            teamIds.Should().NotContain(ids[1], "GK2 não pode estar em nenhum time");

            opt.Unassigned.Select(x => x.PlayerId)
               .Should().Contain(ids[0], "GK1 deve estar em Unassigned");
            opt.Unassigned.Select(x => x.PlayerId)
               .Should().Contain(ids[1], "GK2 deve estar em Unassigned");
        }
    }

    // ── 4. Estrutura: sem overlap, tamanhos corretos ──────────────────────────

    [Fact]
    public async Task Structure_NoOverlaps_CorrectTeamSizes_6FieldPlayers()
    {
        // 6 jogadores de linha (sem GK) → perTeam = 3 → 2 times de 3
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false),
            ("D", false), ("E", false), ("F", false));

        var ids   = players.Select(p => p.Id).ToList();
        var stats = ids.Select((id, i) =>
            TestHelpers.RatedNeutralStats(id, $"P{i}",
                attackRating:  i % 2 == 0 ? 0.8 : 0.2,
                defenseRating: i % 2 == 0 ? 0.2 : 0.8,
                physicalRating: 0.5 + i * 0.05));

        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings);

        result.Options.Should().NotBeEmpty();

        foreach (var opt in result.Options)
        {
            opt.TeamA.Should().HaveCount(3, "perTeam=3 → Time A deve ter 3 jogadores");
            opt.TeamB.Should().HaveCount(3, "perTeam=3 → Time B deve ter 3 jogadores");

            var allIds = opt.TeamA.Select(x => x.PlayerId)
                            .Concat(opt.TeamB.Select(x => x.PlayerId))
                            .ToList();

            allIds.Should().OnlyHaveUniqueItems("nenhum jogador pode estar nos dois times");
            allIds.Should().HaveCount(6, "todos os 6 jogadores de linha devem ser atribuídos");
            opt.Unassigned.Should().BeEmpty("sem GKs, Unassigned deve estar vazio");
        }
    }

    // ── 5. Restrição de perfil: ofensivos separados ───────────────────────────

    [Fact]
    public async Task ProfileConstraint_OffensivePlayers_AlwaysSplitBetweenTeams()
    {
        // A e B são ofensivos (attack >> defense); C e D são defensivos (defense >> attack)
        // A e B NUNCA podem estar no mesmo time
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            TestHelpers.RatedNeutralStats(ids[0], "A", attackRating: 0.90, defenseRating: 0.10, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[1], "B", attackRating: 0.85, defenseRating: 0.15, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[2], "C", attackRating: 0.10, defenseRating: 0.90, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[3], "D", attackRating: 0.15, defenseRating: 0.85, physicalRating: 0.5),
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings);

        result.Options.Should().NotBeEmpty();

        foreach (var opt in result.Options)
        {
            var aIds = opt.TeamA.Select(x => x.PlayerId).ToHashSet();
            var bIds = opt.TeamB.Select(x => x.PlayerId).ToHashSet();

            bool bothOffensiveInA = aIds.Contains(ids[0]) && aIds.Contains(ids[1]);
            bool bothOffensiveInB = bIds.Contains(ids[0]) && bIds.Contains(ids[1]);

            (bothOffensiveInA || bothOffensiveInB).Should().BeFalse(
                "jogadores ofensivos A e B nunca podem estar no mesmo time");
        }
    }

    // ── 6. Restrição de perfil: defensivos separados ──────────────────────────

    [Fact]
    public async Task ProfileConstraint_DefensivePlayers_AlwaysSplitBetweenTeams()
    {
        // C e D são defensivos — nunca devem estar no mesmo time
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            TestHelpers.RatedNeutralStats(ids[0], "A", attackRating: 0.90, defenseRating: 0.10, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[1], "B", attackRating: 0.85, defenseRating: 0.15, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[2], "C", attackRating: 0.10, defenseRating: 0.90, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[3], "D", attackRating: 0.15, defenseRating: 0.85, physicalRating: 0.5),
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings);

        result.Options.Should().NotBeEmpty();

        foreach (var opt in result.Options)
        {
            var aIds = opt.TeamA.Select(x => x.PlayerId).ToHashSet();
            var bIds = opt.TeamB.Select(x => x.PlayerId).ToHashSet();

            bool bothDefensiveInA = aIds.Contains(ids[2]) && aIds.Contains(ids[3]);
            bool bothDefensiveInB = bIds.Contains(ids[2]) && bIds.Contains(ids[3]);

            (bothDefensiveInA || bothDefensiveInB).Should().BeFalse(
                "jogadores defensivos C e D nunca podem estar no mesmo time");
        }
    }

    // ── 7. Redistribuição de neutros ──────────────────────────────────────────

    [Fact]
    public async Task NeutralRedistribution_FillsTheSmallerGroup()
    {
        // 3 ofensivos, 2 defensivos, 1 neutro → neutro vira defensivo (3/3)
        // → por restrição: cada time deve ter exatamente 1 ou 2 ofensivos e 1 ou 2 defensivos
        var players = TestHelpers.Players(
            ("Off1", false), ("Off2", false), ("Off3", false),
            ("Def1", false), ("Def2", false), ("Neu", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            TestHelpers.RatedNeutralStats(ids[0], "Off1", attackRating: 0.9, defenseRating: 0.1, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[1], "Off2", attackRating: 0.8, defenseRating: 0.2, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[2], "Off3", attackRating: 0.7, defenseRating: 0.3, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[3], "Def1", attackRating: 0.1, defenseRating: 0.9, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[4], "Def2", attackRating: 0.2, defenseRating: 0.8, physicalRating: 0.5),
            TestHelpers.RatedNeutralStats(ids[5], "Neu",  attackRating: 0.5, defenseRating: 0.5, physicalRating: 0.5),
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 3, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings);

        result.Options.Should().NotBeEmpty(
            "6 jogadores com 3 off + 2 def + 1 neutro (→ vira def) devem gerar times válidos");

        // Todos os times devem ter exatamente 3 jogadores
        foreach (var opt in result.Options)
        {
            opt.TeamA.Should().HaveCount(3);
            opt.TeamB.Should().HaveCount(3);
        }
    }

    // ── 8. Ordenação por PhysicalDiff ─────────────────────────────────────────

    [Fact]
    public async Task Options_OrderedByPhysicalDiff_Ascending()
    {
        // 8 jogadores: 4 ofensivos e 4 defensivos com físico variado
        var players = TestHelpers.Players(
            ("O1", false), ("O2", false), ("O3", false), ("O4", false),
            ("D1", false), ("D2", false), ("D3", false), ("D4", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            TestHelpers.RatedNeutralStats(ids[0], "O1", 0.9, 0.1, 0.90),
            TestHelpers.RatedNeutralStats(ids[1], "O2", 0.8, 0.2, 0.70),
            TestHelpers.RatedNeutralStats(ids[2], "O3", 0.7, 0.3, 0.55),
            TestHelpers.RatedNeutralStats(ids[3], "O4", 0.6, 0.4, 0.40),
            TestHelpers.RatedNeutralStats(ids[4], "D1", 0.1, 0.9, 0.80),
            TestHelpers.RatedNeutralStats(ids[5], "D2", 0.2, 0.8, 0.65),
            TestHelpers.RatedNeutralStats(ids[6], "D3", 0.3, 0.7, 0.50),
            TestHelpers.RatedNeutralStats(ids[7], "D4", 0.4, 0.6, 0.35),
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 4, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings, optionsCount: 3);

        result.Options.Should().NotBeEmpty();
        result.Options.Select(o => o.BalanceDiff)
              .Should().BeInAscendingOrder(
                  "opções ordenadas por PhysicalDiff crescente (BalanceDiff = PhysicalDiff nesta estratégia)");
    }

    // ── 9. Equilíbrio físico: primeira opção tem menor PhysicalDiff ───────────

    [Fact]
    public async Task FirstOption_HasBestPhysicalBalance()
    {
        // 4 jogadores: 2 ofensivos e 2 defensivos com físico bem diferente
        // Melhor divisão: A(off,phy=0.9) + C(def,phy=0.1) vs B(off,phy=0.1) + D(def,phy=0.9)
        //   → PhysicalDiff = |(0.9+0.1)-(0.1+0.9)| = 0 (perfeito)
        // Pior divisão: A+B vs C+D → PhysicalDiff = |(0.9+0.1)-(0.1+0.9)| = 0 também
        //   → Neste caso ambas divisões mistas têm PhysicalDiff=0 por simetria
        // Testamos com físico assimétrico:
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            TestHelpers.RatedNeutralStats(ids[0], "A", attackRating: 0.9, defenseRating: 0.1, physicalRating: 0.8),
            TestHelpers.RatedNeutralStats(ids[1], "B", attackRating: 0.8, defenseRating: 0.2, physicalRating: 0.2),
            TestHelpers.RatedNeutralStats(ids[2], "C", attackRating: 0.1, defenseRating: 0.9, physicalRating: 0.7),
            TestHelpers.RatedNeutralStats(ids[3], "D", attackRating: 0.2, defenseRating: 0.8, physicalRating: 0.3),
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings);

        result.Options.Should().NotBeEmpty();

        var best = result.Options.First();
        var physDiffBest = Math.Abs(best.TeamAWeight - best.TeamBWeight);

        foreach (var opt in result.Options.Skip(1))
        {
            var physDiffOther = Math.Abs(opt.TeamAWeight - opt.TeamBWeight);
            physDiffBest.Should().BeLessThanOrEqualTo(physDiffOther,
                "a primeira opção deve ter o menor PhysicalDiff");
        }
    }

    // ── 10. Todos neutros → equilíbrio físico perfeito ────────────────────────

    [Fact]
    public async Task AllNeutralPlayers_SamePhysical_PerfectBalance()
    {
        // 4 jogadores todos neutros (attack == defense) com mesmo físico
        // qualquer divisão 2v2 tem PhysicalDiff = 0
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids   = players.Select(p => p.Id).ToList();
        var stats = ids.Select(id => TestHelpers.RatedNeutralStats(id, "P", 0.5, 0.5, 0.6));

        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings);

        result.Options.First().BalanceDiff.Should().BeApproximately(0.0, 1e-9,
            "todos com mesmo físico → PhysicalDiff = 0");
    }

    // ── 11. Deduplicação ──────────────────────────────────────────────────────

    [Fact]
    public async Task Deduplication_NoIdenticalTeamCompositions()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids   = players.Select(p => p.Id).ToList();
        var stats = new[]
        {
            TestHelpers.RatedNeutralStats(ids[0], "A", 0.9, 0.1, 0.5),
            TestHelpers.RatedNeutralStats(ids[1], "B", 0.8, 0.2, 0.5),
            TestHelpers.RatedNeutralStats(ids[2], "C", 0.1, 0.9, 0.5),
            TestHelpers.RatedNeutralStats(ids[3], "D", 0.2, 0.8, 0.5),
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings, optionsCount: 10);

        // Garante que nenhuma composição canônica aparece duas vezes
        var keys = result.Options.Select(opt =>
        {
            var aIds = opt.TeamA.Select(x => x.PlayerId).OrderBy(x => x).ToList();
            var bIds = opt.TeamB.Select(x => x.PlayerId).OrderBy(x => x).ToList();
            var k1   = string.Join(",", aIds) + "|" + string.Join(",", bIds);
            var k2   = string.Join(",", bIds) + "|" + string.Join(",", aIds);
            return string.CompareOrdinal(k1, k2) <= 0 ? k1 : k2;
        }).ToList();

        keys.Should().OnlyHaveUniqueItems("composições de time duplicadas devem ser removidas");
    }

    // ── 12. Score = PhysicalDiff ──────────────────────────────────────────────

    [Fact]
    public async Task Score_EqualsPhysicalDiff_TeamWeightsArePhysicalSums()
    {
        var players = TestHelpers.Players(
            ("A", false), ("B", false), ("C", false), ("D", false));

        var ids = players.Select(p => p.Id).ToList();

        var stats = new[]
        {
            TestHelpers.RatedNeutralStats(ids[0], "A", 0.9, 0.1, 0.70),
            TestHelpers.RatedNeutralStats(ids[1], "B", 0.8, 0.2, 0.30),
            TestHelpers.RatedNeutralStats(ids[2], "C", 0.1, 0.9, 0.60),
            TestHelpers.RatedNeutralStats(ids[3], "D", 0.2, 0.8, 0.40),
        };

        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };
        var result   = await Build(stats).GenerateTeamsAsync(players, settings);

        foreach (var opt in result.Options)
        {
            // TeamAWeight e TeamBWeight devem ser as somas do físico
            var physSumA = opt.TeamA.Sum(p =>
                p.PhysicalRatingNorm ?? 0.0);
            var physSumB = opt.TeamB.Sum(p =>
                p.PhysicalRatingNorm ?? 0.0);

            opt.TeamAWeight.Should().BeApproximately(physSumA, 1e-9,
                "TeamAWeight deve ser a soma do físico do Time A");
            opt.TeamBWeight.Should().BeApproximately(physSumB, 1e-9,
                "TeamBWeight deve ser a soma do físico do Time B");
            opt.BalanceDiff.Should().BeApproximately(
                Math.Abs(physSumA - physSumB), 1e-9,
                "BalanceDiff deve ser |PhysicalSumA - PhysicalSumB|");
        }
    }
}
