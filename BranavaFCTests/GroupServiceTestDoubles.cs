using BratnavaFC.Application.Abstractions;
using Moq;

namespace BranavaFC.Tests;

public static class GroupServiceTestDoubles
{
    /// <summary>
    /// IGroupService neutro: diz que nenhuma patota ficou abandonada.
    ///
    /// É o default certo para a maioria dos testes, que não têm nada a ver com essa regra e
    /// só precisam que a dependência exista. Sem o setup explícito, o Moq devolveria uma
    /// Task com null e o <c>Contains</c> de quem chama estouraria — o que transformaria um
    /// detalhe de construção em falha no meio do teste, longe da causa.
    ///
    /// Quem testa a regra passa o próprio mock.
    /// </summary>
    public static IGroupService GroupServiceStub()
    {
        var groups = new Mock<IGroupService>();

        groups.Setup(x => x.FindAbandonedByAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);

        return groups.Object;
    }
}
