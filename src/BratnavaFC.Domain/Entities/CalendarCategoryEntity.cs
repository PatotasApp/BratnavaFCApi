namespace BratnavaFC.Domain.Entities;

public class CalendarCategoryEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Color { get; private set; }   // hex: "#3b82f6"
    public string? Icon { get; private set; }    // emoji ou "lucide:Cake"
    public bool IsSystem { get; private set; }   // não pode ser excluída pelo admin

    // EF
    private CalendarCategoryEntity() { }

    public CalendarCategoryEntity(Guid groupId, string name, string? color, string? icon, bool isSystem = false)
    {
        SetGroup(groupId);
        Rename(name);
        SetColor(color);
        SetIcon(icon);
        IsSystem = isSystem;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Category name is required.");
        Name = name.Trim();
    }

    public void SetColor(string? color) => Color = color?.Trim();
    public void SetIcon(string? icon) => Icon = icon?.Trim();

    private void SetGroup(Guid groupId)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId is required.");
        GroupId = groupId;
    }
}
