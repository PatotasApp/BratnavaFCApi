namespace BratnavaFC.Domain.Entities;

public class PollOptionImageEntity : BaseEntity
{
    public Guid OptionId { get; private set; }
    public PollOptionEntity? Option { get; private set; }
    public string ImageUrl { get; private set; } = null!;
    public int SortOrder { get; private set; }

    private PollOptionImageEntity() { }

    public PollOptionImageEntity(Guid optionId, string imageUrl, int sortOrder = 0)
    {
        if (optionId == Guid.Empty) throw new InvalidOperationException("OptionId é obrigatório.");
        if (string.IsNullOrWhiteSpace(imageUrl)) throw new InvalidOperationException("ImageUrl é obrigatório.");
        OptionId = optionId;
        ImageUrl = imageUrl.Trim();
        SortOrder = sortOrder;
    }
}
