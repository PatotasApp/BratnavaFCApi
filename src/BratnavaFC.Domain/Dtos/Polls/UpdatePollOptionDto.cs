namespace BratnavaFC.Domain.Dtos.Polls;

public sealed class UpdatePollOptionDto
{
    public string? Text { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// Full replacement list. If null, existing images are preserved. Empty list clears all images.
    /// </summary>
    public List<string>? Images { get; set; }
}
