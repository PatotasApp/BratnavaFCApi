using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;

namespace BranavaFC.Tests;

public class PlayerEntityTests
{
    [Fact]
    public void Ctor_ShouldSetFields_AndTrimName()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var p = new PlayerEntity("  Caio  ", userId, groupId, 12.5m, true, false, Status.Active);

        p.Name.Should().Be("Caio");
        p.UserId.Should().Be(userId);
        p.GroupId.Should().Be(groupId);
        p.SkillPoints.Should().Be(12.5m);
        p.IsGoalkeeper.Should().BeTrue();
        p.IsGuest.Should().BeFalse();
        p.Status.Should().Be(Status.Active);
    }

    [Fact]
    public void Ctor_Guest_ShouldAllowNullUserId()
    {
        var groupId = Guid.NewGuid();

        var p = new PlayerEntity("Visitante", null, groupId, 5m, false, true, Status.Active);

        p.UserId.Should().BeNull();
        p.IsGuest.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_WithInvalidName_ShouldThrow(string? name)
    {
        var act = () => new PlayerEntity(name!, Guid.NewGuid(), Guid.NewGuid(), 0m, false, false, Status.Active);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Player name is required.");
    }

    [Fact]
    public void Rename_ShouldTrimName()
    {
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, false, Status.Active);

        p.Rename("  Marlon  ");

        p.Name.Should().Be("Marlon");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_WithInvalidName_ShouldThrow(string? name)
    {
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, false, Status.Active);

        var act = () => p.Rename(name!);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Player name is required.");
    }

    [Fact]
    public void SetUser_WithEmpty_ShouldThrow()
    {
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, false, Status.Active);

        var act = () => p.SetUser(Guid.Empty);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("UserId is required.");
    }

    [Fact]
    public void SetGroup_WithEmpty_ShouldThrow()
    {
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, false, Status.Active);

        var act = () => p.SetGroup(Guid.Empty);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("GroupId is required.");
    }

    [Fact]
    public void SetSkillPoints_WithNegative_ShouldThrow()
    {
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, false, Status.Active);

        var act = () => p.SetSkillPoints(-0.1m);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("SkillPoints cannot be negative.");
    }

    [Fact]
    public void SetSkillPoints_WithValid_ShouldUpdate()
    {
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, false, Status.Active);

        p.SetSkillPoints(7.25m);

        p.SkillPoints.Should().Be(7.25m);
    }

    [Fact]
    public void SetIsGuest_ShouldToggle()
    {
        var p = new PlayerEntity("Ok", Guid.NewGuid(), Guid.NewGuid(), 0m, false, false, Status.Active);

        p.SetIsGuest(true);

        p.IsGuest.Should().BeTrue();
    }

    // ─── ClearUser ────────────────────────────────────────────────────────────

    [Fact]
    public void ClearUser_ShouldSetUserIdToNull()
    {
        var userId = Guid.NewGuid();
        var p = new PlayerEntity("Fulano", userId, Guid.NewGuid(), 0m, false, false, Status.Active);

        p.UserId.Should().Be(userId, "UserId deve estar preenchido antes do clear.");

        p.ClearUser();

        p.UserId.Should().BeNull("ClearUser deve zerar o vínculo com a conta.");
    }

    [Fact]
    public void ClearUser_WhenAlreadyNull_ShouldRemainNull()
    {
        // Convidado criado sem conta
        var p = new PlayerEntity("Visitante", null, Guid.NewGuid(), 0m, false, true, Status.Active);

        p.ClearUser();

        p.UserId.Should().BeNull("Chamadas repetidas de ClearUser devem ser idempotentes.");
    }

    [Fact]
    public void ClearUser_ShouldNotAffectOtherFields()
    {
        var userId  = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var p = new PlayerEntity("Caio", userId, groupId, 8m, true, false, Status.Active);

        p.ClearUser();

        p.Name.Should().Be("Caio");
        p.GroupId.Should().Be(groupId);
        p.SkillPoints.Should().Be(8m);
        p.IsGoalkeeper.Should().BeTrue();
        p.Status.Should().Be(Status.Active);
    }

    [Fact]
    public void ClearUser_AfterSetUser_ShouldClearAgain()
    {
        var groupId = Guid.NewGuid();
        var p = new PlayerEntity("Caio", null, groupId, 0m, false, true, Status.Active);

        var newUserId = Guid.NewGuid();
        p.SetUser(newUserId);
        p.UserId.Should().Be(newUserId);

        p.ClearUser();

        p.UserId.Should().BeNull("ClearUser após SetUser deve funcionar corretamente.");
    }
}
