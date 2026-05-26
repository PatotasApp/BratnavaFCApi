using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Polls;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class PollService : IPollService
{
    private readonly AppDbContext _db;
    private readonly IPushService _push;
    private readonly INotificationScheduler _scheduler;
    private readonly ILogger<PollService> _logger;

    public PollService(AppDbContext db, IPushService push, INotificationScheduler scheduler, ILogger<PollService> logger)
    {
        _db        = db;
        _push      = push;
        _scheduler = scheduler;
        _logger    = logger;
    }

    public async Task<Result<List<PollSummaryDto>>> GetPollsAsync(Guid groupId, Guid playerId, CancellationToken ct = default)
    {
        try
        {
            var polls = await _db.Polls
                .AsNoTracking()
                .Where(p => p.GroupId == groupId)
                .OrderByDescending(p => p.CreateDate)
                .Select(p => new
                {
                    p.Id, p.Title, p.Description, p.AllowMultipleVotes, p.ShowVotes, p.Status, p.CreateDate,
                    p.DeadlineDate, p.DeadlineTime,
                    p.Type, p.EventDate, p.EventTime, p.EventLocation, p.EventIcon, p.CostType, p.CostAmount,
                    p.LinkedMatchId,
                    OptionCount = p.Options.Count,
                    TotalVoters = p.Votes.Select(v => v.PlayerId).Distinct().Count(),
                    HasVoted = p.Votes.Any(v => v.PlayerId == playerId)
                })
                .ToListAsync(ct);

            var dtos = polls.Select(p => new PollSummaryDto
            {
                Id = p.Id,
                Title = p.Title,
                Description = p.Description,
                AllowMultipleVotes = p.AllowMultipleVotes,
                ShowVotes = p.ShowVotes,
                Status = p.Status,
                DeadlineDate = p.DeadlineDate?.ToString("yyyy-MM-dd"),
                DeadlineTime = p.DeadlineTime?.ToString("HH:mm"),
                Type = p.Type,
                EventDate = p.EventDate?.ToString("yyyy-MM-dd"),
                EventTime = p.EventTime?.ToString("HH:mm"),
                EventLocation = p.EventLocation,
                EventIcon = p.EventIcon,
                CostType = p.CostType,
                CostAmount = p.CostAmount,
                OptionCount = p.OptionCount,
                TotalVoters = p.TotalVoters,
                HasVoted = p.HasVoted,
                CreateDate = p.CreateDate,
                LinkedMatchId = p.LinkedMatchId
            }).ToList();

            return Result<List<PollSummaryDto>>.Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em GetPollsAsync.");
            return Result<List<PollSummaryDto>>.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result<PollDto>> GetPollAsync(Guid groupId, Guid pollId, Guid playerId, bool isAdmin, CancellationToken ct = default, bool skipImages = false)
    {
        try
        {
            // Poll header (never includes base64 images — those live in Options only)
            var poll = await _db.Polls
                .AsNoTracking()
                .Where(p => p.Id == pollId && p.GroupId == groupId)
                .Select(p => new
                {
                    p.Id, p.Title, p.Description, p.AllowMultipleVotes, p.ShowVotes,
                    p.Status, p.DeadlineDate, p.DeadlineTime, p.Type,
                    p.EventDate, p.EventTime, p.EventLocation, p.EventIcon,
                    p.CostType, p.CostAmount, p.CreateDate, p.LinkedMatchId
                })
                .FirstOrDefaultAsync(ct);

            if (poll is null)
                return Result<PollDto>.Fail("Votação não encontrada.");

            // Options — project to DTO; load images
            var options = await _db.PollOptions
                .AsNoTracking()
                .Where(o => o.PollId == pollId)
                .OrderBy(o => o.SortOrder).ThenBy(o => o.CreateDate)
                .Select(o => new
                {
                    o.Id, o.Text, o.Description, o.SortOrder,
                    Images = skipImages
                        ? new List<string>()
                        : o.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.CreateDate).Select(i => i.ImageUrl).ToList()
                })
                .ToListAsync(ct);

            // Votes + player names in one query
            var votesWithPlayer = await _db.PollVotes
                .AsNoTracking()
                .Where(v => v.PollId == pollId)
                .Join(_db.Players.AsNoTracking(), v => v.PlayerId, p => p.Id, (v, p) => new PollVoteDto
                {
                    OptionId = v.OptionId,
                    PlayerId = v.PlayerId,
                    PlayerName = p.Name
                })
                .ToListAsync(ct);

            var myVotes = votesWithPlayer.Where(v => v.PlayerId == playerId).Select(v => v.OptionId).ToList();
            var totalVoters = votesWithPlayer.Select(v => v.PlayerId).Distinct().Count();
            var optionVoteCounts = votesWithPlayer.GroupBy(v => v.OptionId).ToDictionary(g => g.Key, g => g.Count());

            List<PollMemberVoteDto>? members = null;
            if (isAdmin)
            {
                var allPlayers = await _db.Players
                    .AsNoTracking()
                    .Where(p => p.GroupId == groupId && !p.IsGuest && p.UserId != null && p.Status == Status.Active)
                    .OrderBy(p => p.Name)
                    .Select(p => new { p.Id, p.Name })
                    .ToListAsync(ct);

                var votesByPlayer = votesWithPlayer
                    .GroupBy(v => v.PlayerId)
                    .ToDictionary(g => g.Key, g => g.Select(v => v.OptionId).ToList());

                members = allPlayers.Select(p => new PollMemberVoteDto
                {
                    PlayerId = p.Id,
                    PlayerName = p.Name,
                    VotedOptionIds = votesByPlayer.TryGetValue(p.Id, out var ids) ? ids : new List<Guid>()
                }).ToList();
            }

            var dto = new PollDto
            {
                Id = poll.Id,
                Title = poll.Title,
                Description = poll.Description,
                AllowMultipleVotes = poll.AllowMultipleVotes,
                ShowVotes = poll.ShowVotes,
                Status = poll.Status,
                DeadlineDate = poll.DeadlineDate?.ToString("yyyy-MM-dd"),
                DeadlineTime = poll.DeadlineTime?.ToString("HH:mm"),
                Type = poll.Type,
                EventDate = poll.EventDate?.ToString("yyyy-MM-dd"),
                EventTime = poll.EventTime?.ToString("HH:mm"),
                EventLocation = poll.EventLocation,
                EventIcon = poll.EventIcon,
                CostType = poll.CostType,
                CostAmount = poll.CostAmount,
                CreateDate = poll.CreateDate,
                MyVotedOptionIds = myVotes,
                TotalVoters = totalVoters,
                LinkedMatchId = poll.LinkedMatchId,
                Votes = (poll.ShowVotes || isAdmin) ? votesWithPlayer : null,
                Options = options.Select(o => new PollOptionDto
                {
                    Id = o.Id,
                    Text = o.Text,
                    Description = o.Description,
                    Images = o.Images,
                    SortOrder = o.SortOrder,
                    VoteCount = optionVoteCounts.GetValueOrDefault(o.Id, 0)
                }).ToList(),
                Members = members
            };

            return Result<PollDto>.Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em GetPollAsync.");
            return Result<PollDto>.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result<PollDto>> CreatePollAsync(Guid groupId, Guid userId, CreatePollDto dto, CancellationToken ct = default)
    {
        try
        {
            if (!await _db.Groups.AnyAsync(g => g.Id == groupId, ct))
                return Result<PollDto>.Fail("Grupo não encontrado.");

            DateOnly? deadlineDate = dto.DeadlineDate is not null && DateOnly.TryParse(dto.DeadlineDate, out var dd) ? dd : null;
            TimeOnly? deadlineTime = dto.DeadlineTime is not null && TimeOnly.TryParse(dto.DeadlineTime, out var dt) ? dt : null;
            var poll = new PollEntity(groupId, dto.Title, dto.Description, dto.AllowMultipleVotes, dto.ShowVotes, userId, deadlineDate, deadlineTime);
            _db.Polls.Add(poll);

            if (dto.AddToCalendar && deadlineDate.HasValue)
            {
                var reminder = new CalendarEventEntity(
                    groupId,
                    $"Encerramento: {dto.Title}",
                    dto.Description,
                    categoryId: null,
                    eventDate: deadlineDate.Value,
                    eventTime: deadlineTime,
                    timeTbd: deadlineTime is null,
                    createdByUserId: userId,
                    icon: "🗳️");
                _db.CalendarEvents.Add(reminder);
            }

            await _db.SaveChangesAsync(ct);

            await NotifyPollCreatedAsync(groupId, poll.Id, dto.Title, ct);
            await _scheduler.SchedulePollRemindersAsync(poll.Id, groupId, dto.Title, deadlineDate, deadlineTime, ct);

            return await GetPollAsync(groupId, poll.Id, Guid.Empty, true, ct, skipImages: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em CreatePollAsync.");
            return Result<PollDto>.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result<PollDto>> CreateEventPollAsync(Guid groupId, Guid userId, CreateEventPollDto dto, CancellationToken ct = default)
    {
        try
        {
            if (!await _db.Groups.AnyAsync(g => g.Id == groupId, ct))
                return Result<PollDto>.Fail("Grupo não encontrado.");

            if (!DateOnly.TryParse(dto.EventDate, out var eventDate))
                return Result<PollDto>.Fail("Data do evento é obrigatória.");

            TimeOnly? eventTime = dto.EventTime is not null && TimeOnly.TryParse(dto.EventTime, out var et) ? et : null;
            DateOnly? deadlineDate = dto.DeadlineDate is not null && DateOnly.TryParse(dto.DeadlineDate, out var dd) ? dd : null;
            TimeOnly? deadlineTime = dto.DeadlineTime is not null && TimeOnly.TryParse(dto.DeadlineTime, out var dt) ? dt : null;

            var poll = new PollEntity(
                groupId, dto.Title, dto.Description,
                allowMultipleVotes: false, showVotes: dto.ShowVotes,
                userId, deadlineDate, deadlineTime,
                type: "event", eventDate: eventDate, eventTime: eventTime,
                eventLocation: dto.EventLocation, eventIcon: dto.EventIcon,
                costType: dto.CostType, costAmount: dto.CostAmount);

            _db.Polls.Add(poll);
            _db.PollOptions.Add(new PollOptionEntity(poll.Id, "Sim", null, null, 0));
            _db.PollOptions.Add(new PollOptionEntity(poll.Id, "Talvez", null, null, 1));
            _db.PollOptions.Add(new PollOptionEntity(poll.Id, "Não", null, null, 2));
            await _db.SaveChangesAsync(ct);

            await NotifyEventPollCreatedAsync(groupId, poll.Id, dto.Title, ct);
            await _scheduler.SchedulePollRemindersAsync(poll.Id, groupId, dto.Title, deadlineDate, deadlineTime, ct);

            return await GetPollAsync(groupId, poll.Id, Guid.Empty, true, ct, skipImages: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em CreateEventPollAsync.");
            return Result<PollDto>.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result> ClosePollAsync(Guid groupId, Guid pollId, Guid userId, ClosePollDto dto, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result.Fail("Votação não encontrada.");

            poll.Close();

            string? createdEventTitle = null;
            DateOnly? createdEventDate = null;
            TimeOnly? createdEventTime = null;

            if (dto.CreateEvent && !string.IsNullOrWhiteSpace(dto.EventTitle) && !string.IsNullOrWhiteSpace(dto.EventDate)
                && DateOnly.TryParse(dto.EventDate, out var eventDate))
            {
                createdEventTime = dto.EventTime is not null && TimeOnly.TryParse(dto.EventTime, out var et) ? et : null;
                Guid? categoryId = dto.CategoryId is not null && Guid.TryParse(dto.CategoryId, out var cid) ? cid : null;
                var calendarEvent = new CalendarEventEntity(
                    groupId, dto.EventTitle, dto.EventDescription, categoryId,
                    eventDate, createdEventTime, false, userId, dto.EventIcon);
                _db.CalendarEvents.Add(calendarEvent);
                createdEventTitle = dto.EventTitle;
                createdEventDate  = eventDate;
            }

            await _db.SaveChangesAsync(ct);

            await NotifyPollClosedAsync(groupId, pollId, poll.Title, ct);

            if (createdEventTitle is not null && createdEventDate.HasValue)
                await NotifyEventCreatedFromPollAsync(groupId, createdEventTitle, createdEventDate.Value, createdEventTime, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em ClosePollAsync.");
            return Result.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result> ReopenPollAsync(Guid groupId, Guid pollId, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result.Fail("Votação não encontrada.");

            poll.Reopen();
            await _db.SaveChangesAsync(ct);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em ReopenPollAsync.");
            return Result.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result> SetShowVotesAsync(Guid groupId, Guid pollId, bool showVotes, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result.Fail("Votação não encontrada.");

            poll.Update(null, null, null, showVotes, null, null);
            await _db.SaveChangesAsync(ct);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em SetShowVotesAsync.");
            return Result.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result> DeletePollAsync(Guid groupId, Guid pollId, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result.Ok(); // já excluído — idempotente
            await _scheduler.CancelPollRemindersAsync(pollId, ct);
            _db.Polls.Remove(poll);
            await _db.SaveChangesAsync(ct);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em DeletePollAsync.");
            return Result.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result<PollOptionDto>> AddOptionAsync(Guid groupId, Guid pollId, AddPollOptionDto dto, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result<PollOptionDto>.Fail("Votação não encontrada.");
            if (poll.IsEventType()) return Result<PollOptionDto>.Fail("As opções de eventos não podem ser modificadas.");

            var nextOrder = await _db.PollOptions.Where(o => o.PollId == pollId).CountAsync(ct);
            var option = new PollOptionEntity(poll.Id, dto.Text, dto.Description, null, nextOrder);
            _db.PollOptions.Add(option);
            await _db.SaveChangesAsync(ct);

            for (int i = 0; i < dto.Images.Count; i++)
                _db.PollOptionImages.Add(new PollOptionImageEntity(option.Id, dto.Images[i], i));
            if (dto.Images.Count > 0)
                await _db.SaveChangesAsync(ct);

            return Result<PollOptionDto>.Ok(MapOption(option, 0, dto.Images));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em AddOptionAsync.");
            return Result<PollOptionDto>.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result<PollOptionDto>> UpdateOptionAsync(Guid groupId, Guid pollId, Guid optionId, UpdatePollOptionDto dto, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result<PollOptionDto>.Fail("Votação não encontrada.");
            if (poll.IsEventType()) return Result<PollOptionDto>.Fail("As opções de eventos não podem ser modificadas.");

            var option = await _db.PollOptions.FirstOrDefaultAsync(o => o.Id == optionId && o.PollId == pollId, ct);
            if (option is null) return Result<PollOptionDto>.Fail("Opção não encontrada.");

            option.Update(dto.Text, dto.Description);

            List<string> finalImages;
            if (dto.Images is not null)
            {
                // Replace images
                var existing = await _db.PollOptionImages.Where(i => i.OptionId == optionId).ToListAsync(ct);
                _db.PollOptionImages.RemoveRange(existing);
                for (int i = 0; i < dto.Images.Count; i++)
                    _db.PollOptionImages.Add(new PollOptionImageEntity(optionId, dto.Images[i], i));
                finalImages = dto.Images;
            }
            else
            {
                finalImages = await _db.PollOptionImages
                    .Where(i => i.OptionId == optionId)
                    .OrderBy(i => i.SortOrder)
                    .Select(i => i.ImageUrl)
                    .ToListAsync(ct);
            }

            await _db.SaveChangesAsync(ct);
            var voteCount = await _db.PollVotes.CountAsync(v => v.OptionId == optionId, ct);
            return Result<PollOptionDto>.Ok(MapOption(option, voteCount, finalImages));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em UpdateOptionAsync.");
            return Result<PollOptionDto>.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result> DeleteOptionAsync(Guid groupId, Guid pollId, Guid optionId, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result.Fail("Votação não encontrada.");
            if (poll.IsEventType()) return Result.Fail("As opções de eventos não podem ser modificadas.");

            var option = await _db.PollOptions.FirstOrDefaultAsync(o => o.Id == optionId && o.PollId == pollId, ct);
            if (option is null) return Result.Ok(); // já excluído — idempotente
            _db.PollOptions.Remove(option);
            await _db.SaveChangesAsync(ct);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em DeleteOptionAsync.");
            return Result.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result<PollDto>> CastVoteAsync(Guid groupId, Guid pollId, Guid playerId, CastVoteDto dto, bool isAdmin = false, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result<PollDto>.Fail("Votação não encontrada.");

            var voteError = poll.ValidateVote(dto.OptionIds.Count);
            if (voteError is not null) return Result<PollDto>.Fail(voteError);

            var validOptionIds = await _db.PollOptions.Where(o => o.PollId == pollId).Select(o => o.Id).ToListAsync(ct);
            if (dto.OptionIds.Any(id => !validOptionIds.Contains(id)))
                return Result<PollDto>.Fail("Opção inválida.");

            var existingVotes = await _db.PollVotes.Where(v => v.PollId == pollId && v.PlayerId == playerId).ToListAsync(ct);
            _db.PollVotes.RemoveRange(existingVotes);

            foreach (var optionId in dto.OptionIds.Distinct())
                _db.PollVotes.Add(new PollVoteEntity(pollId, optionId, playerId));

            await _db.SaveChangesAsync(ct);
            return await GetPollAsync(groupId, pollId, playerId, isAdmin, ct, skipImages: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em CastVoteAsync.");
            return Result<PollDto>.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result<PollDto>> RemoveVoteAsync(Guid groupId, Guid pollId, Guid playerId, bool isAdmin = false, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result<PollDto>.Fail("Votação não encontrada.");

            var changeError = poll.ValidateVoteChange();
            if (changeError is not null) return Result<PollDto>.Fail(changeError);

            var votes = await _db.PollVotes.Where(v => v.PollId == pollId && v.PlayerId == playerId).ToListAsync(ct);
            _db.PollVotes.RemoveRange(votes);
            await _db.SaveChangesAsync(ct);
            return await GetPollAsync(groupId, pollId, playerId, isAdmin, ct, skipImages: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em RemoveVoteAsync.");
            return Result<PollDto>.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result<PollDto>> AdminCastVoteAsync(Guid groupId, Guid pollId, AdminCastVoteDto dto, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result<PollDto>.Fail("Votação não encontrada.");

            if (!poll.AllowMultipleVotes && dto.OptionIds.Count > 1)
                return Result<PollDto>.Fail("Esta votação permite apenas uma opção.");

            if (dto.OptionIds.Count > 0)
            {
                var validOptionIds = await _db.PollOptions.Where(o => o.PollId == pollId).Select(o => o.Id).ToListAsync(ct);
                if (dto.OptionIds.Any(id => !validOptionIds.Contains(id)))
                    return Result<PollDto>.Fail("Opção inválida.");
            }

            var existingVotes = await _db.PollVotes.Where(v => v.PollId == pollId && v.PlayerId == dto.PlayerId).ToListAsync(ct);
            _db.PollVotes.RemoveRange(existingVotes);

            foreach (var optionId in dto.OptionIds.Distinct())
                _db.PollVotes.Add(new PollVoteEntity(pollId, optionId, dto.PlayerId));

            await _db.SaveChangesAsync(ct);
            return await GetPollAsync(groupId, pollId, Guid.Empty, true, ct, skipImages: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em AdminCastVoteAsync.");
            return Result<PollDto>.Fail("Erro interno. Tente novamente.");
        }
    }

    public async Task<Result> UpdateDeadlineAsync(Guid groupId, Guid pollId, UpdatePollDeadlineDto dto, CancellationToken ct = default)
    {
        try
        {
            var poll = await _db.Polls.FirstOrDefaultAsync(p => p.Id == pollId && p.GroupId == groupId, ct);
            if (poll is null) return Result.Fail("Votação não encontrada.");

            if (dto.ClearDeadline)
            {
                poll.SetDeadline(null, null);
            }
            else
            {
                DateOnly? deadlineDate = dto.DeadlineDate is not null && DateOnly.TryParse(dto.DeadlineDate, out var dd) ? dd : null;
                TimeOnly? deadlineTime = dto.DeadlineTime is not null && TimeOnly.TryParse(dto.DeadlineTime, out var dt) ? dt : null;
                poll.SetDeadline(deadlineDate, deadlineTime);
            }

            // Se estava encerrada manualmente, reabrir — não faz sentido estender
            // (ou remover) o prazo e manter a votação fechada.
            if (poll.Status == "closed")
                poll.Reopen();

            await _db.SaveChangesAsync(ct);
            await _scheduler.ReschedulePollRemindersAsync(
                pollId, groupId, poll.Title, poll.DeadlineDate, poll.DeadlineTime, ct);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em UpdateDeadlineAsync.");
            return Result.Fail("Erro interno. Tente novamente.");
        }
    }

    // ── Notificações ──────────────────────────────────────────────────────────

    private Task NotifyPollCreatedAsync(Guid groupId, Guid pollId, string title, CancellationToken ct) =>
        _push.SendToGroupAsync(
            groupId,
            title: $"Nova votação: {title}",
            body:  "Uma nova votação foi criada.",
            data:  new Dictionary<string, string> { ["type"] = "poll_created", ["groupId"] = groupId.ToString(), ["pollId"] = pollId.ToString() },
            ct);

    private Task NotifyEventPollCreatedAsync(Guid groupId, Guid pollId, string title, CancellationToken ct) =>
        _push.SendToGroupAsync(
            groupId,
            title: $"Novo evento para votar: {title}",
            body:  "Vote se você vai participar do evento!",
            data:  new Dictionary<string, string> { ["type"] = "poll_created", ["groupId"] = groupId.ToString(), ["pollId"] = pollId.ToString() },
            ct);

    private Task NotifyPollClosedAsync(Guid groupId, Guid pollId, string pollTitle, CancellationToken ct) =>
        _push.SendToGroupAsync(
            groupId,
            title: "Votação encerrada!",
            body:  $"A votação \"{pollTitle}\" foi encerrada. Confira os resultados.",
            data:  new Dictionary<string, string> { ["type"] = "poll_closed", ["groupId"] = groupId.ToString(), ["pollId"] = pollId.ToString() },
            ct);

    private Task NotifyEventCreatedFromPollAsync(
        Guid groupId, string eventTitle, DateOnly date, TimeOnly? time, CancellationToken ct)
    {
        var timeStr = time.HasValue ? $" às {time.Value:HH:mm}" : string.Empty;
        return _push.SendToGroupAsync(
            groupId,
            title: $"Novo evento: {eventTitle}",
            body:  $"Evento confirmado para {date:dd/MM/yyyy}{timeStr}.",
            data:  new Dictionary<string, string> { ["type"] = "event_created", ["groupId"] = groupId.ToString() },
            ct);
    }

    // ── Mapeamento ────────────────────────────────────────────────────────────

    private static PollOptionDto MapOption(PollOptionEntity option, int voteCount, List<string>? images = null) => new()
    {
        Id        = option.Id,
        Text      = option.Text,
        Description = option.Description,
        Images    = images ?? new List<string>(),
        SortOrder = option.SortOrder,
        VoteCount = voteCount,
    };
}
