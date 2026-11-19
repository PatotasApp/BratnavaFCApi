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

    public PuskasPollEntity(Guid groupId, IEnumerable<Guid> clipIds, bool countdown = true)
    {
        GroupId = groupId;
        Code = Guid.NewGuid().ToString("N");
        IsOpen = true;
        SetClips(clipIds, countdown);
    }

    private sealed record Payload(List<Guid> Ids, bool Countdown);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // Formato antigo: só um array de ids (contagem regressiva por padrão). Novo: { ids, countdown }.
    private Payload Read()
    {
        if (ClipIdsJson.TrimStart().StartsWith('['))
            return new Payload(JsonSerializer.Deserialize<List<Guid>>(ClipIdsJson) ?? [], true);
        return JsonSerializer.Deserialize<Payload>(ClipIdsJson, Json) ?? new Payload([], true);
    }

    /// <summary>Gols da votação na ordem do ranking (posição 1, 2, 3…).</summary>
    public IReadOnlyList<Guid> ClipIds => Read().Ids;

    /// <summary>O telão passa do último ao 1º; a folha de votação segue a mesma ordem.</summary>
    public bool Countdown => Read().Countdown;

    public void SetClips(IEnumerable<Guid> clipIds, bool? countdown = null)
    {
        ClipIdsJson = JsonSerializer.Serialize(new Payload(clipIds.Distinct().ToList(), countdown ?? Countdown));
        UpdateDate = DateTime.UtcNow;
    }

    public void SetOpen(bool isOpen)
    {
        IsOpen = isOpen;
        UpdateDate = DateTime.UtcNow;
    }
}
