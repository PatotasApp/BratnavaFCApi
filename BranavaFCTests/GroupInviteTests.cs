using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

// ─── Helpers ─────────────────────────────────────────────────────────────────

file static class Builders
{
    public static UserEntity MakeUser(string userName = "user1") =>
        new(userName, "Primeiro", "Sobrenome", $"{userName}@mail.com", "hash", null, null);

    public static GroupEntity MakeGroup(string name = "Patota FC") =>
        new(name, null, Guid.NewGuid());

    public static GroupService MakeSut(BratnavaFC.Infrastructure.Data.AppDbContext db)
    {
        var logger = Mock.Of<ILogger<GroupService>>();
        var repo   = new Mock<IRepositoryBase<GroupEntity>>().Object;
        var push   = Mock.Of<IPushService>();
        return new GroupService(db, logger, repo, push);
    }
}

// ─── GroupInviteEntity (unit tests — sem banco) ───────────────────────────────

public class GroupInviteEntityTests
{
    [Fact]
    public void Constructor_WithValidArgs_ShouldSetStatusPending()
    {
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        var invite = new GroupInviteEntity(groupId, userId, null);

        invite.Status.Should().Be(GroupInviteStatus.Pending);
        invite.GroupId.Should().Be(groupId);
        invite.TargetUserId.Should().Be(userId);
        invite.GuestPlayerId.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithGuestPlayerId_ShouldSetIt()
    {
        var guestId = Guid.NewGuid();

        var invite = new GroupInviteEntity(Guid.NewGuid(), Guid.NewGuid(), guestId);

        invite.GuestPlayerId.Should().Be(guestId);
    }

    [Fact]
    public void Constructor_WithEmptyGroupId_ShouldThrow()
    {
        var act = () => new GroupInviteEntity(Guid.Empty, Guid.NewGuid(), null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithEmptyTargetUserId_ShouldThrow()
    {
        var act = () => new GroupInviteEntity(Guid.NewGuid(), Guid.Empty, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Accept_ShouldSetStatusAccepted()
    {
        var invite = new GroupInviteEntity(Guid.NewGuid(), Guid.NewGuid(), null);

        invite.Accept();

        invite.Status.Should().Be(GroupInviteStatus.Accepted);
    }

    [Fact]
    public void Reject_ShouldSetStatusRejected()
    {
        var invite = new GroupInviteEntity(Guid.NewGuid(), Guid.NewGuid(), null);

        invite.Reject();

        invite.Status.Should().Be(GroupInviteStatus.Rejected);
    }
}

// ─── GroupService — CreateInviteAsync ────────────────────────────────────────

public class GroupService_CreateInviteTests
{
    [Fact]
    public async Task HappyPath_WithoutGuest_ShouldPersistPendingInvite()
    {
        await using var db  = DbContextFactory.Create(nameof(HappyPath_WithoutGuest_ShouldPersistPendingInvite));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var result = await sut.CreateInviteAsync(group.Id, new CreateGroupInviteDto(user.Id, null), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Status.Should().Be(ResultStatus.Created);
        result.Data.Should().NotBeNull();
        result.Data!.GroupId.Should().Be(group.Id);
        result.Data.TargetUserId.Should().Be(user.Id);
        result.Data.GuestPlayerId.Should().BeNull();
        result.Data.Status.Should().Be((int)GroupInviteStatus.Pending);

        var persisted = await db.GroupInvites.FindAsync(result.Data.Id);
        persisted.Should().NotBeNull();
    }

    [Fact]
    public async Task HappyPath_WithGuestPlayer_ShouldSetGuestInfo()
    {
        await using var db  = DbContextFactory.Create(nameof(HappyPath_WithGuestPlayer_ShouldSetGuestInfo));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        var guest = new PlayerEntity("Convidado Teste", null, group.Id, 7m, false, true, Status.Active);
        db.Users.Add(user);
        db.Groups.Add(group);
        db.Players.Add(guest);
        await db.SaveChangesAsync();

        var result = await sut.CreateInviteAsync(group.Id, new CreateGroupInviteDto(user.Id, guest.Id), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.GuestPlayerId.Should().Be(guest.Id);
        result.Data.GuestPlayerName.Should().Be("Convidado Teste");
    }

    [Fact]
    public async Task WhenGroupNotFound_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenGroupNotFound_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var user = Builders.MakeUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await sut.CreateInviteAsync(Guid.NewGuid(), new CreateGroupInviteDto(user.Id, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task WhenUserNotFound_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenUserNotFound_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var group = Builders.MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var result = await sut.CreateInviteAsync(group.Id, new CreateGroupInviteDto(Guid.NewGuid(), null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task WhenUserAlreadyMember_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenUserAlreadyMember_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        db.Players.Add(new PlayerEntity("Membro", user.Id, group.Id, 0m, false, false, Status.Active));
        await db.SaveChangesAsync();

        var result = await sut.CreateInviteAsync(group.Id, new CreateGroupInviteDto(user.Id, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task WhenDuplicatePendingInvite_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenDuplicatePendingInvite_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        db.GroupInvites.Add(new GroupInviteEntity(group.Id, user.Id, null));
        await db.SaveChangesAsync();

        var result = await sut.CreateInviteAsync(group.Id, new CreateGroupInviteDto(user.Id, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task WhenGuestPlayerNotInGroup_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenGuestPlayerNotInGroup_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var result = await sut.CreateInviteAsync(group.Id, new CreateGroupInviteDto(user.Id, Guid.NewGuid()), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task AfterRejectedInvite_ShouldAllowNewInvite()
    {
        await using var db  = DbContextFactory.Create(nameof(AfterRejectedInvite_ShouldAllowNewInvite));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        var rejected = new GroupInviteEntity(group.Id, user.Id, null);
        rejected.Reject();
        db.GroupInvites.Add(rejected);
        await db.SaveChangesAsync();

        // Convite rejeitado não deve bloquear um novo convite
        var result = await sut.CreateInviteAsync(group.Id, new CreateGroupInviteDto(user.Id, null), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be((int)GroupInviteStatus.Pending);
    }

    [Fact]
    public async Task WhenUserIsGuestInGroup_ShouldAllowInvite()
    {
        // Cenário: mensalista virou convidado (IsGuest=true, mas UserId ainda preenchido).
        // O sistema NÃO deve bloquear o novo convite.
        await using var db  = DbContextFactory.Create(nameof(WhenUserIsGuestInGroup_ShouldAllowInvite));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        // Player com UserId preenchido mas marcado como guest (ex-mensalista)
        db.Players.Add(new PlayerEntity("Ex-Mensalista", user.Id, group.Id, 5m, false, true, Status.Active));
        await db.SaveChangesAsync();

        var result = await sut.CreateInviteAsync(group.Id, new CreateGroupInviteDto(user.Id, null), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be((int)GroupInviteStatus.Pending);
    }

    [Fact]
    public async Task WhenUserIsActiveMember_ShouldStillBlockInvite()
    {
        // Garantir que a correção não quebrou o bloqueio para mensalistas ativos.
        await using var db  = DbContextFactory.Create(nameof(WhenUserIsActiveMember_ShouldStillBlockInvite));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        db.Players.Add(new PlayerEntity("Mensalista Ativo", user.Id, group.Id, 5m, false, false, Status.Active));
        await db.SaveChangesAsync();

        var result = await sut.CreateInviteAsync(group.Id, new CreateGroupInviteDto(user.Id, null), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }
}

// ─── GroupService — GetMyInvitesAsync & GetMyPendingInviteCountAsync ──────────

public class GroupService_GetInviteTests
{
    [Fact]
    public async Task GetMyInvitesAsync_ShouldReturnOnlyPendingInvites()
    {
        await using var db  = DbContextFactory.Create(nameof(GetMyInvitesAsync_ShouldReturnOnlyPendingInvites));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);

        var pending  = new GroupInviteEntity(group.Id, user.Id, null);
        var accepted = new GroupInviteEntity(group.Id, user.Id, null);
        accepted.Accept();
        var rejected = new GroupInviteEntity(group.Id, user.Id, null);
        rejected.Reject();
        db.GroupInvites.AddRange(pending, accepted, rejected);
        await db.SaveChangesAsync();

        var result = await sut.GetMyInvitesAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].Id.Should().Be(pending.Id);
    }

    [Fact]
    public async Task GetMyInvitesAsync_ShouldNotReturnOtherUsersInvites()
    {
        await using var db  = DbContextFactory.Create(nameof(GetMyInvitesAsync_ShouldNotReturnOtherUsersInvites));
        var sut = Builders.MakeSut(db);

        var user1 = Builders.MakeUser("user1");
        var user2 = Builders.MakeUser("user2");
        var group = Builders.MakeGroup();
        db.Users.AddRange(user1, user2);
        db.Groups.Add(group);
        db.GroupInvites.Add(new GroupInviteEntity(group.Id, user1.Id, null));
        await db.SaveChangesAsync();

        var result = await sut.GetMyInvitesAsync(user2.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyPendingInviteCountAsync_ShouldReturnCorrectCount()
    {
        await using var db  = DbContextFactory.Create(nameof(GetMyPendingInviteCountAsync_ShouldReturnCorrectCount));
        var sut = Builders.MakeSut(db);

        var user   = Builders.MakeUser();
        var group1 = Builders.MakeGroup("G1");
        var group2 = Builders.MakeGroup("G2");
        var group3 = Builders.MakeGroup("G3");
        db.Users.Add(user);
        db.Groups.AddRange(group1, group2, group3);

        var pending1 = new GroupInviteEntity(group1.Id, user.Id, null);
        var pending2 = new GroupInviteEntity(group2.Id, user.Id, null);
        var accepted = new GroupInviteEntity(group3.Id, user.Id, null);
        accepted.Accept();
        db.GroupInvites.AddRange(pending1, pending2, accepted);
        await db.SaveChangesAsync();

        var result = await sut.GetMyPendingInviteCountAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(2);
    }
}

// ─── GroupService — AcceptInviteAsync ────────────────────────────────────────

public class GroupService_AcceptInviteTests
{
    [Fact]
    public async Task WithoutGuestPlayer_ShouldCreateNewPlayerAndMarkAccepted()
    {
        await using var db  = DbContextFactory.Create(nameof(WithoutGuestPlayer_ShouldCreateNewPlayerAndMarkAccepted));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        var invite = new GroupInviteEntity(group.Id, user.Id, null);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        var result = await sut.AcceptInviteAsync(invite.Id, user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();

        // convite deve ser Accepted
        var updatedInvite = await db.GroupInvites.FindAsync(invite.Id);
        updatedInvite!.Status.Should().Be(GroupInviteStatus.Accepted);

        // deve ter criado 1 player vinculado ao usuário
        var players = db.Players.Where(p => p.GroupId == group.Id).ToList();
        players.Should().HaveCount(1);
        players[0].UserId.Should().Be(user.Id);
        players[0].IsGuest.Should().BeFalse();
        players[0].Name.Should().Be("Primeiro Sobrenome");
    }

    [Fact]
    public async Task WithGuestPlayer_ShouldLinkUserAndClearGuestFlag()
    {
        await using var db  = DbContextFactory.Create(nameof(WithGuestPlayer_ShouldLinkUserAndClearGuestFlag));
        var sut = Builders.MakeSut(db);

        var user        = Builders.MakeUser();
        var group       = Builders.MakeGroup();
        var guestPlayer = new PlayerEntity("Guest Andrei", null, group.Id, 8m, false, true, Status.Active);
        db.Users.Add(user);
        db.Groups.Add(group);
        db.Players.Add(guestPlayer);
        var invite = new GroupInviteEntity(group.Id, user.Id, guestPlayer.Id);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        var result = await sut.AcceptInviteAsync(invite.Id, user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();

        var updatedInvite = await db.GroupInvites.FindAsync(invite.Id);
        updatedInvite!.Status.Should().Be(GroupInviteStatus.Accepted);

        var updatedPlayer = db.Players.IgnoreQueryFilters().First(p => p.Id == guestPlayer.Id);
        updatedPlayer.UserId.Should().Be(user.Id);
        updatedPlayer.IsGuest.Should().BeFalse();
        // habilidades do perfil guest devem ser preservadas
        updatedPlayer.SkillPoints.Should().Be(8m);
        updatedPlayer.Name.Should().Be("Guest Andrei");
    }

    [Fact]
    public async Task WhenInviteNotFound_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenInviteNotFound_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var result = await sut.AcceptInviteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task WhenInviteBelongsToAnotherUser_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenInviteBelongsToAnotherUser_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var owner = Builders.MakeUser("owner");
        var other = Builders.MakeUser("other");
        var group = Builders.MakeGroup();
        db.Users.AddRange(owner, other);
        db.Groups.Add(group);
        var invite = new GroupInviteEntity(group.Id, owner.Id, null);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        // "other" tenta aceitar o convite de "owner"
        var result = await sut.AcceptInviteAsync(invite.Id, other.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task WhenAlreadyAccepted_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenAlreadyAccepted_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        var invite = new GroupInviteEntity(group.Id, user.Id, null);
        invite.Accept();
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        var result = await sut.AcceptInviteAsync(invite.Id, user.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task WhenMatchInAcceptation_NewPlayer_ShouldAddToMatch()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenMatchInAcceptation_NewPlayer_ShouldAddToMatch));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);

        // Partida já aberta para aceites
        var match = new MatchEntity(group.Id, DateTime.UtcNow, "Arena");
        match.OpenAcceptation();
        db.Matches.Add(match);

        var invite = new GroupInviteEntity(group.Id, user.Id, null);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        await sut.AcceptInviteAsync(invite.Id, user.Id, CancellationToken.None);

        // Player criado deve estar na partida
        var newPlayer = db.Players.First(p => p.UserId == user.Id && p.GroupId == group.Id);
        var updatedMatch = db.Matches.Include(m => m.Players).First(m => m.Id == match.Id);
        updatedMatch.Players.Should().HaveCount(1);
        updatedMatch.Players.Should().Contain(mp => mp.PlayerId == newPlayer.Id);
    }

    [Fact]
    public async Task WhenUserWasFormerMember_ShouldReactivateExistingPlayerRecord()
    {
        // Cenário: usuário era mensalista, virou guest (SetIsGuest(true), UserId mantido).
        // Ao aceitar novo convite sem GuestPlayerId, deve reativar o player existente
        // em vez de criar um duplicado.
        await using var db  = DbContextFactory.Create(nameof(WhenUserWasFormerMember_ShouldReactivateExistingPlayerRecord));
        var sut = Builders.MakeSut(db);

        var user         = Builders.MakeUser();
        var group        = Builders.MakeGroup();
        var formerPlayer = new PlayerEntity("Ex-Mensalista", user.Id, group.Id, 8m, true, true, Status.Active);
        db.Users.Add(user);
        db.Groups.Add(group);
        db.Players.Add(formerPlayer);
        var invite = new GroupInviteEntity(group.Id, user.Id, null);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        await sut.AcceptInviteAsync(invite.Id, user.Id, CancellationToken.None);

        // Deve existir exatamente 1 player no grupo (o antigo reativado, sem duplicata)
        var allPlayers = db.Players.Where(p => p.GroupId == group.Id).ToList();
        allPlayers.Should().HaveCount(1);

        var reactivated = allPlayers[0];
        reactivated.Id.Should().Be(formerPlayer.Id);
        reactivated.IsGuest.Should().BeFalse();
        reactivated.UserId.Should().Be(user.Id);
        // Habilidades preservadas
        reactivated.SkillPoints.Should().Be(8m);
        reactivated.IsGoalkeeper.Should().BeTrue();
    }

    [Fact]
    public async Task WhenUserWasFormerMember_ReactivatedPlayer_ShouldBeAddedToAcceptationMatch()
    {
        // Ao reativar ex-mensalista, o player deve ser incluído na partida em Acceptation.
        await using var db  = DbContextFactory.Create(nameof(WhenUserWasFormerMember_ReactivatedPlayer_ShouldBeAddedToAcceptationMatch));
        var sut = Builders.MakeSut(db);

        var user         = Builders.MakeUser();
        var group        = Builders.MakeGroup();
        var formerPlayer = new PlayerEntity("Ex-Mensalista", user.Id, group.Id, 5m, false, true, Status.Active);
        db.Users.Add(user);
        db.Groups.Add(group);
        db.Players.Add(formerPlayer);

        var match = new MatchEntity(group.Id, DateTime.UtcNow, "Arena");
        match.OpenAcceptation();
        db.Matches.Add(match);

        var invite = new GroupInviteEntity(group.Id, user.Id, null);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        await sut.AcceptInviteAsync(invite.Id, user.Id, CancellationToken.None);

        var updatedMatch = db.Matches.Include(m => m.Players).First(m => m.Id == match.Id);
        updatedMatch.Players.Should().HaveCount(1);
        updatedMatch.Players.Should().Contain(mp => mp.PlayerId == formerPlayer.Id);
    }

    [Fact]
    public async Task WhenMatchInAcceptation_GuestPlayer_ShouldAddToMatch()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenMatchInAcceptation_GuestPlayer_ShouldAddToMatch));
        var sut = Builders.MakeSut(db);

        var user   = Builders.MakeUser();
        var group  = Builders.MakeGroup();
        var guest  = new PlayerEntity("Zé da Pelada", null, group.Id, 7m, false, true, Status.Active);
        db.Users.Add(user);
        db.Groups.Add(group);
        db.Players.Add(guest);

        var match = new MatchEntity(group.Id, DateTime.UtcNow, "Arena");
        match.OpenAcceptation();
        db.Matches.Add(match);

        var invite = new GroupInviteEntity(group.Id, user.Id, guest.Id);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        await sut.AcceptInviteAsync(invite.Id, user.Id, CancellationToken.None);

        // Guest player vinculado deve estar na partida
        var updatedMatch = db.Matches.Include(m => m.Players).First(m => m.Id == match.Id);
        updatedMatch.Players.Should().HaveCount(1);
        updatedMatch.Players.Should().Contain(mp => mp.PlayerId == guest.Id);
    }

    [Fact]
    public async Task WhenMatchInAcceptation_AndNewPlayerHasAbsence_ShouldAutoRejectMatchPlayer()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenMatchInAcceptation_AndNewPlayerHasAbsence_ShouldAutoRejectMatchPlayer));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);

        var matchDate = DateTime.UtcNow.AddDays(5);
        var match = new MatchEntity(group.Id, matchDate, "Arena");
        match.OpenAcceptation();
        db.Matches.Add(match);

        var matchOnly = DateOnly.FromDateTime(matchDate);
        var absence   = new UserAbsenceEntity(user.Id, matchOnly, matchOnly, AbsenceType.Travel, null);
        db.UserAbsences.Add(absence);

        var invite = new GroupInviteEntity(group.Id, user.Id, null);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        await sut.AcceptInviteAsync(invite.Id, user.Id, CancellationToken.None);

        var newPlayer    = db.Players.First(p => p.UserId == user.Id && p.GroupId == group.Id);
        var updatedMatch = db.Matches.Include(m => m.Players).First(m => m.Id == match.Id);
        var mp = updatedMatch.Players.First(p => p.PlayerId == newPlayer.Id);

        mp.InviteResponse.Should().Be(InviteResponse.Rejected);
        mp.AutoRejectedByAbsenceId.Should().Be(absence.Id);
    }
}

// ─── GroupService — RejectInviteAsync ────────────────────────────────────────

public class GroupService_RejectInviteTests
{
    [Fact]
    public async Task HappyPath_ShouldMarkRejected()
    {
        await using var db  = DbContextFactory.Create(nameof(HappyPath_ShouldMarkRejected));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        var invite = new GroupInviteEntity(group.Id, user.Id, null);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        var result = await sut.RejectInviteAsync(invite.Id, user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();

        var updatedInvite = await db.GroupInvites.FindAsync(invite.Id);
        updatedInvite!.Status.Should().Be(GroupInviteStatus.Rejected);
    }

    [Fact]
    public async Task HappyPath_ShouldNotCreateOrModifyPlayer()
    {
        await using var db  = DbContextFactory.Create(nameof(HappyPath_ShouldNotCreateOrModifyPlayer));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        var invite = new GroupInviteEntity(group.Id, user.Id, null);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        await sut.RejectInviteAsync(invite.Id, user.Id, CancellationToken.None);

        db.Players.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenInviteNotFound_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenInviteNotFound_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var result = await sut.RejectInviteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task WhenAlreadyRejected_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenAlreadyRejected_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var user  = Builders.MakeUser();
        var group = Builders.MakeGroup();
        db.Users.Add(user);
        db.Groups.Add(group);
        var invite = new GroupInviteEntity(group.Id, user.Id, null);
        invite.Reject();
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        var result = await sut.RejectInviteAsync(invite.Id, user.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task WhenInviteBelongsToAnotherUser_ShouldReturnFailure()
    {
        await using var db  = DbContextFactory.Create(nameof(WhenInviteBelongsToAnotherUser_ShouldReturnFailure));
        var sut = Builders.MakeSut(db);

        var owner = Builders.MakeUser("owner");
        var other = Builders.MakeUser("other");
        var group = Builders.MakeGroup();
        db.Users.AddRange(owner, other);
        db.Groups.Add(group);
        var invite = new GroupInviteEntity(group.Id, owner.Id, null);
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync();

        var result = await sut.RejectInviteAsync(invite.Id, other.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }
}
