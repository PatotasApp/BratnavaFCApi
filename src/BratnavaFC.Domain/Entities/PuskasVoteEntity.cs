namespace BratnavaFC.Domain.Entities;

/// <summary>Um voto (um gol escolhido) de uma pessoa na votação do Puskas; até 3 por pessoa.</summary>
public sealed class PuskasVoteEntity : BaseEntity
{
    public Guid PollId { get; private set; }
    public Guid ClipId { get; private set; }

    /// <summary>Nome como a pessoa digitou (para a apuração).</summary>
    public string VoterName { get; private set; } = default!;

    /// <summary>Nome sem acento, minúsculo e com espaços normalizados: impede votar duas vezes com o mesmo nome.</summary>
    public string VoterKey { get; private set; } = default!;

    // EF Core
    private PuskasVoteEntity() { }

    public PuskasVoteEntity(Guid pollId, Guid clipId, string voterName, string voterKey)
    {
        PollId = pollId;
        ClipId = clipId;
        VoterName = voterName;
        VoterKey = voterKey;
    }
}
