using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Job Hangfire recorrente diário (08:00 horário de Brasília / 11:00 UTC).
/// Para cada grupo, envia uma notificação a todos os membros quando um jogador
/// ativo vinculado a uma conta de usuário tem aniversário no dia.
/// </summary>
public sealed class BirthdayNotificationJob : IBirthdayNotificationJob
{
    // Brasil permanentemente UTC-3 desde o fim do horário de verão em 2019
    private static readonly TimeSpan BrasilOffset = TimeSpan.FromHours(-3);

    private readonly AppDbContext _db;
    private readonly IPushService _push;
    private readonly ILogger<BirthdayNotificationJob> _logger;

    public BirthdayNotificationJob(
        AppDbContext db,
        IPushService push,
        ILogger<BirthdayNotificationJob> logger)
    {
        _db     = db;
        _push   = push;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var nowBrasil = DateTimeOffset.UtcNow.ToOffset(BrasilOffset);
        int month = nowBrasil.Month;
        int day   = nowBrasil.Day;

        // Encontra IDs de usuários com aniversário hoje (mês + dia)
        var birthdayUserIds = await _db.Users
            .AsNoTracking()
            .Where(u => u.BirthDate != null
                     && u.BirthDate.Value.Month == month
                     && u.BirthDate.Value.Day   == day)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (birthdayUserIds.Count == 0)
        {
            _logger.LogInformation("BirthdayNotificationJob: nenhum aniversariante hoje ({Date:dd/MM}).", nowBrasil);
            return;
        }

        // Jogadores ativos (não-convidados) vinculados a esses usuários
        var birthdayPlayers = await _db.Players
            .AsNoTracking()
            .Where(p => !p.IsGuest
                     && p.UserId.HasValue
                     && birthdayUserIds.Contains(p.UserId.Value)
                     && p.Status == Status.Active)
            .Select(p => new { p.Name, p.GroupId })
            .ToListAsync(ct);

        if (birthdayPlayers.Count == 0)
        {
            _logger.LogInformation("BirthdayNotificationJob: usuários aniversariantes encontrados mas sem jogadores ativos.");
            return;
        }

        // Agrupa por patota e envia uma notificação por jogador aniversariante
        var byGroup = birthdayPlayers.GroupBy(p => p.GroupId);
        int sent = 0;

        foreach (var grp in byGroup)
        {
            foreach (var player in grp)
            {
                try
                {
                    await _push.SendToGroupAsync(
                        grp.Key,
                        title: "🎂 Aniversário hoje!",
                        body:  $"Hoje é aniversário de {player.Name}!",
                        data:  new Dictionary<string, string>
                        {
                            ["type"]    = "birthday",
                            ["groupId"] = grp.Key.ToString(),
                        },
                        ct);

                    sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "BirthdayNotificationJob: falha ao enviar notificação de aniversário de {Player} no grupo {GroupId}.",
                        player.Name, grp.Key);
                }
            }
        }

        _logger.LogInformation(
            "BirthdayNotificationJob: {Count} notificação(ões) de aniversário enviada(s) em {Date:dd/MM}.",
            sent, nowBrasil);
    }
}
