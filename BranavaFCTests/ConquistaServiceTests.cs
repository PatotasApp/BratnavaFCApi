using BratnavaFC.Application.Services;
using FluentAssertions;
using Xunit;

namespace BranavaFC.Tests;

public class ConquistaServiceTests
{
    [Fact]
    public void BuildCareerMarcos_ReturnsTheCompleteProgressTrack()
    {
        var goals = ConquistaService.BuildCareerMarcos(
            games: 12,
            goals: 12,
            assists: 4,
            mvps: 1)
            .Single(item => item.Id == "marco-gols");

        goals.Etapas.Select(stage => stage.Nome).Should().ContainInOrder(
            "Primeiro Gol",
            "Pé Quente",
            "Finalizador",
            "Matador",
            "Goleador",
            "Artilheiro Nato",
            "Lenda do Gol",
            "Máquina de Gols");
        goals.Etapas.Single(stage => stage.Nome == "Finalizador")
            .Desbloqueada.Should().BeTrue();
        goals.Etapas.Single(stage => stage.Nome == "Matador")
            .Desbloqueada.Should().BeFalse();
        goals.Etapas.Single(stage => stage.Nome == "Matador")
            .Descricao.Should().Be("Marque 25 gols.");
    }
}
