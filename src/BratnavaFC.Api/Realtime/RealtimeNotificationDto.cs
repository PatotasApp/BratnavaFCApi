namespace BratnavaFC.Api.Realtime;

/// <summary>
/// Payload do evento de sininho. Vai por um método de hub próprio
/// (<c>NotificationEvent</c>), separado do <c>RealtimeEvent</c> dos eventos de grupo: as
/// formas são diferentes — este é escopado ao usuário e o <c>GroupId</c> é opcional — e
/// misturar as duas no mesmo método obrigaria o cliente a adivinhar o formato.
/// </summary>
public sealed record RealtimeNotificationDto(
    string Type,
    Guid? GroupId,
    string Title,
    string? NotificationType,
    DateTime OccurredAtUtc);
