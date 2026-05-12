namespace BratnavaFC.Domain.Entities;

public class PlayerEntity : InactivatableEntity
{
    public string Name { get; private set; } = null!;
    public Guid? UserId { get; private set; }
    public UserEntity? User { get; private set; }
    public Guid GroupId { get; private set; }
    public GroupEntity Group { get; private set; } = null!;
    public decimal SkillPoints { get; private set; }
    public bool IsGoalkeeper { get; private set; }
    public bool IsGuest { get; private set; }

    /// <summary>
    /// Data em que o jogador foi vinculado como mensalista (aceitou o convite).
    /// Nulo para jogadores convidados ainda não vinculados.
    /// </summary>
    public DateTime? JoinedAt { get; private set; }

    private readonly List<MatchPlayerEntity> _matchPlayers = new();
    public IReadOnlyCollection<MatchPlayerEntity> MatchPlayers => _matchPlayers;

    // EF
    private PlayerEntity() { }

    public PlayerEntity(string name, Guid? userId, Guid groupId, decimal skillPoints, bool isGoalkeeper, bool isGuest = false, BratnavaFC.Domain.Enums.Status status = Enums.Status.Active)
    {
        Rename(name);
        if (userId.HasValue) SetUser(userId.Value);
        SetGroup(groupId);
        SetSkillPoints(skillPoints);
        SetGoalkeeper(isGoalkeeper);
        SetIsGuest(isGuest);
        // Jogador criado diretamente já é mensalista — data de entrada = data de criação
        if (!isGuest && userId.HasValue)
            JoinedAt = DateTime.UtcNow;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Player name is required.");

        Name = name.Trim();
    }

    public void SetUser(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new InvalidOperationException("UserId is required.");

        UserId = userId;
    }

    public void SetGroup(Guid groupId)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId is required.");

        GroupId = groupId;
    }

    public void SetSkillPoints(decimal skillPoints)
    {
        if (skillPoints < 0)
            throw new InvalidOperationException("SkillPoints cannot be negative.");

        SkillPoints = skillPoints;
    }

    public void SetGoalkeeper(bool isGoalkeeper)
    {
        IsGoalkeeper = isGoalkeeper;
    }

    public void SetIsGuest(bool isGuest)
    {
        IsGuest = isGuest;
    }

    /// <summary>Desvincula a conta de usuário — converte para convidado sem conta associada.</summary>
    public void ClearUser()
    {
        UserId = null;
    }

    /// <summary>Marca a data de entrada como mensalista. Chamado ao aceitar o convite.</summary>
    public void SetJoinedAt(DateTime joinedAt)
    {
        JoinedAt = joinedAt;
    }

    public int? GuestStarRating { get; private set; }

    public void SetGuestStarRating(int? stars)
    {
        if (stars.HasValue && (stars.Value < 1 || stars.Value > 5))
            throw new InvalidOperationException("GuestStarRating must be between 1 and 5.");

        GuestStarRating = stars;
    }

    // ── Admin ratings (0–10) ─────────────────────────────────────────────────

    /// <summary>Habilidade de ataque avaliada pelo admin (0–10).</summary>
    public int? AttackRating { get; private set; }

    /// <summary>Habilidade de defesa avaliada pelo admin (0–10).</summary>
    public int? DefenseRating { get; private set; }

    /// <summary>Avaliação geral avaliada pelo admin (0–10). Tem peso maior no W_base.</summary>
    public int? OverallRating { get; private set; }

    public void SetAttackRating(int? rating)
    {
        if (rating.HasValue && (rating.Value < 0 || rating.Value > 10))
            throw new InvalidOperationException("AttackRating must be between 0 and 10.");
        AttackRating = rating;
    }

    public void SetDefenseRating(int? rating)
    {
        if (rating.HasValue && (rating.Value < 0 || rating.Value > 10))
            throw new InvalidOperationException("DefenseRating must be between 0 and 10.");
        DefenseRating = rating;
    }

    public void SetOverallRating(int? rating)
    {
        if (rating.HasValue && (rating.Value < 0 || rating.Value > 10))
            throw new InvalidOperationException("OverallRating must be between 0 and 10.");
        OverallRating = rating;
    }
}
