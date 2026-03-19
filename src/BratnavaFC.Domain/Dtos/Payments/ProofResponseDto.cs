namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class ProofResponseDto
{
    public string  Base64    { get; init; } = string.Empty;
    public string  FileName  { get; init; } = string.Empty;
    public string  MimeType  { get; init; } = string.Empty;
}
