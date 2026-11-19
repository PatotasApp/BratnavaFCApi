using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

public sealed class GuestAccessTests
{
    [Theory]
    [InlineData("pagel", "-", "pagel")]
    [InlineData("pagel", "", "pagel")]
    [InlineData("pagel", null, "pagel")]
    [InlineData("Heitor", "Pires", "Heitor Pires")]
    [InlineData(" Ana ", " Souza Lima ", "Ana Souza Lima")]
    public void JoinName_NeverShowsThePlaceholderLastName(string first, string? last, string expected)
    {
        UserEntity.JoinName(first, last).Should().Be(expected);
    }

    private static MatchApplicationEntity ConfirmedApplication()
    {
        var application = new MatchApplicationEntity(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            MatchApplicationSource.AdminInvite, invitedByUserId: Guid.NewGuid());
        application.Status.Should().Be(MatchApplicationStatus.PendingPlayer);
        application.AcceptByPlayer(Guid.NewGuid());
        return application;
    }

    [Fact]
    public void ConfirmedGuest_CanWithdrawAndReconfirm()
    {
        var application = ConfirmedApplication();

        application.WithdrawByPlayer();
        application.Status.Should().Be(MatchApplicationStatus.Cancelled);

        application.ReconfirmByPlayer();
        application.Status.Should().Be(MatchApplicationStatus.Accepted);
    }

    [Fact]
    public void Withdraw_RequiresAConfirmedGuest()
    {
        var application = new MatchApplicationEntity(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            MatchApplicationSource.AdminInvite, invitedByUserId: Guid.NewGuid());

        var act = () => application.WithdrawByPlayer();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reconfirm_RequiresAWithdrawnConfirmation()
    {
        var application = ConfirmedApplication();

        var act = () => application.ReconfirmByPlayer();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GroupProfile_KeepsGoalsHiddenFromGuestsByDefault()
    {
        var group = new GroupEntity("Patota", null, Guid.NewGuid());
        group.ShowGoalsToGuests.Should().BeFalse();

        group.UpdateProfile(true, true, false, null, null, showGoalsToGuests: true);

        group.ShowGoalsToGuests.Should().BeTrue();
    }
}
