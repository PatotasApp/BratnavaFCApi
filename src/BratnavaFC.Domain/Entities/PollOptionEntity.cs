namespace BratnavaFC.Domain.Entities;
public class PollOptionEntity : BaseEntity
{
    public Guid PollId { get; private set; }
    public PollEntity? Poll { get; private set; }
    public string Text { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }
    public int SortOrder { get; private set; }

    private PollOptionEntity() { }

    public PollOptionEntity(Guid pollId, string text, string? description, string? imageUrl, int sortOrder = 0)
    {
        if (pollId == Guid.Empty) throw new InvalidOperationException("PollId é obrigatório.");
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Texto da opção é obrigatório.");
        PollId = pollId;
        Text = text.Trim();
        Description = description?.Trim();
        ImageUrl = imageUrl?.Trim();
        SortOrder = sortOrder;
    }

    public void Update(string? text, string? description, string? imageUrl)
    {
        if (text is not null) Text = text.Trim();
        if (description is not null) Description = description.Trim();
        if (imageUrl is not null) ImageUrl = imageUrl.Trim() == "" ? null : imageUrl.Trim();
        UpdateDate = DateTime.UtcNow;
    }
}
