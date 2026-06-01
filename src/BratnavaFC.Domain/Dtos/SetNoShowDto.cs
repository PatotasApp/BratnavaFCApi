namespace BratnavaFC.Domain.Dtos;

public sealed class SetNoShowDto
{
    /// <summary>True = jogador não jogou (exclui de stats). False = desfaz marcação.</summary>
    public bool DidNotPlay { get; set; }
}
