using System.Security.Claims;
using BratnavaFC.Api.Controllers;
using BratnavaFC.Api.Realtime;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Redis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// invite/accept e invite/reject: admin responde por qualquer jogador (ação de admin); quem não é
/// admin só responde pelo próprio playerId (ação de jogador).
/// </summary>
public sealed class MatchInviteResponseControllerTests
{
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid MatchId = Guid.NewGuid();

    private static Mock<IMatchService> Service()
    {
        var mock = new Mock<IMatchService>();
        mock.Setup(s => s.AcceptInviteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .ReturnsAsync(Result.Ok());
        mock.Setup(s => s.RejectInviteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .ReturnsAsync(Result.Ok());
        mock.Setup(s => s.AcceptInviteAsPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        mock.Setup(s => s.RejectInviteAsPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        return mock;
    }

    private static MatchesController Sut(AppDbContext db, Mock<IMatchService> service, Guid userId, string role = "User")
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)],
            "Tests", ClaimTypes.Name, ClaimTypes.Role);

        return new MatchesController(
            service.Object,
            db,
            Mock.Of<IMatchEventPublisher>(),
            Mock.Of<IReplayUrlService>(),
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IBetService>(),
            Mock.Of<IRealtimeNotifier>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static async Task<PlayerEntity> AddPlayerAsync(AppDbContext db, Guid? userId, Guid groupId, bool isGuest = false)
    {
        var player = new PlayerEntity("Jogador", userId, groupId, 3m, false, isGuest);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    private static InviteActionDto Dto(Guid playerId) => new() { PlayerId = playerId };

    [Fact]
    public async Task NonAdminPlayer_AcceptingOwnInvite_IsRecordedAsPlayerAction()
    {
        var userId = Guid.NewGuid();
        await using var db = DbContextFactory.Create(nameof(NonAdminPlayer_AcceptingOwnInvite_IsRecordedAsPlayerAction));
        var me = await AddPlayerAsync(db, userId, GroupId);
        var service = Service();

        var result = await Sut(db, service, userId).AcceptInviteAsync(GroupId, MatchId, Dto(me.Id), CancellationToken.None);

        result.Should().NotBeOfType<ForbidResult>();
        service.Verify(s => s.AcceptInviteAsPlayerAsync(GroupId, MatchId, me.Id, userId, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(s => s.AcceptInviteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task NonAdminPlayer_RejectingOwnInvite_IsRecordedAsPlayerAction()
    {
        var userId = Guid.NewGuid();
        await using var db = DbContextFactory.Create(nameof(NonAdminPlayer_RejectingOwnInvite_IsRecordedAsPlayerAction));
        var me = await AddPlayerAsync(db, userId, GroupId);
        var service = Service();

        var result = await Sut(db, service, userId).RejectInviteAsync(GroupId, MatchId, Dto(me.Id), CancellationToken.None);

        result.Should().NotBeOfType<ForbidResult>();
        service.Verify(s => s.RejectInviteAsPlayerAsync(GroupId, MatchId, me.Id, userId, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(s => s.RejectInviteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task NonAdminPlayer_RespondingForAnotherPlayer_IsForbidden()
    {
        var userId = Guid.NewGuid();
        await using var db = DbContextFactory.Create(nameof(NonAdminPlayer_RespondingForAnotherPlayer_IsForbidden));
        await AddPlayerAsync(db, userId, GroupId);
        var other = await AddPlayerAsync(db, Guid.NewGuid(), GroupId);
        var service = Service();
        var sut = Sut(db, service, userId);

        (await sut.AcceptInviteAsync(GroupId, MatchId, Dto(other.Id), CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await sut.RejectInviteAsync(GroupId, MatchId, Dto(other.Id), CancellationToken.None)).Should().BeOfType<ForbidResult>();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GuestPlayer_RespondingThroughAdminEndpoint_IsForbidden()
    {
        var userId = Guid.NewGuid();
        await using var db = DbContextFactory.Create(nameof(GuestPlayer_RespondingThroughAdminEndpoint_IsForbidden));
        var guest = await AddPlayerAsync(db, userId, GroupId, isGuest: true);
        var service = Service();

        var result = await Sut(db, service, userId).AcceptInviteAsync(GroupId, MatchId, Dto(guest.Id), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PlayerFromAnotherGroup_UsingOwnPlayerId_IsForbidden()
    {
        var userId = Guid.NewGuid();
        await using var db = DbContextFactory.Create(nameof(PlayerFromAnotherGroup_UsingOwnPlayerId_IsForbidden));
        var meElsewhere = await AddPlayerAsync(db, userId, Guid.NewGuid());
        var service = Service();

        var result = await Sut(db, service, userId).AcceptInviteAsync(GroupId, MatchId, Dto(meElsewhere.Id), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GroupAdmin_RespondingForAnotherPlayer_IsRecordedAsAdminAction()
    {
        var adminId = Guid.NewGuid();
        await using var db = DbContextFactory.Create(nameof(GroupAdmin_RespondingForAnotherPlayer_IsRecordedAsAdminAction));
        db.GroupAdmins.Add(new GroupAdminEntity { UserId = adminId, GroupId = GroupId });
        await db.SaveChangesAsync();
        var other = await AddPlayerAsync(db, Guid.NewGuid(), GroupId);
        var service = Service();
        var sut = Sut(db, service, adminId);

        await sut.AcceptInviteAsync(GroupId, MatchId, Dto(other.Id), CancellationToken.None);
        await sut.RejectInviteAsync(GroupId, MatchId, Dto(other.Id), CancellationToken.None);

        service.Verify(s => s.AcceptInviteAsync(GroupId, MatchId, other.Id, It.IsAny<CancellationToken>(), adminId), Times.Once);
        service.Verify(s => s.RejectInviteAsync(GroupId, MatchId, other.Id, It.IsAny<CancellationToken>(), adminId), Times.Once);
        service.Verify(s => s.AcceptInviteAsPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UserOutsideGroup_IsForbidden()
    {
        await using var db = DbContextFactory.Create(nameof(UserOutsideGroup_IsForbidden));
        var someone = await AddPlayerAsync(db, Guid.NewGuid(), GroupId);
        var service = Service();

        var result = await Sut(db, service, Guid.NewGuid()).AcceptInviteAsync(GroupId, MatchId, Dto(someone.Id), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        service.VerifyNoOtherCalls();
    }
}
