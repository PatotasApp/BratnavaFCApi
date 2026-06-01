using BratnavaFC.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Testes para MatchPlayerEntity.SetDidNotPlay() —
/// flag que marca jogadores que confirmaram presença mas não apareceram.
/// </summary>
public class MatchPlayerEntity_DidNotPlayTests
{
    [Fact]
    public void SetDidNotPlay_True_ShouldSetFlag()
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());

        // Act
        mp.SetDidNotPlay(true);

        // Assert
        mp.DidNotPlay.Should().BeTrue();
    }

    [Fact]
    public void SetDidNotPlay_False_ShouldClearFlag()
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());
        mp.SetDidNotPlay(true);

        // Act — desfaz a marcação
        mp.SetDidNotPlay(false);

        // Assert
        mp.DidNotPlay.Should().BeFalse();
    }

    [Fact]
    public void NewMatchPlayer_DidNotPlay_ShouldDefaultToFalse()
    {
        // Arrange + Act
        var mp = new MatchPlayerEntity(Guid.NewGuid());

        // Assert
        mp.DidNotPlay.Should().BeFalse("padrão deve ser false — jogador está presente até ser marcado como ausente.");
    }

    [Fact]
    public void SetDidNotPlay_IsIdempotent_WhenCalledTwiceWithSameValue()
    {
        // Arrange
        var mp = new MatchPlayerEntity(Guid.NewGuid());

        // Act
        mp.SetDidNotPlay(true);
        mp.SetDidNotPlay(true);

        // Assert
        mp.DidNotPlay.Should().BeTrue("chamadas repetidas com o mesmo valor não devem causar erro.");
    }

    [Fact]
    public void SetDidNotPlay_DoesNotAffectOtherProperties()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var mp = new MatchPlayerEntity(playerId);
        mp.SetTeam(1);

        // Act
        mp.SetDidNotPlay(true);

        // Assert — outras propriedades mantêm seus valores
        mp.PlayerId.Should().Be(playerId);
        mp.Team.Should().Be(1);
        mp.DidNotPlay.Should().BeTrue();
    }
}
