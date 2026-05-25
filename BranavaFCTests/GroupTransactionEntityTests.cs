using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;

namespace BranavaFC.Tests;

public class GroupTransactionEntityTests
{
    private static readonly Guid _groupId = Guid.NewGuid();

    // ── Construtor manual ─────────────────────────────────────────────────────

    [Fact]
    public void ManualConstructor_Income_ShouldSetFields()
    {
        var userId = Guid.NewGuid();
        var date   = new DateOnly(2026, 5, 1);

        var sut = new GroupTransactionEntity(
            _groupId, TransactionType.Income, 150m, "Mensalidade", date, userId);

        sut.GroupId.Should().Be(_groupId);
        sut.Type.Should().Be(TransactionType.Income);
        sut.Amount.Should().Be(150m);
        sut.Description.Should().Be("Mensalidade");
        sut.Date.Should().Be(date);
        sut.Category.Should().BeNull();
        sut.IsAutomatic.Should().BeFalse();
        sut.SourceType.Should().Be(TransactionSourceType.Manual);
        sut.CreatedByUserId.Should().Be(userId);
        sut.SourceId.Should().BeNull();
        sut.PlayerName.Should().BeNull();
    }

    [Fact]
    public void ManualConstructor_Expense_ShouldRequireCategory()
    {
        var act = () => new GroupTransactionEntity(
            _groupId, TransactionType.Expense, 100m, "Aluguel",
            new DateOnly(2026, 5, 1), Guid.NewGuid(), category: null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Categoria*");
    }

    [Fact]
    public void ManualConstructor_Expense_WithCategory_ShouldSetCategory()
    {
        var sut = new GroupTransactionEntity(
            _groupId, TransactionType.Expense, 200m, "Aluguel da quadra",
            new DateOnly(2026, 5, 1), Guid.NewGuid(), TransactionCategory.AluguelDeQuadra);

        sut.Type.Should().Be(TransactionType.Expense);
        sut.Category.Should().Be(TransactionCategory.AluguelDeQuadra);
        sut.IsAutomatic.Should().BeFalse();
    }

    [Fact]
    public void ManualConstructor_ZeroAmount_ShouldThrow()
    {
        var act = () => new GroupTransactionEntity(
            _groupId, TransactionType.Income, 0m, "desc",
            new DateOnly(2026, 5, 1), Guid.NewGuid());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Amount*");
    }

    [Fact]
    public void ManualConstructor_NegativeAmount_ShouldThrow()
    {
        var act = () => new GroupTransactionEntity(
            _groupId, TransactionType.Income, -1m, "desc",
            new DateOnly(2026, 5, 1), Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ManualConstructor_EmptyGroupId_ShouldThrow()
    {
        var act = () => new GroupTransactionEntity(
            Guid.Empty, TransactionType.Income, 100m, "desc",
            new DateOnly(2026, 5, 1), Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ManualConstructor_EmptyDescription_ShouldThrow()
    {
        var act = () => new GroupTransactionEntity(
            _groupId, TransactionType.Income, 100m, "  ",
            new DateOnly(2026, 5, 1), Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    // ── Construtor automático ─────────────────────────────────────────────────

    [Fact]
    public void AutoConstructor_HappyPath_ShouldSetAutomaticFields()
    {
        var sourceId = Guid.NewGuid();
        var date     = new DateOnly(2026, 5, 1);

        var sut = new GroupTransactionEntity(
            _groupId, 100m, "Mensalidade Maio/2026 – João",
            date, TransactionSourceType.MonthlyPayment, sourceId, "João");

        sut.Type.Should().Be(TransactionType.Income);
        sut.IsAutomatic.Should().BeTrue();
        sut.SourceType.Should().Be(TransactionSourceType.MonthlyPayment);
        sut.SourceId.Should().Be(sourceId);
        sut.PlayerName.Should().Be("João");
        sut.Category.Should().BeNull();
        sut.CreatedByUserId.Should().BeNull();
    }

    [Fact]
    public void AutoConstructor_ZeroAmount_ShouldThrow()
    {
        var act = () => new GroupTransactionEntity(
            _groupId, 0m, "desc",
            new DateOnly(2026, 5, 1), TransactionSourceType.MonthlyPayment, Guid.NewGuid());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Amount*");
    }

    [Fact]
    public void AutoConstructor_NegativeAmount_ShouldThrow()
    {
        var act = () => new GroupTransactionEntity(
            _groupId, -50m, "desc",
            new DateOnly(2026, 5, 1), TransactionSourceType.ExtraCharge, Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AutoConstructor_EmptyGroupId_ShouldThrow()
    {
        var act = () => new GroupTransactionEntity(
            Guid.Empty, 100m, "desc",
            new DateOnly(2026, 5, 1), TransactionSourceType.MonthlyPayment, Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }
}
