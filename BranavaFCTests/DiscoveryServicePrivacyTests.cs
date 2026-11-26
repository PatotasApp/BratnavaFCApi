using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Moq;

namespace BranavaFC.Tests;

public sealed class DiscoveryServicePrivacyTests
{
    private static DiscoveryService Service(BratnavaFC.Infrastructure.Data.AppDbContext db) =>
        new(db, Mock.Of<IImageStorageService>(), Mock.Of<IPushService>(), Mock.Of<IMatchService>());

    private static UserEntity User(string suffix) => new(
        $"user{suffix}", $"Nome{suffix}", "Teste", $"user{suffix}@example.com",
        "hash", null, null);

    [Fact]
    public async Task GroupSearch_ShowsEveryActiveGroupAsPrivate()
    {
        await using var db = DbContextFactory.Create(nameof(GroupSearch_ShowsEveryActiveGroupAsPrivate));
        var member = User("private-member");
        var outsider = User("private-outsider");
        var group = new GroupEntity("Resenha privada", null, member.Id);
        group.UpdateProfile(false, false, false, null, null);
        var membership = new PlayerEntity(member.DisplayName, member.Id, group.Id, 0, false);
        db.AddRange(member, outsider, group, membership);
        await db.SaveChangesAsync();

        var memberResult = await Service(db).SearchGroupsAsync(
            member.Id, "Resenha", 1, 20, default);
        var outsiderResult = await Service(db).SearchGroupsAsync(
            outsider.Id, "Resenha", 1, 20, default);

        memberResult.Success.Should().BeTrue();
        var memberItem = memberResult.Data!.Items.Should().ContainSingle().Subject;
        memberItem.IsMember.Should().BeTrue();
        memberItem.IsPublic.Should().BeFalse();
        memberItem.IsAcceptingPlayers.Should().BeFalse();
        outsiderResult.Success.Should().BeTrue();
        var outsiderItem = outsiderResult.Data!.Items.Should().ContainSingle().Subject;
        outsiderItem.IsMember.Should().BeFalse();
        outsiderItem.IsPublic.Should().BeFalse();
        outsiderItem.IsAcceptingPlayers.Should().BeFalse();
    }

    [Fact]
    public async Task NewExternalInteractions_AreTemporarilyBlocked()
    {
        await using var db = DbContextFactory.Create(nameof(NewExternalInteractions_AreTemporarilyBlocked));
        var service = Service(db);

        var application = await service.ApplyAsync(
            Guid.NewGuid(), Guid.NewGuid(), new(Guid.NewGuid()), default);
        var invitation = await service.InviteAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), null), default);

        application.Success.Should().BeFalse();
        application.Status.Should().Be(BratnavaFC.Domain.Common.ResultStatus.Forbidden);
        invitation.Success.Should().BeFalse();
        invitation.Status.Should().Be(BratnavaFC.Domain.Common.ResultStatus.Forbidden);
    }

    [Fact]
    public async Task PrivateGroup_DoesNotTreatInactivePlayerAsMember()
    {
        await using var db = DbContextFactory.Create(nameof(PrivateGroup_DoesNotTreatInactivePlayerAsMember));
        var requester = User("inactive");
        var group = new GroupEntity("Patota privada", null, requester.Id);
        group.UpdateProfile(false, false, false, null, null);
        var inactiveMembership = new PlayerEntity(
            requester.DisplayName, requester.Id, group.Id, 0, false, status: Status.Inactive);
        db.AddRange(requester, group, inactiveMembership);
        await db.SaveChangesAsync();

        var result = await Service(db).GetGroupProfileAsync(group.Id, requester.Id, default);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(BratnavaFC.Domain.Common.ResultStatus.NotFound);
        result.Error.Should().Be("Esta patota é privada.");
    }

    [Fact]
    public async Task PlayerSearch_DoesNotExposeMembershipForArbitraryGroupId()
    {
        await using var db = DbContextFactory.Create(nameof(PlayerSearch_DoesNotExposeMembershipForArbitraryGroupId));
        var requester = User("requester");
        var target = User("target");
        var group = new GroupEntity("Patota", null, requester.Id);
        var targetMembership = new PlayerEntity(target.DisplayName, target.Id, group.Id, 0, false);
        db.AddRange(requester, target, group, targetMembership);
        await db.SaveChangesAsync();

        var result = await Service(db).SearchPlayersAsync(
            requester.Id, target.UserName, 1, 20, default, group.Id);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle().Which.InGroup.Should().BeFalse();
    }

    [Fact]
    public async Task RejectingGuestInvite_ReturnsUpdatedMatchInsteadOfNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(RejectingGuestInvite_ReturnsUpdatedMatchInsteadOfNotFound));
        var user = User("guest");
        var group = new GroupEntity("Patota", null, user.Id);
        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        match.OpenAcceptation();
        var application = new MatchApplicationEntity(
            group.Id, match.Id, user.Id, MatchApplicationSource.AdminInvite, Guid.NewGuid());
        db.AddRange(user, group, match, application);
        await db.SaveChangesAsync();

        var result = await Service(db).SetGuestAttendanceAsync(user.Id, match.Id, false, default);

        result.Success.Should().BeTrue();
        result.Data!.MyResponse.Should().Be("not_going");
        application.Status.Should().Be(MatchApplicationStatus.Rejected);
    }

    [Fact]
    public async Task GuestMatch_HidesAggregateScoreWhenGoalsArePrivate()
    {
        await using var db = DbContextFactory.Create(nameof(GuestMatch_HidesAggregateScoreWhenGoalsArePrivate));
        var user = User("score");
        var group = new GroupEntity("Patota", null, user.Id);
        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        match.OpenAcceptation();
        var application = new MatchApplicationEntity(
            group.Id, match.Id, user.Id, MatchApplicationSource.AdminInvite, Guid.NewGuid());
        application.AcceptByPlayer(Guid.NewGuid());
        db.AddRange(user, group, match, application);
        await db.SaveChangesAsync();

        var result = await Service(db).GetGuestMatchAsync(user.Id, match.Id, default);

        result.Success.Should().BeTrue();
        result.Data!.GoalsVisible.Should().BeFalse();
        result.Data.TeamAGoals.Should().BeNull();
        result.Data.TeamBGoals.Should().BeNull();
    }

    [Fact]
    public async Task GuestMatch_UsesSelectedManualGuestWhenLegacyPlayerHasNoUserId()
    {
        await using var db = DbContextFactory.Create(nameof(GuestMatch_UsesSelectedManualGuestWhenLegacyPlayerHasNoUserId));
        var user = User("legacy-guest");
        var otherUser = User("legacy-other");
        var group = new GroupEntity("Patota", null, otherUser.Id);
        var guest = new PlayerEntity("Pagel", null, group.Id, 0, false, true,
            Status.Active, GuestOrigin.Manual);
        var otherPlayer = new PlayerEntity(otherUser.DisplayName, otherUser.Id, group.Id, 0, false);
        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(1), "Quadra");
        match.OpenAcceptation();

        var guestMatchPlayer = new MatchPlayerEntity(guest.Id);
        guestMatchPlayer.AssignToPlayer(guest);
        guestMatchPlayer.AssignToMatch(match);
        guestMatchPlayer.AcceptInvite();
        var otherMatchPlayer = new MatchPlayerEntity(otherPlayer.Id);
        otherMatchPlayer.AssignToPlayer(otherPlayer);
        otherMatchPlayer.AssignToMatch(match);
        otherMatchPlayer.AcceptInvite();
        match.Players.Add(guestMatchPlayer);
        match.Players.Add(otherMatchPlayer);

        var application = new MatchApplicationEntity(
            group.Id, match.Id, user.Id, MatchApplicationSource.AdminInvite, otherUser.Id);
        application.AcceptByPlayer(guest.Id);
        match.GoToMatchMaking();
        db.AddRange(user, otherUser, group, guest, otherPlayer, match, application);
        await db.SaveChangesAsync();

        var service = Service(db);
        var summary = await service.GetGuestMatchesAsync(user.Id, default);
        var detail = await service.GetGuestMatchAsync(user.Id, match.Id, default);

        summary.Success.Should().BeTrue();
        summary.Data.Should().ContainSingle().Which.MyResponse.Should().Be("going");
        detail.Success.Should().BeTrue();
        detail.Data!.Players.Should().ContainSingle(x => x.Name == "Pagel" && x.IsMe);
    }
}
