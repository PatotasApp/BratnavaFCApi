using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

using static BratnavaFC.Application.TeamGeneration.StrategyHelpers;

namespace BratnavaFC.Application.TeamGeneration.Strategies;

/// <summary>
/// Distribui os jogadores de linha em times equilibrando por perfil e físico.
///
/// Regras:
///   1. Todos os jogadores de linha devem ter os três ratings (ataque, defesa, físico).
///      Se algum estiver sem rating o algoritmo retorna resultado vazio.
///   2. Goleiros são excluídos do algoritmo e colocados em Unassigned.
///   3. Classificação por perfil:
///        attack &gt; defense → Ofensivo
///        defense &gt; attack → Defensivo
///        attack == defense → Neutro (redistribuído para equalizar os grupos)
///   4. Restrição na busca: cada time recebe no máximo ceil(totalOff/2) ofensivos
///      e ceil(totalDef/2) defensivos, garantindo a distribuição de perfis.
///   5. Score = |PhysicalSumA − PhysicalSumB| (minimizado).
/// </summary>
public sealed class ProfileStrategy : ITeamGenerationStrategy
{
    private readonly IPlayerStatsService _statsService;

    public ProfileStrategy(IPlayerStatsService statsService)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
    }

    // ── Perfil ────────────────────────────────────────────────────────────────

    private enum PlayerProfile { Offensive, Defensive, Neutral }

    private sealed record ProfiledPlayer(
        PlayerRequestDto Player,
        PlayerStats      Stats,
        PlayerProfile    Profile)
    {
        public double Physical => PhysicalOf(Stats);
    }

    // ── Resultado da busca ────────────────────────────────────────────────────

    private sealed record DraftResult(
        List<ProfiledPlayer> TeamA,
        List<ProfiledPlayer> TeamB,
        double PhysicalSumA,
        double PhysicalSumB)
    {
        public double PhysicalDiff => Math.Abs(PhysicalSumA - PhysicalSumB);
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    public async Task<TeamsOptionsResultDto> GenerateTeamsAsync(
        List<PlayerRequestDto> players,
        TeamGenerationSettings settings,
        int optionsCount = 3,
        CancellationToken cancellationToken = default)
    {
        // Input vazio → sem opções
        if (players.Count == 0)
            return new TeamsOptionsResultDto([]);

        // Carrega stats
        Result<Dictionary<Guid, PlayerStats>> statsResult =
            await LoadStatsByPlayerId(_statsService, players, cancellationToken).ConfigureAwait(false);

        if (!statsResult.Success)
            return BuildEmptyResult(players);

        Dictionary<Guid, PlayerStats> statsById = statsResult.Data!;

        // Separa goleiros → Unassigned
        List<PlayerRequestDto> goalkeepers  = players.Where(p =>  p.IsGoalkeeper).ToList();
        List<PlayerRequestDto> fieldPlayers = players.Where(p => !p.IsGoalkeeper).ToList();

        // Valida: todos os jogadores de linha precisam dos três ratings
        foreach (PlayerRequestDto p in fieldPlayers)
        {
            PlayerStats s = GetOrCreateStats(statsById, p.Id, p.Name);
            if (!s.AttackRatingNorm.HasValue ||
                !s.DefenseRatingNorm.HasValue ||
                !s.PhysicalRatingNorm.HasValue)
                return BuildEmptyResult(players);
        }

        // Classifica e resolve neutros
        List<ProfiledPlayer> profiled = Classify(fieldPlayers, statsById);

        // Goleiros são excluídos do algoritmo, então o tamanho real por time
        // é calculado a partir dos jogadores de linha disponíveis, não do PlayersPerTeam
        // original (que inclui goleiros).
        int perTeam = fieldPlayers.Count / 2;
        if (perTeam == 0) return BuildEmptyResult(players);
        int maxOffPerTeam  = (int)Math.Ceiling(profiled.Count(p => p.Profile == PlayerProfile.Offensive) / 2.0);
        int maxDefPerTeam  = (int)Math.Ceiling(profiled.Count(p => p.Profile == PlayerProfile.Defensive) / 2.0);

        // Busca exaustiva
        var pool = new List<DraftResult>();
        Expand(profiled, 0, perTeam, maxOffPerTeam, maxDefPerTeam,
               new List<ProfiledPlayer>(perTeam),
               new List<ProfiledPlayer>(perTeam),
               0, 0, 0, 0, 0.0, 0.0, pool);

        // Deduplica
        var seen   = new HashSet<string>();
        var unique = new List<DraftResult>();
        foreach (DraftResult r in pool)
        {
            string key = BuildTeamsKey(
                r.TeamA.Select(p => p.Player.Id),
                r.TeamB.Select(p => p.Player.Id));
            if (seen.Add(key)) unique.Add(r);
        }

        // Seleciona top N por PhysicalDiff
        List<DraftResult> best = unique
            .OrderBy(r => r.PhysicalDiff)
            .Take(optionsCount)
            .ToList();

        // Goleiros ficam no Unassigned
        List<PlayerWeightDto> gkUnassigned = goalkeepers
            .Select(p =>
            {
                PlayerStats s = GetOrCreateStats(statsById, p.Id, p.Name);
                return new PlayerWeightDto(p.Id, 0.0)
                {
                    AttackRatingNorm   = s.AttackRatingNorm,
                    DefenseRatingNorm  = s.DefenseRatingNorm,
                    PhysicalRatingNorm = s.PhysicalRatingNorm,
                };
            })
            .ToList();

        List<TeamOptionDto> options = best.Select(r => BuildOption(r, gkUnassigned)).ToList();
        return new TeamsOptionsResultDto(options);
    }

    // ── Classificação ─────────────────────────────────────────────────────────

    private static List<ProfiledPlayer> Classify(
        List<PlayerRequestDto> fieldPlayers,
        Dictionary<Guid, PlayerStats> statsById)
    {
        var offensives = new List<PlayerRequestDto>();
        var defensives = new List<PlayerRequestDto>();
        var neutrals   = new Queue<PlayerRequestDto>();

        foreach (PlayerRequestDto p in fieldPlayers)
        {
            PlayerStats s   = GetOrCreateStats(statsById, p.Id, p.Name);
            double      atk = AttackOf(s);
            double      def = DefenseOf(s);

            if      (atk > def) offensives.Add(p);
            else if (def > atk) defensives.Add(p);
            else                neutrals.Enqueue(p);
        }

        var result = new List<ProfiledPlayer>(fieldPlayers.Count);

        result.AddRange(offensives.Select(p =>
            new ProfiledPlayer(p, GetOrCreateStats(statsById, p.Id, p.Name), PlayerProfile.Offensive)));
        result.AddRange(defensives.Select(p =>
            new ProfiledPlayer(p, GetOrCreateStats(statsById, p.Id, p.Name), PlayerProfile.Defensive)));

        // Distribui neutros para equalizar os grupos; sobrando → Neutral verdadeiro
        while (neutrals.Count > 0)
        {
            PlayerRequestDto p        = neutrals.Dequeue();
            PlayerStats      s        = GetOrCreateStats(statsById, p.Id, p.Name);
            int              offCount = result.Count(x => x.Profile == PlayerProfile.Offensive);
            int              defCount = result.Count(x => x.Profile == PlayerProfile.Defensive);

            PlayerProfile assigned =
                offCount < defCount ? PlayerProfile.Offensive :
                defCount < offCount ? PlayerProfile.Defensive :
                                      PlayerProfile.Neutral;

            result.Add(new ProfiledPlayer(p, s, assigned));
        }

        return result;
    }

    // ── Busca exaustiva com restrição de perfil ───────────────────────────────

    private static void Expand(
        List<ProfiledPlayer> all,
        int index,
        int perTeam,
        int maxOffPerTeam,
        int maxDefPerTeam,
        List<ProfiledPlayer> teamA,
        List<ProfiledPlayer> teamB,
        int offA, int offB,
        int defA, int defB,
        double physA, double physB,
        List<DraftResult> results)
    {
        // Terminal: ambos os times completos
        if (teamA.Count == perTeam && teamB.Count == perTeam)
        {
            results.Add(new DraftResult(
                new List<ProfiledPlayer>(teamA),
                new List<ProfiledPlayer>(teamB),
                physA, physB));
            return;
        }

        if (index >= all.Count) return;

        ProfiledPlayer p      = all[index];
        double         phys   = p.Physical;
        bool           isOff  = p.Profile == PlayerProfile.Offensive;
        bool           isDef  = p.Profile == PlayerProfile.Defensive;
        int            slotsA = perTeam - teamA.Count;
        int            slotsB = perTeam - teamB.Count;
        int            remaining = all.Count - index - 1; // jogadores após este

        // Tenta colocar no Time A
        if (slotsA > 0)
        {
            bool canA = (!isOff || offA < maxOffPerTeam) &&
                        (!isDef || defA < maxDefPerTeam);
            if (canA)
            {
                teamA.Add(p);
                Expand(all, index + 1, perTeam, maxOffPerTeam, maxDefPerTeam,
                       teamA, teamB,
                       offA + (isOff ? 1 : 0), offB,
                       defA + (isDef ? 1 : 0), defB,
                       physA + phys, physB, results);
                teamA.RemoveAt(teamA.Count - 1);
            }
        }

        // Tenta colocar no Time B
        if (slotsB > 0)
        {
            bool canB = (!isOff || offB < maxOffPerTeam) &&
                        (!isDef || defB < maxDefPerTeam);
            if (canB)
            {
                teamB.Add(p);
                Expand(all, index + 1, perTeam, maxOffPerTeam, maxDefPerTeam,
                       teamA, teamB,
                       offA, offB + (isOff ? 1 : 0),
                       defA, defB + (isDef ? 1 : 0),
                       physA, physB + phys, results);
                teamB.RemoveAt(teamB.Count - 1);
            }
        }

        // Pula este jogador (Unassigned) — só quando há candidatos suficientes sobrando
        if (remaining >= slotsA + slotsB)
        {
            Expand(all, index + 1, perTeam, maxOffPerTeam, maxDefPerTeam,
                   teamA, teamB,
                   offA, offB, defA, defB, physA, physB, results);
        }
    }

    // ── Monta o DTO de saída ──────────────────────────────────────────────────

    private static TeamOptionDto BuildOption(DraftResult r, List<PlayerWeightDto> gkUnassigned)
    {
        static PlayerWeightDto ToDto(ProfiledPlayer p) =>
            new(p.Player.Id, p.Physical)
            {
                AttackRatingNorm   = p.Stats.AttackRatingNorm,
                DefenseRatingNorm  = p.Stats.DefenseRatingNorm,
                PhysicalRatingNorm = p.Stats.PhysicalRatingNorm,
            };

        return new TeamOptionDto(
            TeamA:        r.TeamA.Select(ToDto).ToList(),
            TeamB:        r.TeamB.Select(ToDto).ToList(),
            Unassigned:   gkUnassigned,
            TeamAWeight:  r.PhysicalSumA,
            TeamBWeight:  r.PhysicalSumB,
            BalanceDiff:  r.PhysicalDiff,
            SynergyTotal: 0,
            Score:        r.PhysicalDiff
        )
        {
            PhysicalDiff = r.PhysicalDiff,
        };
    }
}
