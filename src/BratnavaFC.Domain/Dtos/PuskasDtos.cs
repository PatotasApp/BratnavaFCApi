namespace BratnavaFC.Domain.Dtos;

/// <summary>Jogador que pode ser apontado como autor do gol (quem jogou aquela partida).</summary>
public record PuskasCandidateDto(Guid PlayerId, string Name, int Team);

/// <summary>
/// Replay entre os mais curtidos, com o autor informado pelo admin (null enquanto não informado).
/// ScorerName usa o nome curto das estatísticas (primeiro nome, ou completo se houver homônimo).
/// </summary>
public record PuskasClipDto(
    RankedReplayDto Clip,
    Guid? ScorerPlayerId,
    string? ScorerName,
    IReadOnlyList<PuskasCandidateDto> Candidates
);

/// <summary>Top N de replays de gol mais curtidos (empatados no corte entram) para o telão do Puskas.</summary>
public record PuskasDto(
    string? GroupName,
    int Top,
    int Identified,
    IReadOnlyList<PuskasClipDto> Items
);

public record SetPuskasScorerRequest(Guid? PlayerId);

// ── Votação do Puskas (QR code) ──────────────────────────────────────────────

/// <summary>Votação do Puskas da patota (admin). O Code vai no link/QR do site.</summary>
public record PuskasPollDto(string Code, bool IsOpen, int ClipCount, int Voters, int MaxVotes);

/// <summary>Gol na folha de votação: só miniatura, autor e data (sem curtidas, para não influenciar).</summary>
public record PuskasSheetItemDto(Guid ClipId, string VideoUrl, string ScorerName, DateTime? MatchPlayedAt, string? ClipTime);

/// <summary>O que quem escaneou o QR code vê (público, sem login).</summary>
public record PuskasSheetDto(string? GroupName, bool IsOpen, int MaxVotes, IReadOnlyList<PuskasSheetItemDto> Items);

public record SubmitPuskasVoteRequest(string VoterName, IReadOnlyList<Guid> ClipIds);

public record SetPuskasPollOpenRequest(bool IsOpen);

/// <summary>Resultado de um gol na apuração; Rank com empate (1, 1, 3).</summary>
public record PuskasResultItemDto(
    Guid ClipId, string VideoUrl, string ScorerName, DateTime? MatchPlayedAt, string? ClipTime,
    int Rank, int Votes, IReadOnlyList<string> Voters);

public record PuskasResultsDto(PuskasPollDto Poll, int TotalVoters, IReadOnlyList<string> VoterNames, IReadOnlyList<PuskasResultItemDto> Items);
