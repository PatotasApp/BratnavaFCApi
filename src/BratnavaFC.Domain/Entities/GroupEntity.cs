namespace BratnavaFC.Domain.Entities;

public class GroupEntity : InactivatableEntity
{
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public bool IsPublic { get; private set; } = true;
    public bool IsAcceptingPlayers { get; private set; }
    public bool ShowMembersOnProfile { get; private set; }
    /// <summary>Se verdadeiro, convidados de fora veem a lista de gols das partidas em que jogaram.</summary>
    public bool ShowGoalsToGuests { get; private set; }
    public string? ProfileDescription { get; private set; }
    public string? ProfileCity { get; private set; }
    public DateTimeOffset? ScheduleMatchDate { get; private set; }
    /// <summary>
    /// Dono da patota. NULO significa que o criador excluiu a conta — a patota continua
    /// existindo e sendo administrada pelos admins, só deixa de ter dono.
    ///
    /// É posse, não autoria: o <see cref="TransferCreator"/> reaponta este campo quando
    /// alguém passa a patota adiante. Por isso ele nunca vira uma lápide com nome; quem
    /// exibe mostra "Usuário deletado" quando encontra nulo.
    /// </summary>
    public Guid? CreatedByUserId { get; private set; }
    /// <summary>
    /// Caminho do objeto no bucket de imagens do R2, no formato
    /// <c>logos/{groupId}/{guid}.png</c>. Guardamos a KEY, não a URL: o host público vive em
    /// configuração, então trocar o r2.dev por um domínio custom é mudança de config, não
    /// UPDATE em toda a tabela. Ver <see cref="UserEntity.ProfilePhotoKey"/>.
    /// </summary>
    public string? LogoKey { get; private set; }

    public DateTimeOffset? LogoUpdatedAt { get; private set; }

    private readonly List<PlayerEntity> _players = [];
    public IReadOnlyCollection<PlayerEntity> Players => _players;

    private readonly List<GroupAdminEntity> _admins = [];
    public IReadOnlyCollection<GroupAdminEntity> Admins => _admins;

    private readonly List<GroupFinanceiroEntity> _financeiros = [];
    public IReadOnlyCollection<GroupFinanceiroEntity> Financeiros => _financeiros;

    // EF
    private GroupEntity() { }

    public GroupEntity(string name, DateTimeOffset? scheduleMatchDate, Guid createdByUserId, string? code = null)
    {
        Rename(name);
        Reschedule(scheduleMatchDate);
        CreatedByUserId = createdByUserId;
        Code = string.IsNullOrWhiteSpace(code) ? GenerateCode() : NormalizeCode(code);
    }

    public void UpdateProfile(bool isPublic, bool isAcceptingPlayers, bool showMembers,
        string? description, string? city, bool showGoalsToGuests = false)
    {
        ShowGoalsToGuests = showGoalsToGuests;
        IsPublic = isPublic;
        IsAcceptingPlayers = isAcceptingPlayers;
        ShowMembersOnProfile = showMembers;
        ProfileDescription = NormalizeOptional(description, 500);
        ProfileCity = NormalizeOptional(city, 100);
    }

    public void RegenerateCode(string code) => Code = NormalizeCode(code);

    public static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }

    private static string NormalizeCode(string value)
    {
        var code = value.Trim().ToUpperInvariant();
        if (code.Length != 8 || code.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c)))
            throw new InvalidOperationException("Group code must have 8 uppercase alphanumeric characters.");
        return code;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new InvalidOperationException($"Value must have at most {maxLength} characters.");
        return normalized;
    }

    public void AddPlayer(PlayerEntity player)
    {
        if (player == null)
            throw new InvalidOperationException("Player is required.");

        if(_players.Any(p => p.UserId == player.UserId))
            throw new InvalidOperationException("Player already exists in the group.");

        _players.Add(player);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Group name is required.");

        Name = name.Trim();
    }

    public void Reschedule(DateTimeOffset? scheduleMatchDate)
    {
        ScheduleMatchDate = scheduleMatchDate;
    }

    public void SetLogo(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new InvalidOperationException("Group logo object key is required.");

        LogoKey = objectKey.Trim();
        LogoUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RemoveLogo()
    {
        LogoKey = null;
        LogoUpdatedAt = null;
    }

    public void SetAdmins(IEnumerable<Guid> userAdminIds)
    {
        if (userAdminIds == null) throw new InvalidOperationException("Admins list is required.");

        _admins.Clear();

        foreach (var adminId in userAdminIds.Distinct())
        {
            _admins.Add(new GroupAdminEntity
            {
                GroupId = Id,
                UserId = adminId
            });
        }
    }

    public void RemoveAdmin(Guid userId)
    {
        if (userId == CreatedByUserId)
            throw new InvalidOperationException("The group creator cannot be removed from admins.");

        var admin = _admins.FirstOrDefault(a => a.UserId == userId);
        if (admin == null)
            throw new InvalidOperationException("User is not an admin of this group.");

        _admins.Remove(admin);
    }

    public void AddFinanceiro(Guid userId)
    {
        if (_financeiros.Any(f => f.UserId == userId)) return;
        _financeiros.Add(new GroupFinanceiroEntity { GroupId = Id, UserId = userId });
    }

    public void RemoveFinanceiro(Guid userId)
    {
        var fin = _financeiros.FirstOrDefault(f => f.UserId == userId);
        if (fin == null)
            throw new InvalidOperationException("User is not a financeiro of this group.");
        _financeiros.Remove(fin);
    }

    public void TransferCreator(Guid newCreatorUserId)
    {
        if (!_admins.Any(a => a.UserId == newCreatorUserId))
            throw new InvalidOperationException("The new creator must be an existing admin of the group.");

        CreatedByUserId = newCreatorUserId;
    }
}
