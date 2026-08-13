using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Xunit;

namespace BratnavaFC.Tests;

public sealed class MatchRewindTests
{
    [Fact]
    public void Acceptation_CannotRewindToCreated()
    {
        var match = NewMatch();
        match.OpenAcceptation();

        var exception = Assert.Throws<InvalidOperationException>(match.RewindOneStep);

        Assert.Contains("aceitacao", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MatchStatus.Acceptation, match.Status);
    }

    [Theory]
    [InlineData(MatchStatus.Started)]
    [InlineData(MatchStatus.Ended)]
    [InlineData(MatchStatus.PostGame)]
    [InlineData(MatchStatus.Finalized)]
    public void StartedOrLater_CannotRewind(MatchStatus status)
    {
        var match = NewMatch();
        SetStatus(match, status);

        var exception = Assert.Throws<InvalidOperationException>(match.RewindOneStep);

        Assert.Contains("iniciada", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(status, match.Status);
    }

    [Fact]
    public void MatchMaking_RewindsToAcceptation()
    {
        var match = NewMatch();
        SetStatus(match, MatchStatus.MatchMaking);

        match.RewindOneStep();

        Assert.Equal(MatchStatus.Acceptation, match.Status);
    }

    private static MatchEntity NewMatch() =>
        new(Guid.NewGuid(), DateTime.UtcNow.AddDays(1), "Quadra");

    private static void SetStatus(MatchEntity match, MatchStatus status)
    {
        typeof(MatchEntity)
            .GetProperty(nameof(MatchEntity.Status))!
            .SetValue(match, status);
    }
}
