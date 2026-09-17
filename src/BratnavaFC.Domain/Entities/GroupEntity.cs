namespace BratnavaFC.Domain.Entities;

public class GroupEntity : InactivatableEntity
{
    public string Name { get; private set; } = null!;
    public DateTimeOffset? ScheduleMatchDate { get; private set; }
    public Guid CreatedByUserId { get; private set; }
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

    public GroupEntity(string name, DateTimeOffset? scheduleMatchDate, Guid createdByUserId)
    {
        Rename(name);
        Reschedule(scheduleMatchDate);
        CreatedByUserId = createdByUserId;
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
