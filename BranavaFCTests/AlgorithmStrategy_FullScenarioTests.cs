using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Application.TeamGeneration.Strategies;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Cenário completo: 10 jogadores de linha + 2 goleiros → PerTeam=6, IncludeGoalkeepers=true
///
/// Jogadores e W_base (WinRate = wins/100):
/// ┌─────────────────────────────────────────────────────────────────┐
/// │  #0    #1    #2    #3    #4    #5    #6    #7    #8    #9       │
/// │  P1    P2    P3    GK1   P4    P5    P6    P7    P8    P9       │
/// │  0.85  0.80  0.75  0.72  0.68  0.63  0.58  0.52  0.47  0.43   │
/// │                    ▲GK                                          │
/// │  #10   #11                                                      │
/// │  P10   GK2                                                      │
/// │  0.38  0.32                                                     │
/// │         ▲GK                                                     │
/// └─────────────────────────────────────────────────────────────────┘
///
/// ΣW_total = 7.13  →  ideal por time ≈ 3.565
///
/// Para depurar o algoritmo, abra AlgorithmStrategy.cs e coloque breakpoints em:
///   - GenerateTeamsAsync         → ponto de entrada, ver candidatesByStrength e allSeedPairs
///   - EvaluateSeedPairs          → ver deduplicação: evaluatedKeys e uniqueOutcomes
///   - SearchBestDraft (beam loop)→ ver beam sendo expandido e podado a cada iteração
///   - DraftState.Expand          → ver cada jogador sendo colocado no Time A ou B
///   - CreateInitialState         → ver sementes sendo fixadas no estado inicial
/// </summary>
public class AlgorithmStrategy_FullScenarioTests
{
    // ── Cenário base ─────────────────────────────────────────────────────────

    /// <summary>ΣW de todos os 12 jogadores.</summary>
    private const double TotalWeight = 0.85 + 0.80 + 0.75 + 0.72
                                     + 0.68 + 0.63 + 0.58 + 0.52
                                     + 0.47 + 0.43 + 0.38 + 0.32;  // = 7.13

    private const double IdealWeightPerTeam = TotalWeight / 2.0;    // ≈ 3.565

    private static readonly TeamGenerationSettings Settings6v6 =
        new() { PlayersPerTeam = 6, IncludeGoalkeepers = true };

    /// <summary>
    /// Cria os 12 PlayerRequestDtos.
    /// índice → nome → isGK → W_base esperado:
    ///   [0] P1  false 0.85 | [1] P2  false 0.80 | [2]  P3  false 0.75
    ///   [3] GK1 true  0.72 | [4] P4  false 0.68 | [5]  P5  false 0.63
    ///   [6] P6  false 0.58 | [7] P7  false 0.52 | [8]  P8  false 0.47
    ///   [9] P9  false 0.43 | [10] P10 false 0.38 | [11] GK2 true 0.32
    /// </summary>
    private static List<PlayerRequestDto> BuildPlayers() =>
        TestHelpers.Players(
            ("P1",  false),  // [0]  W=0.85
            ("P2",  false),  // [1]  W=0.80
            ("P3",  false),  // [2]  W=0.75
            ("GK1", true),   // [3]  W=0.72  GK
            ("P4",  false),  // [4]  W=0.68
            ("P5",  false),  // [5]  W=0.63
            ("P6",  false),  // [6]  W=0.58
            ("P7",  false),  // [7]  W=0.52
            ("P8",  false),  // [8]  W=0.47
            ("P9",  false),  // [9]  W=0.43
            ("P10", false),  // [10] W=0.38
            ("GK2", true));  // [11] W=0.32  GK

    /// <summary>
    /// Cria as stats correspondentes. WinRate = wins / 100 para cada jogador.
    /// Todos têm 100 partidas → IsNeutral=false → EffectiveWeight = WinRate.
    /// SynergyWith vazio (sem histórico de dupla).
    /// </summary>
    private static PlayerStats[] BuildStats(List<PlayerRequestDto> p) =>
    [
        TestHelpers.Stats(p[0].Id,  "P1",  wins: 85, ties: 0, losses: 15),   // 0.85
        TestHelpers.Stats(p[1].Id,  "P2",  wins: 80, ties: 0, losses: 20),   // 0.80
        TestHelpers.Stats(p[2].Id,  "P3",  wins: 75, ties: 0, losses: 25),   // 0.75
        TestHelpers.Stats(p[3].Id,  "GK1", wins: 72, ties: 0, losses: 28),   // 0.72
        TestHelpers.Stats(p[4].Id,  "P4",  wins: 68, ties: 0, losses: 32),   // 0.68
        TestHelpers.Stats(p[5].Id,  "P5",  wins: 63, ties: 0, losses: 37),   // 0.63
        TestHelpers.Stats(p[6].Id,  "P6",  wins: 58, ties: 0, losses: 42),   // 0.58
        TestHelpers.Stats(p[7].Id,  "P7",  wins: 52, ties: 0, losses: 48),   // 0.52
        TestHelpers.Stats(p[8].Id,  "P8",  wins: 47, ties: 0, losses: 53),   // 0.47
        TestHelpers.Stats(p[9].Id,  "P9",  wins: 43, ties: 0, losses: 57),   // 0.43
        TestHelpers.Stats(p[10].Id, "P10", wins: 38, ties: 0, losses: 62),   // 0.38
        TestHelpers.Stats(p[11].Id, "GK2", wins: 32, ties: 0, losses: 68),   // 0.32
    ];

    private static AlgorithmStrategy BuildStrategy(List<PlayerRequestDto> players)
        => new AlgorithmStrategy(new FakeStatsService(BuildStats(players)));

    // ── T1 — Estrutura ───────────────────────────────────────────────────────

    /// <summary>
    /// FASE 1 + FASE 6 do resumo visual:
    /// Com PerTeam=6 e 12 candidatos → maxAssignable=12 →
    /// cada time deve ter exatamente 6 jogadores e Unassigned deve estar vazio.
    /// </summary>
    [Fact]
    public async Task T1_Estrutura_CadaTimeTem6Jogadores_SemOverlap_SemUnassigned()
    {
        var players  = BuildPlayers();
        var strategy = BuildStrategy(players);

        var result = await strategy.GenerateTeamsAsync(players, Settings6v6, optionsCount: 3);

        result.Options.Should().NotBeEmpty("12 jogadores + PerTeam=6 deve produzir ao menos uma opção");

        foreach (var opt in result.Options)
        {
            // 6 por time
            opt.TeamA.Should().HaveCount(6, "PerTeam=6 → TeamA deve ter 6 jogadores");
            opt.TeamB.Should().HaveCount(6, "PerTeam=6 → TeamB deve ter 6 jogadores");

            // Todos os 12 foram alocados → Unassigned vazio
            opt.Unassigned.Should().BeEmpty(
                "maxAssignable=12 = perTeam×2, todos os 12 jogadores devem ser distribuídos");

            // Sem sobreposição entre os dois times
            var allAssignedIds = opt.TeamA.Select(x => x.PlayerId)
                                    .Concat(opt.TeamB.Select(x => x.PlayerId))
                                    .ToList();

            allAssignedIds.Should().OnlyHaveUniqueItems(
                "nenhum jogador pode aparecer nos dois times simultaneamente");

            allAssignedIds.Should().HaveCount(12,
                "todos os 12 candidatos devem estar atribuídos");
        }
    }

    // ── T2 — Goleiros ────────────────────────────────────────────────────────

    /// <summary>
    /// Com ≥ 2 goleiros elegíveis, a regra de composição obrigatória garante 1 GK em cada time.
    /// Essa validação é estrutural (constraint), não depende de score.
    /// </summary>
    [Fact]
    public async Task T2_Goleiros_MelhorOpcao_TemUmGoleiroEmCadaTime()
    {
        var players  = BuildPlayers();
        var strategy = BuildStrategy(players);

        var result   = await strategy.GenerateTeamsAsync(players, Settings6v6, optionsCount: 3);
        var best     = result.Options.First();

        var gkIds = players.Where(p => p.IsGoalkeeper).Select(p => p.Id).ToHashSet();

        int gkCountA = best.TeamA.Count(x => gkIds.Contains(x.PlayerId));
        int gkCountB = best.TeamB.Count(x => gkIds.Contains(x.PlayerId));

        gkCountA.Should().Be(1,
            "constraint obrigatório: Time A deve ter exatamente 1 goleiro quando há ≥2 elegíveis");

        gkCountB.Should().Be(1,
            "constraint obrigatório: Time B deve ter exatamente 1 goleiro quando há ≥2 elegíveis");
    }

    // ── T3 — Ordenação primária por BalanceDiff ───────────────────────────────

    /// <summary>
    /// As opções devem estar em ordem crescente de BalanceDiff (critério principal).
    /// Quando há empate em BalanceDiff, a ordenação secundária é por SynergyTotal decrescente.
    /// </summary>
    [Fact]
    public async Task T3_Opcoes_OrdenadaPorBalanceDiffCrescente()
    {
        var players  = BuildPlayers();
        var strategy = BuildStrategy(players);

        var result = await strategy.GenerateTeamsAsync(players, Settings6v6, optionsCount: 3);

        result.Options.Should().NotBeEmpty();

        result.Options.Select(o => o.BalanceDiff)
            .Should().BeInAscendingOrder(
                "opções são ordenadas por BalanceDiff crescente; sinergia é apenas critério de desempate");
    }

    // ── T4 — Qualidade do balanceamento ──────────────────────────────────────

    /// <summary>
    /// ΣW = 7.13, ideal por time = 3.565.
    /// Com beam width=24 e 66 pares de sementes avaliados, o algoritmo deve
    /// encontrar uma divisão muito próxima do ideal — BalanceDiff &lt; 0.10.
    /// </summary>
    [Fact]
    public async Task T4_BalanceDiff_MelhorOpcaoEstaProximoDoIdealDe3_565PorTime()
    {
        var players  = BuildPlayers();
        var strategy = BuildStrategy(players);

        var result = await strategy.GenerateTeamsAsync(players, Settings6v6, optionsCount: 3);
        var best   = result.Options.First();

        best.BalanceDiff.Should().BeLessThan(0.10,
            $"ΣW={TotalWeight:0.00}, ideal/time={IdealWeightPerTeam:0.000}; " +
            "o beam search com 66 pares deve encontrar balanceamento muito próximo do ideal");

        // BalanceDiff deve ser consistente com os pesos reais dos times
        double expectedDiff = Math.Abs(best.TeamAWeight - best.TeamBWeight);
        best.BalanceDiff.Should().BeApproximately(expectedDiff, 1e-9,
            "BalanceDiff deve ser exatamente |TeamAWeight - TeamBWeight|");
    }

    // ── T5 — Cálculo do Score ────────────────────────────────────────────────

    /// <summary>
    /// Score = BalanceDiff.
    /// Sinergia não entra mais no score — é critério de desempate externo.
    /// Goleiro é constraint estrutural.
    /// </summary>
    [Fact]
    public async Task T5_Score_IgualAoBalanceDiff()
    {
        var players  = BuildPlayers();
        var strategy = BuildStrategy(players);

        var result = await strategy.GenerateTeamsAsync(players, Settings6v6, optionsCount: 3);

        foreach (var opt in result.Options)
        {
            opt.Score.Should().BeApproximately(opt.BalanceDiff, 1e-9,
                $"Score deve ser igual a BalanceDiff ({opt.BalanceDiff:0.000}); sinergia só desempata externamente");
        }
    }

    // ── T6 — Deduplicação ────────────────────────────────────────────────────

    /// <summary>
    /// FASE 5: EvaluateSeedPairs descarta resultados com a mesma chave canônica.
    /// As N opções retornadas devem representar composições de times genuinamente distintas,
    /// inclusive considerando que A↔B são invariantes (chave canônica).
    /// </summary>
    [Fact]
    public async Task T6_Deduplicacao_TresOpcoesTêmComposicoesDeTimesDistintas()
    {
        var players  = BuildPlayers();
        var strategy = BuildStrategy(players);

        var result = await strategy.GenerateTeamsAsync(players, Settings6v6, optionsCount: 3);

        // Gera chave canônica A↔B-invariante para cada opção
        static string CanonicalKey(TeamOptionDto opt)
        {
            string keyA = string.Join(",", opt.TeamA.Select(x => x.PlayerId).OrderBy(id => id));
            string keyB = string.Join(",", opt.TeamB.Select(x => x.PlayerId).OrderBy(id => id));
            // Menor lexicograficamente vem primeiro (mesmo critério do BuildTeamsKey)
            return string.CompareOrdinal(keyA, keyB) <= 0 ? $"{keyA}|{keyB}" : $"{keyB}|{keyA}";
        }

        var keys = result.Options.Select(CanonicalKey).ToList();

        keys.Should().OnlyHaveUniqueItems(
            "cada opção deve ser uma composição genuinamente diferente; " +
            "EvaluateSeedPairs deduplica via BuildTeamsKey (A↔B invariante)");
    }

    // ── T7 — Mapeamento de pesos ─────────────────────────────────────────────

    /// <summary>
    /// Todos os 12 jogadores têm 100 partidas → IsNeutral=false →
    /// EffectiveWeight = WinRate = wins/100 para cada um.
    /// Verifica que os pesos dos PlayerWeightDto na resposta batem exatamente com o esperado.
    /// </summary>
    [Fact]
    public async Task T7_Pesos_CadaJogadorTemExatamenteOSeuWinRateComoEfetiveWeight()
    {
        var players  = BuildPlayers();
        var stats    = BuildStats(players);
        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));

        var result = await strategy.GenerateTeamsAsync(players, Settings6v6, optionsCount: 1);
        var best   = result.Options.First();

        // Tabela de referência: nome → W_base esperado
        var expectedWeightByName = new Dictionary<string, double>
        {
            { "P1",  0.85 }, { "P2",  0.80 }, { "P3",  0.75 }, { "GK1", 0.72 },
            { "P4",  0.68 }, { "P5",  0.63 }, { "P6",  0.58 }, { "P7",  0.52 },
            { "P8",  0.47 }, { "P9",  0.43 }, { "P10", 0.38 }, { "GK2", 0.32 },
        };

        var nameById = players.ToDictionary(p => p.Id, p => p.Name);

        foreach (var pw in best.TeamA.Concat(best.TeamB))
        {
            string name           = nameById[pw.PlayerId];
            double expectedWeight = expectedWeightByName[name];

            pw.Weight.Should().BeApproximately(expectedWeight, 1e-9,
                $"{name}: EffectiveWeight deve ser {expectedWeight:0.00} " +
                "(IsNeutral=false → EffectiveWeight = WinRate = wins/100)");
        }

        // TeamAWeight + TeamBWeight deve ser igual ao ΣW_total
        (best.TeamAWeight + best.TeamBWeight).Should().BeApproximately(TotalWeight, 1e-9,
            $"soma dos pesos dos dois times deve ser ΣW_total = {TotalWeight:0.00}");
    }

    // ── T8 — Sinergia ────────────────────────────────────────────────────────

    /// <summary>
    /// Cenário isolado para testar o efeito da sinergia no beam search.
    ///
    /// 4 jogadores com peso idêntico (0.50):
    ///   Syn1, Syn2 → sinergia conjunta = +0.45
    ///   Neu1, Neu2 → sem sinergia
    ///
    /// Como BalanceDiff = 0 para QUALQUER divisão 2v2 (todos pesam igual),
    /// a sinergia é o único diferenciador entre as opções:
    ///
    ///   Syn1+Syn2 no mesmo time → Score = 1.00×0 + 0 - 0.25×0.45 = -0.1125  ← menor = melhor
    ///   qualquer outra divisão  → Score = 1.00×0 + 0 - 0.25×0.00 = 0.0000
    ///
    /// Portanto o beam search DEVE colocar Syn1 e Syn2 juntos.
    /// </summary>
    [Fact]
    public async Task T8_Sinergia_ParComAltaSinergiaEPesoIgualFicaNoMesmoTime()
    {
        var players = TestHelpers.Players(
            ("Syn1", false),
            ("Syn2", false),
            ("Neu1", false),
            ("Neu2", false));

        var statsArr = new[]
        {
            TestHelpers.Stats(players[0].Id, "Syn1", wins: 5, ties: 0, losses: 5),  // W=0.50
            TestHelpers.Stats(players[1].Id, "Syn2", wins: 5, ties: 0, losses: 5),  // W=0.50
            TestHelpers.Stats(players[2].Id, "Neu1", wins: 5, ties: 0, losses: 5),  // W=0.50
            TestHelpers.Stats(players[3].Id, "Neu2", wins: 5, ties: 0, losses: 5),  // W=0.50
        };

        // Adiciona sinergia Syn1 → Syn2 (GetPairSynergy tenta os dois lados)
        statsArr[0].SynergyWith![players[1].Id] = +0.45;

        var strategy = new AlgorithmStrategy(new FakeStatsService(statsArr));
        var settings = new TeamGenerationSettings { PlayersPerTeam = 2, IncludeGoalkeepers = false };

        var result = await strategy.GenerateTeamsAsync(players, settings, optionsCount: 1);
        var best   = result.Options.First();

        bool syn1InA = best.TeamA.Any(x => x.PlayerId == players[0].Id);
        bool syn2InA = best.TeamA.Any(x => x.PlayerId == players[1].Id);
        bool syn1InB = best.TeamB.Any(x => x.PlayerId == players[0].Id);
        bool syn2InB = best.TeamB.Any(x => x.PlayerId == players[1].Id);

        bool togetherInA = syn1InA && syn2InA;
        bool togetherInB = syn1InB && syn2InB;

        (togetherInA || togetherInB).Should().BeTrue(
            "Syn1 e Syn2 têm sinergia +0.45 e pesos idênticos; " +
            "como BalanceDiff=0 para qualquer split 2v2, a sinergia é o único diferenciador: " +
            "Score juntos=-0.1125 < Score separados=0.0, portanto devem ficar no mesmo time");

        best.SynergyTotal.Should().BeApproximately(0.45, 1e-9,
            "SynergyTotal = soma das sinergias de todos os pares nos dois times = 0.45");
    }

    // ── T9 — Soma dos pesos do time A e B ────────────────────────────────────

    /// <summary>
    /// TeamAWeight e TeamBWeight expostos no DTO devem bater com
    /// a soma dos Weight de cada PlayerWeightDto no time correspondente.
    /// </summary>
    [Fact]
    public async Task T9_TeamWeights_ConsistentesComSomaDosPesosDosJogadores()
    {
        var players  = BuildPlayers();
        var strategy = BuildStrategy(players);

        var result = await strategy.GenerateTeamsAsync(players, Settings6v6, optionsCount: 3);

        foreach (var opt in result.Options)
        {
            double sumA = opt.TeamA.Sum(x => x.Weight);
            double sumB = opt.TeamB.Sum(x => x.Weight);

            opt.TeamAWeight.Should().BeApproximately(sumA, 1e-9,
                "TeamAWeight deve ser a soma dos pesos individuais de TeamA");

            opt.TeamBWeight.Should().BeApproximately(sumB, 1e-9,
                "TeamBWeight deve ser a soma dos pesos individuais de TeamB");
        }
    }

    // ── Debug — Inspeção completa ─────────────────────────────────────────────

    /// <summary>
    /// Teste de depuração: executa o cenário completo do resumo visual e agrega
    /// todos os resultados em variáveis com nomes descritivos.
    ///
    /// COMO USAR:
    ///   1. Abra AlgorithmStrategy.cs
    ///   2. Coloque breakpoints nos pontos abaixo:
    ///
    ///      a) AlgorithmStrategy.cs → linha "var candidatesByStrength = candidates"
    ///         Inspecione: candidatesByStrength (12 jogadores ordenados por W_base desc)
    ///
    ///      b) AlgorithmStrategy.cs → linha "List<DraftOutcome> uniqueOutcomes = EvaluateSeedPairs"
    ///         Inspecione: allSeedPairs.Count (deve ser 66 = C(12,2))
    ///                     selectedPairs.Count (deve ser 66, budget=∞)
    ///
    ///      c) EvaluateSeedPairs → linha "if (evaluatedKeys.Add(key))"
    ///         Inspecione: evaluatedKeys.Count crescendo a cada par único
    ///                     uniqueOutcomes.Count acumulando resultados únicos
    ///
    ///      d) SearchBestDraft → linha "beam = expandedStates"
    ///         Inspecione: beam.Count (cresce até 24 e estabiliza)
    ///                     beam[0].PartialScore (o mais promissor até o momento)
    ///
    ///      e) DraftState.Expand → yield return new DraftState(...)
    ///         Inspecione: candidate.Player.Name (jogador sendo alocado)
    ///                     synergyGain (ganho de sinergia da alocação)
    ///                     TeamA.Count + TeamB.Count (progresso do draft)
    ///
    ///   3. Execute o teste no modo Debug do Visual Studio / Rider
    ///   4. Inspecione as variáveis nomeadas abaixo na janela Locals / Watch
    /// </summary>
    [Fact]
    public async Task Debug_InspecaoCompleta_CenarioVisual_12Jogadores_6v6()
    {
        // ── Setup ──────────────────────────────────────────────────────────────
        var players  = BuildPlayers();
        var stats    = BuildStats(players);

        // Dicionário auxiliar para legibilidade nos asserts e no Watch
        var nameById  = players.ToDictionary(p => p.Id, p => p.Name);
        var idByName  = players.ToDictionary(p => p.Name, p => p.Id);
        var gkIds     = players.Where(p => p.IsGoalkeeper).Select(p => p.Id).ToHashSet();

        var strategy = new AlgorithmStrategy(new FakeStatsService(stats));

        // ── Execução — coloque breakpoint AQUI para entrar no algoritmo ────────
        var result = await strategy.GenerateTeamsAsync(players, Settings6v6, optionsCount: 3);

        // ── Variáveis nomeadas para inspeção no debugger ───────────────────────

        // Opções retornadas (inspecione cada uma no Watch)
        var opt1 = result.Options[0];                                    // melhor
        var opt2 = result.Options.Count >= 2 ? result.Options[1] : null; // segunda
        var opt3 = result.Options.Count >= 3 ? result.Options[2] : null; // terceira

        // Composição textual da melhor opção (legível no debugger)
        string FormatTeam(IEnumerable<PlayerWeightDto> team)
            => string.Join(" | ", team.Select(x => $"{nameById[x.PlayerId]}({x.Weight:0.00})"));

        string opt1TeamAStr   = FormatTeam(opt1.TeamA);    // ex: "P1(0.85) | GK1(0.72) | ..."
        string opt1TeamBStr   = FormatTeam(opt1.TeamB);
        double opt1BalanceDiff  = opt1.BalanceDiff;          // quanto os times diferem em peso
        double opt1SynergyTotal = opt1.SynergyTotal;        // soma das sinergias dos pares
        double opt1Score        = opt1.Score;               // score final (menor = melhor)
        double opt1SumA         = opt1.TeamAWeight;         // ΣW do Time A
        double opt1SumB         = opt1.TeamBWeight;         // ΣW do Time B

        // Quem ficou em cada time? (IDs → nomes)
        var opt1TeamANames = opt1.TeamA.Select(x => nameById[x.PlayerId]).OrderBy(n => n).ToList();
        var opt1TeamBNames = opt1.TeamB.Select(x => nameById[x.PlayerId]).OrderBy(n => n).ToList();

        // Goleiros de cada time
        string opt1GkInA = string.Join(", ", opt1.TeamA.Where(x => gkIds.Contains(x.PlayerId)).Select(x => nameById[x.PlayerId]));
        string opt1GkInB = string.Join(", ", opt1.TeamB.Where(x => gkIds.Contains(x.PlayerId)).Select(x => nameById[x.PlayerId]));

        // ── Assertions (documenta o que deve ser verdade) ──────────────────────

        // Estrutura
        result.Options.Should().NotBeEmpty(
            "deve retornar ao menos 1 opção");

        opt1.TeamA.Should().HaveCount(6,
            $"TeamA tem {opt1.TeamA.Count} jogadores; esperado 6");

        opt1.TeamB.Should().HaveCount(6,
            $"TeamB tem {opt1.TeamB.Count} jogadores; esperado 6");

        opt1.Unassigned.Should().BeEmpty(
            "todos os 12 jogadores devem ser alocados (maxAssignable=12)");

        opt1.TeamA.Select(x => x.PlayerId)
            .Concat(opt1.TeamB.Select(x => x.PlayerId))
            .Should().OnlyHaveUniqueItems("sem overlap entre os dois times");

        // Goleiros
        opt1.TeamA.Count(x => gkIds.Contains(x.PlayerId)).Should().Be(1,
            $"Time A deve ter 1 GK; tem: [{opt1GkInA}]");

        opt1.TeamB.Count(x => gkIds.Contains(x.PlayerId)).Should().Be(1,
            $"Time B deve ter 1 GK; tem: [{opt1GkInB}]");

        // Score = BalanceDiff (sinergia não entra mais na fórmula)
        opt1Score.Should().BeApproximately(opt1BalanceDiff, 1e-9,
            $"Score deve ser igual a BalanceDiff ({opt1BalanceDiff:0.000}); sinergia só desempata externamente");

        // Balanceamento próximo do ideal
        opt1BalanceDiff.Should().BeLessThan(0.10,
            $"ΣW={TotalWeight:0.00}, ideal/time={IdealWeightPerTeam:0.000}; " +
            $"Time A=[{opt1TeamAStr}] ΣW={opt1SumA:0.000}; " +
            $"Time B=[{opt1TeamBStr}] ΣW={opt1SumB:0.000}");

        // ΣW total preservada
        (opt1SumA + opt1SumB).Should().BeApproximately(TotalWeight, 1e-9,
            $"ΣW_total={TotalWeight:0.00} deve ser conservado entre os dois times");

        // Ordem crescente de Score
        result.Options.Select(o => o.Score)
            .Should().BeInAscendingOrder("opções retornadas em ordem crescente de Score");
    }

    // ── Cenário real filtrado vindo do JSON ──────────────────────────────────

    private static readonly TeamGenerationSettings SettingsRealJson =
        new() { PlayersPerTeam = 6, IncludeGoalkeepers = true };

    private static readonly HashSet<string> RealScenarioPlayerNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Piccoli",
            "Little Piccoli",
            "Pagel",
            "Luis",
            "Luska",
            "Andrei",
            "Patrick",
            "Marlon",
            "Caio",
            "Mozart",
            "Artur",
            "Matheus"
        };

    /// <summary>
    /// Ajuste este caminho para onde você salvar o JSON dentro do projeto de testes.
    /// </summary>
    private static string GetRealJsonPath()
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "testes.json");

    private static RealVisualReport LoadRealJsonReport()
    {
        var path = GetRealJsonPath();

        File.Exists(path).Should().BeTrue(
            $"arquivo JSON real não encontrado em: {path}. " +
            "Salve o arquivo em BranavaFC.Tests/Fixtures/testes.json " +
            "e configure para copiar para o output directory.");

        var json = File.ReadAllText(path);

        var report = JsonSerializer.Deserialize<RealVisualReport>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        report.Should().NotBeNull("o JSON precisa ser desserializado corretamente");
        report!.Players.Should().NotBeNull();
        report.Players.Should().NotBeEmpty();

        return report;
    }

    private static List<RealVisualPlayer> LoadFilteredRealPlayers()
    {
        var report = LoadRealJsonReport();

        var filtered = report.Players
            .Where(p => RealScenarioPlayerNames.Contains(p.Name))
            .ToList();

        filtered.Should().HaveCount(12,
            "o cenário real filtrado deve conter exatamente os 12 jogadores escolhidos");

        return filtered;
    }

    private static List<PlayerRequestDto> BuildPlayersFromRealJsonComplete()
    {
        var filteredPlayers = LoadFilteredRealPlayers();

        return filteredPlayers
            .Select(p => new PlayerRequestDto(
                p.PlayerId,
                p.Name,
                p.IsGoalkeeper))
            .ToList();
    }

    private static PlayerStats[] BuildStatsFromRealJsonComplete(List<PlayerRequestDto> players)
    {
        var filteredPlayers = LoadFilteredRealPlayers();
        var filteredIds = filteredPlayers.Select(x => x.PlayerId).ToHashSet();

        var statsById = filteredPlayers.ToDictionary(
            p => p.PlayerId,
            p => new PlayerStats
            {
                PlayerId = p.PlayerId,
                Name = p.Name,
                Wins = p.Wins,
                Ties = p.Ties,
                Losses = p.Losses,
                WinRate = p.WinRate,
                Goals = p.Goals,
                Assists = p.Assists,
                SynergyWith = new Dictionary<Guid, double>(),
                NeutralOverride = null
            });

        foreach (var player in filteredPlayers)
        {
            var current = statsById[player.PlayerId];

            foreach (var synergy in player.Synergies.Where(s => filteredIds.Contains(s.WithPlayerId)))
            {
                if (!statsById.TryGetValue(synergy.WithPlayerId, out var other))
                    continue;

                double baseline = (current.WinRate + other.WinRate) / 2.0;
                double effectiveSynergy = Math.Clamp(
                    synergy.WinRateTogether - baseline,
                    -0.5,
                    +0.5);

                current.SynergyWith[synergy.WithPlayerId] = effectiveSynergy;
            }
        }

        return statsById.Values.ToArray();
    }

    private static AlgorithmStrategy BuildStrategyFromRealJsonComplete(List<PlayerRequestDto> players)
        => new AlgorithmStrategy(new FakeStatsService(BuildStatsFromRealJsonComplete(players)));

    [Fact]
    public async Task T10_CenarioRealCompletoDoJson_DeveGerarOpcoesValidasComTodosOsJogadoresESinergias()
    {
        var players = BuildPlayersFromRealJsonComplete();
        var strategy = BuildStrategyFromRealJsonComplete(players);

        var result = await strategy.GenerateTeamsAsync(players, SettingsRealJson, optionsCount: 3);

        result.Options.Should().NotBeEmpty("o cenário real filtrado do JSON deve gerar opções");

        foreach (var opt in result.Options)
        {
            opt.TeamA.Should().HaveCount(6);
            opt.TeamB.Should().HaveCount(6);

            var allAssigned = opt.TeamA.Select(x => x.PlayerId)
                .Concat(opt.TeamB.Select(x => x.PlayerId))
                .ToList();

            allAssigned.Should().OnlyHaveUniqueItems("nenhum jogador pode aparecer nos dois times");
            allAssigned.Count.Should().Be(12, "6x6 deve alocar exatamente os 12 jogadores filtrados");
            opt.Unassigned.Should().BeEmpty("com 12 jogadores em cenário 6x6, ninguém deve sobrar");

            // Score = BalanceDiff (sinergia não entra mais na fórmula)
            opt.Score.Should().BeApproximately(opt.BalanceDiff, 1e-9);
        }

        // Dentro da janela de tolerância (0.05), as opções são reordenadas por equilíbrio
        // dimensional — por isso BalanceDiff pode não ser estritamente crescente.
        // Verificamos que todas as opções estão dentro da janela em relação à melhor.
        var balanceDiffs = result.Options.Select(x => x.BalanceDiff).ToList();
        var bestDiff = balanceDiffs.Min();
        foreach (var diff in balanceDiffs)
            diff.Should().BeLessThanOrEqualTo(bestDiff + 0.05,
                "todas as opções devem estar dentro da janela de tolerância (0.05) em relação ao melhor BalanceDiff");
    }

    [Fact]
    public void T11_CenarioRealCompletoDoJson_DeveCarregarTodosOsJogadoresETodasAsSinergias()
    {
        var players = BuildPlayersFromRealJsonComplete();
        var stats = BuildStatsFromRealJsonComplete(players);

        players.Should().HaveCount(12, "o cenário real filtrado deve conter exatamente 12 jogadores");
        stats.Should().HaveCount(12);

        var expectedNames = RealScenarioPlayerNames.OrderBy(x => x).ToList();
        var actualNames = players.Select(x => x.Name).OrderBy(x => x).ToList();

        actualNames.Should().BeEquivalentTo(expectedNames);

        foreach (var stat in stats)
        {
            stat.SynergyWith.Should().HaveCount(11,
                $"{stat.Name} deve ter sinergia com os outros 11 jogadores do cenário filtrado");
        }
    }

    // ── DTOs auxiliares para desserializar o JSON real ───────────────────────

    private sealed class RealVisualReport
    {
        [JsonPropertyName("groupId")]
        public Guid GroupId { get; set; }

        [JsonPropertyName("totalMatchesConsidered")]
        public int TotalMatchesConsidered { get; set; }

        [JsonPropertyName("totalFinalizedMatches")]
        public int TotalFinalizedMatches { get; set; }

        [JsonPropertyName("totalMatchesWithScore")]
        public int TotalMatchesWithScore { get; set; }

        [JsonPropertyName("players")]
        public List<RealVisualPlayer> Players { get; set; } = new();
    }

    private sealed class RealVisualPlayer
    {
        [JsonPropertyName("playerId")]
        public Guid PlayerId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("isGoalkeeper")]
        public bool IsGoalkeeper { get; set; }

        [JsonPropertyName("gamesPlayed")]
        public int GamesPlayed { get; set; }

        [JsonPropertyName("wins")]
        public int Wins { get; set; }

        [JsonPropertyName("ties")]
        public int Ties { get; set; }

        [JsonPropertyName("losses")]
        public int Losses { get; set; }

        [JsonPropertyName("winRate")]
        public double WinRate { get; set; }

        [JsonPropertyName("mvps")]
        public int Mvps { get; set; }

        [JsonPropertyName("goals")]
        public int Goals { get; set; }

        [JsonPropertyName("assists")]
        public int Assists { get; set; }

        [JsonPropertyName("ownGoals")]
        public int OwnGoals { get; set; }

        [JsonPropertyName("synergies")]
        public List<RealVisualSynergy> Synergies { get; set; } = new();
    }

    private sealed class RealVisualSynergy
    {
        [JsonPropertyName("withPlayerId")]
        public Guid WithPlayerId { get; set; }

        [JsonPropertyName("withPlayerName")]
        public string WithPlayerName { get; set; } = string.Empty;

        [JsonPropertyName("matchesTogether")]
        public int MatchesTogether { get; set; }

        [JsonPropertyName("winsTogether")]
        public int WinsTogether { get; set; }

        [JsonPropertyName("winRateTogether")]
        public double WinRateTogether { get; set; }
    }
}
