using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Testes da paginação introduzida nas listagens que crescem sem limite:
/// polls, replays, bet history, extra charges (+summary) e groups (GodMode).
/// </summary>
public sealed class PaginationTests
{
    // ─── Polls ────────────────────────────────────────────────────────────────

    private static PollService MakePollSut(AppDbContext db)
        => new(db, Mock.Of<IPushService>(), Mock.Of<INotificationScheduler>(), Mock.Of<ILogger<PollService>>());

    private static PollEntity MakePoll(Guid groupId, string title, string type = "poll", bool closed = false)
    {
        var poll = new PollEntity(groupId, title, null, allowMultipleVotes: false, showVotes: false,
            createdByUserId: Guid.NewGuid(), type: type);
        if (closed) poll.Close();
        return poll;
    }

    private static void SetCreateDate(AppDbContext db, BaseEntity entity, DateTime date)
        => db.Entry(entity).Property(nameof(BaseEntity.CreateDate)).CurrentValue = date;

    [Fact]
    public async Task GetPollsAsync_ShouldPaginateNewestFirstAndReportTotal()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollsAsync_ShouldPaginateNewestFirstAndReportTotal));
        var groupId = Guid.NewGuid();

        for (var i = 1; i <= 5; i++)
        {
            var poll = MakePoll(groupId, $"P{i}");
            db.Polls.Add(poll);
            SetCreateDate(db, poll, DateTime.UtcNow.AddDays(-i)); // P1 é a mais recente
        }
        await db.SaveChangesAsync();

        var sut = MakePollSut(db);

        var page1 = await sut.GetPollsAsync(groupId, Guid.NewGuid(), page: 1, pageSize: 2);
        page1.Data!.Total.Should().Be(5);
        page1.Data.Items.Select(p => p.Title).Should().ContainInOrder("P1", "P2");

        var page3 = await sut.GetPollsAsync(groupId, Guid.NewGuid(), page: 3, pageSize: 2);
        page3.Data!.Items.Should().HaveCount(1);
        page3.Data.Items[0].Title.Should().Be("P5");
    }

    [Fact]
    public async Task GetPollsAsync_WithTypeFilter_ShouldReturnOnlyMatchingTypeWithCorrectTotal()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollsAsync_WithTypeFilter_ShouldReturnOnlyMatchingTypeWithCorrectTotal));
        var groupId = Guid.NewGuid();

        db.Polls.AddRange(
            MakePoll(groupId, "Votação 1"),
            MakePoll(groupId, "Votação 2"),
            MakePoll(groupId, "Evento 1", type: "event"));
        await db.SaveChangesAsync();

        var sut = MakePollSut(db);

        var polls  = await sut.GetPollsAsync(groupId, Guid.NewGuid(), type: "poll");
        polls.Data!.Total.Should().Be(2);
        polls.Data.Items.Should().OnlyContain(p => p.Type == "poll");

        var events = await sut.GetPollsAsync(groupId, Guid.NewGuid(), type: "event");
        events.Data!.Total.Should().Be(1);
        events.Data.Items[0].Title.Should().Be("Evento 1");
    }

    [Fact]
    public async Task GetPollsAsync_WithStatusFilter_ShouldReturnOnlyMatchingStatus()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollsAsync_WithStatusFilter_ShouldReturnOnlyMatchingStatus));
        var groupId = Guid.NewGuid();

        db.Polls.AddRange(
            MakePoll(groupId, "Aberta 1"),
            MakePoll(groupId, "Aberta 2"),
            MakePoll(groupId, "Fechada 1", closed: true));
        await db.SaveChangesAsync();

        var sut = MakePollSut(db);

        var open = await sut.GetPollsAsync(groupId, Guid.NewGuid(), status: "open");
        open.Data!.Total.Should().Be(2);
        open.Data.Items.Should().OnlyContain(p => p.Status == "open");

        var closed = await sut.GetPollsAsync(groupId, Guid.NewGuid(), status: "closed");
        closed.Data!.Total.Should().Be(1);
        closed.Data.Items[0].Title.Should().Be("Fechada 1");
    }

    [Fact]
    public async Task GetPollsAsync_WithTypeAndStatus_ShouldCombineFiltersAndPaginate()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollsAsync_WithTypeAndStatus_ShouldCombineFiltersAndPaginate));
        var groupId = Guid.NewGuid();

        // 3 eventos fechados + ruído (evento aberto, votação fechada)
        for (var i = 1; i <= 3; i++)
        {
            var ev = MakePoll(groupId, $"Evento fechado {i}", type: "event", closed: true);
            db.Polls.Add(ev);
            SetCreateDate(db, ev, DateTime.UtcNow.AddDays(-i));
        }
        db.Polls.Add(MakePoll(groupId, "Evento aberto", type: "event"));
        db.Polls.Add(MakePoll(groupId, "Votação fechada", type: "poll", closed: true));
        await db.SaveChangesAsync();

        var sut = MakePollSut(db);

        var page1 = await sut.GetPollsAsync(groupId, Guid.NewGuid(), page: 1, pageSize: 2, type: "event", status: "closed");
        page1.Data!.Total.Should().Be(3, "só eventos fechados contam");
        page1.Data.Items.Should().HaveCount(2);
        page1.Data.Items.Should().OnlyContain(p => p.Type == "event" && p.Status == "closed");

        var page2 = await sut.GetPollsAsync(groupId, Guid.NewGuid(), page: 2, pageSize: 2, type: "event", status: "closed");
        page2.Data!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPollsAsync_ShouldClampOversizedPageSize()
    {
        await using var db = DbContextFactory.Create(nameof(GetPollsAsync_ShouldClampOversizedPageSize));
        var groupId = Guid.NewGuid();
        db.Polls.Add(MakePoll(groupId, "P1"));
        await db.SaveChangesAsync();

        var sut    = MakePollSut(db);
        var result = await sut.GetPollsAsync(groupId, Guid.NewGuid(), page: 1, pageSize: 5000);

        result.Data!.PageSize.Should().Be(100, "pageSize deve ser limitado a 100");
    }

    // ─── Replays ──────────────────────────────────────────────────────────────

    private static MatchService MakeMatchSut(AppDbContext db)
        => new(db, new RepositoryBase<MatchEntity>(db), Mock.Of<IPushService>(),
               Mock.Of<IReplayUrlService>(), Mock.Of<IBetService>(), Mock.Of<INotificationScheduler>(), TestImageStorage.Create());

    private static ReplayClipEntity MakeClip(Guid groupId, Guid matchId, string key, DateTimeOffset recordedAt) =>
        new(groupId, matchId, "goal-replays", key, "video/mp4", "etag", recordedAt, MatchEventType.Gol);

    [Fact]
    public async Task GetAllGroupReplaysAsync_ShouldPaginateNewestFirst()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllGroupReplaysAsync_ShouldPaginateNewestFirst));
        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        for (var i = 1; i <= 5; i++)
            db.ReplayClips.Add(MakeClip(groupId, matchId, $"clip{i}.mp4", DateTimeOffset.UtcNow.AddHours(-i)));
        await db.SaveChangesAsync();

        var sut = MakeMatchSut(db);

        var page1 = await sut.GetAllGroupReplaysAsync(groupId, userId, page: 1, pageSize: 2, CancellationToken.None);
        page1.Data!.Total.Should().Be(5);
        page1.Data.Items.Select(c => c.ObjectKey).Should().ContainInOrder("clip1.mp4", "clip2.mp4");

        var page3 = await sut.GetAllGroupReplaysAsync(groupId, userId, page: 3, pageSize: 2, CancellationToken.None);
        page3.Data!.Items.Should().HaveCount(1);
        page3.Data.Items[0].ObjectKey.Should().Be("clip5.mp4");
    }

    [Fact]
    public async Task GetMyFavoritesAsync_ShouldPaginateOnlyUserFavorites()
    {
        await using var db = DbContextFactory.Create(nameof(GetMyFavoritesAsync_ShouldPaginateOnlyUserFavorites));
        var groupId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        var clips = Enumerable.Range(1, 4)
            .Select(i => MakeClip(groupId, matchId, $"clip{i}.mp4", DateTimeOffset.UtcNow.AddHours(-i)))
            .ToList();
        db.ReplayClips.AddRange(clips);

        // Usuário favoritou apenas os 3 primeiros
        foreach (var clip in clips.Take(3))
            db.ReplayFavorites.Add(new ReplayFavoriteEntity(clip.Id, userId));
        await db.SaveChangesAsync();

        var sut = MakeMatchSut(db);

        var page1 = await sut.GetMyFavoritesAsync(groupId, userId, page: 1, pageSize: 2, CancellationToken.None);
        page1.Data!.Total.Should().Be(3);
        page1.Data.Items.Should().HaveCount(2);
        page1.Data.Items.Should().OnlyContain(c => c.IsFavoritedByMe);

        var page2 = await sut.GetMyFavoritesAsync(groupId, userId, page: 2, pageSize: 2, CancellationToken.None);
        page2.Data!.Items.Should().HaveCount(1);
    }

    // ─── Bet history ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BetGetHistoryAsync_ShouldPaginateByMatchNewestFirst()
    {
        await using var db = DbContextFactory.Create(nameof(BetGetHistoryAsync_ShouldPaginateByMatchNewestFirst));
        var group = new GroupEntity("Patota", null, Guid.NewGuid());
        var user  = new UserEntity("u", "U", "X", "u@t.com", "hash", null, null);
        db.Groups.Add(group);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        for (var i = 1; i <= 3; i++)
        {
            var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(-i), $"Arena {i}");
            db.Matches.Add(match);
            await db.SaveChangesAsync();

            var bet = new MatchBetEntity(group.Id, match.Id, user.Id);
            bet.ReplaceSelections([new MatchBetSelectionEntity(bet.Id, BetCategory.WinningTeam, "TeamA", 10)]);
            bet.Selections[0].Resolve(10, true, false, "TeamA");
            bet.MarkResolved();
            db.Set<MatchBetEntity>().Add(bet);
            await db.SaveChangesAsync();
        }

        var sut = new BetService(db);

        var page1 = await sut.GetHistoryAsync(group.Id, page: 1, pageSize: 2, CancellationToken.None);
        page1.Total.Should().Be(3);
        page1.Items.Should().HaveCount(2);
        page1.Items[0].PlayedAt.Should().BeAfter(page1.Items[1].PlayedAt, "mais recente primeiro");

        var page2 = await sut.GetHistoryAsync(group.Id, page: 2, pageSize: 2, CancellationToken.None);
        page2.Items.Should().HaveCount(1);
    }

    // ─── Extra charges ────────────────────────────────────────────────────────

    private static PaymentService MakePaymentSut(AppDbContext db)
        => new(db, Mock.Of<IPushService>(), Mock.Of<ILogger<PaymentService>>(), Mock.Of<IFinancialTransactionService>());

    private static async Task<(GroupEntity group, PlayerEntity player, UserEntity user)> SeedPaymentGroupAsync(AppDbContext db)
    {
        var user   = new UserEntity("u", "U", "X", "u@t.com", "hash", null, null);
        var group  = new GroupEntity("Patota", null, user.Id);
        var player = new PlayerEntity("Jogador", user.Id, group.Id, 5m, false);
        db.Users.Add(user);
        db.Groups.Add(group);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return (group, player, user);
    }

    private static ExtraChargeEntity AddCharge(
        AppDbContext db, Guid groupId, Guid playerId, string name, DateTime createDate, bool paid = false)
    {
        var charge = new ExtraChargeEntity(groupId, name, null, 50m, null, Guid.NewGuid());
        db.ExtraCharges.Add(charge);
        SetCreateDate(db, charge, createDate);

        var payment = new ExtraChargePaymentEntity(charge.Id, playerId, groupId, 50m);
        if (paid) payment.MarkAsPaid(null, null, null, null);
        db.ExtraChargePayments.Add(payment);
        return charge;
    }

    [Fact]
    public async Task GetExtraChargesAsync_WithYearAndMonth_ShouldFilterAndPaginate()
    {
        await using var db = DbContextFactory.Create(nameof(GetExtraChargesAsync_WithYearAndMonth_ShouldFilterAndPaginate));
        var (group, player, _) = await SeedPaymentGroupAsync(db);

        AddCharge(db, group.Id, player.Id, "Junho A", new DateTime(2026, 6, 5,  0, 0, 0, DateTimeKind.Utc));
        AddCharge(db, group.Id, player.Id, "Junho B", new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc));
        AddCharge(db, group.Id, player.Id, "Julho",   new DateTime(2026, 7, 1,  0, 0, 0, DateTimeKind.Utc));
        AddCharge(db, group.Id, player.Id, "Ano ant.", new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var sut = MakePaymentSut(db);

        var june = await sut.GetExtraChargesAsync(group.Id, year: 2026, month: 6);
        june.Data!.Total.Should().Be(2);
        june.Data.Items.Select(c => c.Name).Should().ContainInOrder("Junho B", "Junho A");

        var junePaged = await sut.GetExtraChargesAsync(group.Id, year: 2026, month: 6, page: 2, pageSize: 1);
        junePaged.Data!.Items.Should().HaveCount(1);
        junePaged.Data.Items[0].Name.Should().Be("Junho A");
    }

    [Fact]
    public async Task GetMyExtraChargesAsync_WithYearAndMonth_ShouldFilterToPlayerCharges()
    {
        await using var db = DbContextFactory.Create(nameof(GetMyExtraChargesAsync_WithYearAndMonth_ShouldFilterToPlayerCharges));
        var (group, player, user) = await SeedPaymentGroupAsync(db);

        AddCharge(db, group.Id, player.Id, "Minha Junho", new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc));
        AddCharge(db, group.Id, player.Id, "Minha Julho", new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc));

        // Cobrança de outro jogador — não deve aparecer
        var otherPlayer = new PlayerEntity("Outro", Guid.NewGuid(), group.Id, 5m, false);
        db.Players.Add(otherPlayer);
        AddCharge(db, group.Id, otherPlayer.Id, "De outro", new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var sut    = MakePaymentSut(db);
        var result = await sut.GetMyExtraChargesAsync(group.Id, user.Id, year: 2026, month: 6);

        result.Data!.Total.Should().Be(1);
        result.Data.Items[0].Name.Should().Be("Minha Junho");
    }

    [Fact]
    public async Task GetExtraChargesSummaryAsync_ShouldAggregateStatusByMonth()
    {
        await using var db = DbContextFactory.Create(nameof(GetExtraChargesSummaryAsync_ShouldAggregateStatusByMonth));
        var (group, player, _) = await SeedPaymentGroupAsync(db);

        // Junho: tudo pago; Julho: uma pendente
        AddCharge(db, group.Id, player.Id, "Junho paga",    new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), paid: true);
        AddCharge(db, group.Id, player.Id, "Julho pendente", new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var sut    = MakePaymentSut(db);
        var result = await sut.GetExtraChargesSummaryAsync(group.Id, 2026);

        result.Success.Should().BeTrue();
        var june = result.Data!.Single(m => m.Month == 6);
        june.Count.Should().Be(1);
        june.AllPaid.Should().BeTrue();
        june.HasPending.Should().BeFalse();

        var july = result.Data!.Single(m => m.Month == 7);
        july.AllPaid.Should().BeFalse();
        july.HasPending.Should().BeTrue();
    }

    // ─── Groups (GodMode) ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllGroupsAsync_ShouldPaginateAlphabetically()
    {
        await using var db = DbContextFactory.Create(nameof(GetAllGroupsAsync_ShouldPaginateAlphabetically));
        db.Groups.AddRange(
            new GroupEntity("Charlie", null, Guid.NewGuid()),
            new GroupEntity("Alpha",   null, Guid.NewGuid()),
            new GroupEntity("Bravo",   null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var sut = new GroupService(db, Mock.Of<ILogger<GroupService>>(),
            new RepositoryBase<GroupEntity>(db), Mock.Of<IPushService>(), TestImageStorage.Create());

        var page1 = await sut.GetAllGroupsAsync(1, 2, CancellationToken.None);
        page1.Data!.Total.Should().Be(3);
        page1.Data.Items.Select(g => g.Name).Should().ContainInOrder("Alpha", "Bravo");

        var page2 = await sut.GetAllGroupsAsync(2, 2, CancellationToken.None);
        page2.Data!.Items.Should().HaveCount(1);
        page2.Data.Items[0].Name.Should().Be("Charlie");
    }
}
