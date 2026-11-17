using System.Text.Json;

namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Votação do Puskas de uma patota: guarda a lista de gols (clipes) como estava quando a
/// votação foi aberta, para a página dos votantes não mudar se as curtidas mudarem. O Code é
/// o segredo do QR code; só quem tem o link vota.
/// </summary>
public sealed class PuskasPollEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public string Code { get; private set; } = default!;
    public bool IsOpen { get; private set; }
    public string ClipIdsJson { get; private set; } = "[]";

    // EF Core
    private PuskasPollEntity() { }

    public PuskasPollEntity(Guid groupId, IEnumerable<Guid> clipIds)
    {
        GroupId = groupId;
        Code = Guid.NewGuid().ToString("N");
        IsOpen = true;
        SetClips(clipIds);
    }

    public IReadOnlyList<Guid> ClipIds => JsonSerializer.Deserialize<List<Guid>>(ClipIdsJson) ?? [];

    public void SetClips(IEnumerable<Guid> clipIds)
    {
        ClipIdsJson = JsonSerializer.Serialize(clipIds.Distinct().ToList());
        UpdateDate = DateTime.UtcNow;
    }

    public void SetOpen(bool isOpen)
    {
        IsOpen = isOpen;
        UpdateDate = DateTime.UtcNow;
    }
}
