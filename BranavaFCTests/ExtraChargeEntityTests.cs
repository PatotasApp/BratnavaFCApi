using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

public class ExtraChargeEntityTests
{
    private static ExtraChargeEntity Make(
        decimal amount = 50m,
        string name = "Churrasco",
        string? description = "desc") =>
        new(Guid.NewGuid(), name, description, amount, null, Guid.NewGuid());

    // ── Constructor ───────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_EmptyGroupId_ShouldThrow()
    {
        var act = () => new ExtraChargeEntity(Guid.Empty, "N", null, 10m, null, Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>().WithMessage("*GroupId*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_BlankName_ShouldThrow(string name)
    {
        var act = () => new ExtraChargeEntity(Guid.NewGuid(), name, null, 10m, null, Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>().WithMessage("*Nome*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Ctor_NonPositiveAmount_ShouldThrow(decimal amount)
    {
        var act = () => new ExtraChargeEntity(Guid.NewGuid(), "N", null, amount, null, Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>().WithMessage("*Valor*");
    }

    [Fact]
    public void Ctor_Valid_ShouldTrimNameAndDescription()
    {
        var groupId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var due     = new DateOnly(2026, 8, 1);

        var charge = new ExtraChargeEntity(groupId, "  Festa  ", "  detalhe  ", 25m, due, adminId);

        charge.GroupId.Should().Be(groupId);
        charge.Name.Should().Be("Festa");
        charge.Description.Should().Be("detalhe");
        charge.Amount.Should().Be(25m);
        charge.DueDate.Should().Be(due);
        charge.CreatedByAdminId.Should().Be(adminId);
        charge.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public void Ctor_NullDescription_ShouldKeepNull()
    {
        var charge = Make(description: null);
        charge.Description.Should().BeNull();
    }

    // ── Cancel / Reactivate ───────────────────────────────────────────────────

    [Fact]
    public void Cancel_ThenReactivate_ShouldToggleIsCancelled()
    {
        var charge = Make();

        charge.Cancel();
        charge.IsCancelled.Should().BeTrue();

        charge.Reactivate();
        charge.IsCancelled.Should().BeFalse();
    }

    // ── UpdateDetails ─────────────────────────────────────────────────────────

    [Fact]
    public void UpdateDetails_AllNull_ShouldChangeNothing()
    {
        var charge = Make(amount: 30m, name: "Original", description: "Desc");

        charge.UpdateDetails(null, null, null);

        charge.Name.Should().Be("Original");
        charge.Description.Should().Be("Desc");
        charge.Amount.Should().Be(30m);
    }

    [Fact]
    public void UpdateDetails_WhitespaceName_ShouldThrow()
    {
        var charge = Make();
        var act = () => charge.UpdateDetails("   ", null, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Nome*");
    }

    [Fact]
    public void UpdateDetails_ValidName_ShouldTrim()
    {
        var charge = Make();
        charge.UpdateDetails("  Novo Nome  ", null, null);
        charge.Name.Should().Be("Novo Nome");
    }

    [Fact]
    public void UpdateDetails_EmptyDescription_ShouldClearToNull()
    {
        var charge = Make(description: "algo");
        charge.UpdateDetails(null, "   ", null);
        charge.Description.Should().BeNull();
    }

    [Fact]
    public void UpdateDetails_Description_ShouldTrim()
    {
        var charge = Make();
        charge.UpdateDetails(null, "  nova desc  ", null);
        charge.Description.Should().Be("nova desc");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateDetails_NonPositiveAmount_ShouldThrow(decimal amount)
    {
        var charge = Make();
        var act = () => charge.UpdateDetails(null, null, amount);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Valor*");
    }

    [Fact]
    public void UpdateDetails_ValidAmount_ShouldUpdate()
    {
        var charge = Make(amount: 30m);
        charge.UpdateDetails(null, null, 75m);
        charge.Amount.Should().Be(75m);
    }
}
