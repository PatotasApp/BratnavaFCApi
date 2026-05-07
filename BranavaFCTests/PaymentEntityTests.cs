using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;

namespace BranavaFC.Tests;

// ── MonthlyPaymentEntity ──────────────────────────────────────────────────────

public class MonthlyPaymentEntityTests
{
    private static MonthlyPaymentEntity Make(decimal amount = 100m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 2026, 5, amount);

    // ── Construtor ────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_HappyPath_ShouldSetStatusPending()
    {
        var sut = Make();

        sut.Status.Should().Be(PaymentStatus.Pending);
        sut.Amount.Should().Be(100m);
        sut.Discount.Should().Be(0m);
        sut.PaidAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_InvalidMonth_ShouldThrow()
    {
        var act = () => new MonthlyPaymentEntity(Guid.NewGuid(), Guid.NewGuid(), 2026, 13, 100m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_MonthZero_ShouldThrow()
    {
        var act = () => new MonthlyPaymentEntity(Guid.NewGuid(), Guid.NewGuid(), 2026, 0, 100m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_NegativeAmount_ShouldThrow()
    {
        var act = () => new MonthlyPaymentEntity(Guid.NewGuid(), Guid.NewGuid(), 2026, 5, -1m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_EmptyGroupId_ShouldThrow()
    {
        var act = () => new MonthlyPaymentEntity(Guid.Empty, Guid.NewGuid(), 2026, 5, 100m);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── MarkAsPaid ────────────────────────────────────────────────────────────

    [Fact]
    public void MarkAsPaid_ShouldSetStatusAndTimestamp()
    {
        var sut     = Make();
        var adminId = Guid.NewGuid();
        var before  = DateTime.UtcNow;

        sut.MarkAsPaid(adminId, null, null, null);

        sut.Status.Should().Be(PaymentStatus.Paid);
        sut.PaidAt.Should().NotBeNull();
        sut.PaidAt!.Value.Should().BeOnOrAfter(before);
        sut.MarkedByAdminId.Should().Be(adminId);
    }

    [Fact]
    public void MarkAsPaid_WithProof_ShouldSetProofFields()
    {
        var sut = Make();

        sut.MarkAsPaid(null, "base64data", "comprovante.jpg", "image/jpeg");

        sut.ProofBase64.Should().Be("base64data");
        sut.ProofFileName.Should().Be("comprovante.jpg");
        sut.ProofMimeType.Should().Be("image/jpeg");
    }

    [Fact]
    public void MarkAsPaid_WithoutAdmin_ShouldSetMarkedByNull()
    {
        var sut = Make();

        sut.MarkAsPaid(null, null, null, null);

        sut.MarkedByAdminId.Should().BeNull();
        sut.Status.Should().Be(PaymentStatus.Paid);
    }

    // ── MarkAsPending ─────────────────────────────────────────────────────────

    [Fact]
    public void MarkAsPending_ShouldRevertStatus()
    {
        var sut = Make();
        sut.MarkAsPaid(null, null, null, null);

        sut.MarkAsPending();

        sut.Status.Should().Be(PaymentStatus.Pending);
        sut.PaidAt.Should().BeNull();
    }

    // ── ApplyDiscount ─────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDiscount_PartialDiscount_ShouldNotMarkPaid()
    {
        var sut     = Make(100m);
        var adminId = Guid.NewGuid();

        sut.ApplyDiscount(30m, "Pontualidade", adminId);

        sut.Discount.Should().Be(30m);
        sut.Status.Should().Be(PaymentStatus.Pending);
        sut.DiscountReason.Should().Be("Pontualidade");
    }

    [Fact]
    public void ApplyDiscount_FullDiscount_ShouldMarkPaidAutomatically()
    {
        var sut     = Make(100m);
        var adminId = Guid.NewGuid();

        sut.ApplyDiscount(100m, "Bolsa total", adminId);

        sut.Status.Should().Be(PaymentStatus.Paid);
        sut.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public void ApplyDiscount_ExceedingAmount_ShouldStillMarkPaid()
    {
        var sut = Make(100m);

        sut.ApplyDiscount(150m, null, Guid.NewGuid());

        sut.Discount.Should().Be(150m);
        sut.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void ApplyDiscount_Accumulates()
    {
        var sut = Make(100m);

        sut.ApplyDiscount(30m, null, Guid.NewGuid());
        sut.ApplyDiscount(40m, null, Guid.NewGuid());

        sut.Discount.Should().Be(70m);
        sut.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void ApplyDiscount_Negative_ShouldThrow()
    {
        var sut = Make();

        var act = () => sut.ApplyDiscount(-10m, null, Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    // ── SetProof ──────────────────────────────────────────────────────────────

    [Fact]
    public void SetProof_ShouldUpdateProofFields()
    {
        var sut = Make();

        sut.SetProof("data", "file.pdf", "application/pdf");

        sut.ProofBase64.Should().Be("data");
        sut.ProofFileName.Should().Be("file.pdf");
        sut.ProofMimeType.Should().Be("application/pdf");
    }

    [Fact]
    public void SetProof_NullValues_ShouldClearProof()
    {
        var sut = Make();
        sut.SetProof("data", "file", "type");

        sut.SetProof(null, null, null);

        sut.ProofBase64.Should().BeNull();
        sut.ProofFileName.Should().BeNull();
        sut.ProofMimeType.Should().BeNull();
    }
}

// ── ExtraChargePaymentEntity ──────────────────────────────────────────────────

public class ExtraChargePaymentEntityTests
{
    private static ExtraChargePaymentEntity Make(decimal amount = 50m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount);

    // ── Construtor ────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_HappyPath_ShouldStartPending()
    {
        var sut = Make();

        sut.Status.Should().Be(PaymentStatus.Pending);
        sut.Amount.Should().Be(50m);
        sut.Discount.Should().Be(0m);
        sut.FinalAmount.Should().Be(50m);
    }

    [Fact]
    public void Constructor_EmptyExtraChargeId_ShouldThrow()
    {
        var act = () => new ExtraChargePaymentEntity(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 50m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_NegativeAmount_ShouldThrow()
    {
        var act = () => new ExtraChargePaymentEntity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -1m);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── FinalAmount ───────────────────────────────────────────────────────────

    [Fact]
    public void FinalAmount_AfterDiscount_ShouldBeAmountMinusDiscount()
    {
        var sut = Make(100m);

        sut.ApplyDiscount(30m, null, Guid.NewGuid());

        sut.FinalAmount.Should().Be(70m);
    }

    [Fact]
    public void FinalAmount_WhenDiscountExceedsAmount_ShouldReturnZero()
    {
        var sut = Make(50m);

        sut.ApplyDiscount(80m, null, Guid.NewGuid());

        sut.FinalAmount.Should().Be(0m);
    }

    // ── MarkAsPaid ────────────────────────────────────────────────────────────

    [Fact]
    public void MarkAsPaid_ShouldSetStatusPaidAndTimestamp()
    {
        var sut = Make();

        sut.MarkAsPaid(Guid.NewGuid(), null, null, null);

        sut.Status.Should().Be(PaymentStatus.Paid);
        sut.PaidAt.Should().NotBeNull();
    }

    // ── MarkAsPending ─────────────────────────────────────────────────────────

    [Fact]
    public void MarkAsPending_ShouldResetStatus()
    {
        var sut = Make();
        sut.MarkAsPaid(null, null, null, null);

        sut.MarkAsPending();

        sut.Status.Should().Be(PaymentStatus.Pending);
        sut.PaidAt.Should().BeNull();
    }

    // ── ApplyDiscount ─────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDiscount_FullCoverage_ShouldAutoMarkPaid()
    {
        var sut = Make(50m);

        sut.ApplyDiscount(50m, "Isenção total", Guid.NewGuid());

        sut.Status.Should().Be(PaymentStatus.Paid);
        sut.Discount.Should().Be(50m);
    }

    [Fact]
    public void ApplyDiscount_NullReason_ShouldNotChangeExistingReason()
    {
        var sut = Make();
        sut.ApplyDiscount(10m, "Motivo inicial", Guid.NewGuid());

        sut.ApplyDiscount(5m, null, Guid.NewGuid());

        sut.DiscountReason.Should().Be("Motivo inicial");
    }

    [Fact]
    public void ApplyDiscount_NegativeValue_ShouldThrow()
    {
        var sut = Make();

        var act = () => sut.ApplyDiscount(-5m, null, Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }
}
