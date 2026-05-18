namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Notificação persistida por usuário, exibida no sininho do app e do site.
/// Criada automaticamente pelo PushService a cada envio de push notification.
/// </summary>
public class UserNotificationEntity : BaseEntity
{
    // EF
    private UserNotificationEntity() { }

    public UserNotificationEntity(
        Guid userId, Guid? groupId,
        string title, string body,
        string? type, string? dataJson)
    {
        UserId   = userId;
        GroupId  = groupId;
        Title    = title;
        Body     = body;
        Type     = type;
        DataJson = dataJson;
        IsRead   = false;
    }

    public Guid   UserId   { get; private set; }
    public Guid?  GroupId  { get; private set; }
    public string Title    { get; private set; } = null!;
    public string Body     { get; private set; } = null!;

    /// <summary>Tipo do push (ex: "match_invite", "poll_reminder"). Usado para routing no app.</summary>
    public string? Type     { get; private set; }

    /// <summary>Payload extra serializado em JSON (ex: matchId, pollId).</summary>
    public string? DataJson  { get; private set; }

    public bool      IsRead  { get; private set; }
    public DateTime? ReadAt  { get; private set; }

    public void MarkAsRead()
    {
        if (IsRead) return;
        IsRead     = true;
        ReadAt     = DateTime.UtcNow;
        UpdateDate = DateTime.UtcNow;
    }
}
