using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

public sealed class DiscoveryDomainTests
{
    [Fact]
    public void NewGroup_IsPublicAndHasEightCharacterCode()
    {
        var group = new GroupEntity("Patota Teste", null, Guid.NewGuid());

        group.IsPublic.Should().BeTrue();
        group.IsAcceptingPlayers.Should().BeFalse();
        group.Code.Should().MatchRegex("^[A-Z2-9]{8}$");
    }

    [Fact]
    public void PlayerRequest_RequiresAdminThenPlayerApproval()
    {
        var application = new MatchApplicationEntity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            MatchApplicationSource.PlayerRequest,
            openingId: Guid.NewGuid());

        application.Status.Should().Be(MatchApplicationStatus.PendingAdmin);

        var guestId = Guid.NewGuid();
        application.ApproveByAdmin(guestId);
        application.Status.Should().Be(MatchApplicationStatus.PendingPlayer);

        application.AcceptByPlayer(guestId);
        application.Status.Should().Be(MatchApplicationStatus.Accepted);
        application.GuestPlayerId.Should().Be(guestId);
    }

    [Theory]
    [InlineData(1, MatchOpeningPosition.Any)]
    [InlineData(3, MatchOpeningPosition.Line)]
    [InlineData(2, MatchOpeningPosition.Goalkeeper)]
    public void Opening_KeepsQuantityAndPosition(
        int slots,
        MatchOpeningPosition position)
    {
        var opening = new MatchOpeningEntity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            slots,
            position);

        opening.Slots.Should().Be(slots);
        opening.Position.Should().Be(position);
        opening.IsActive.Should().BeTrue();

        opening.Close();
        opening.IsActive.Should().BeFalse();
    }
}
