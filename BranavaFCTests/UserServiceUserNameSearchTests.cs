using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// O UserName ganhou índice único e passou a ser a chave usada pelas telas que escolhem uma
/// pessoa para conceder algo num grupo (convite, admin, financeiro). Escolher a pessoa errada
/// ali tem consequência — no convite, o histórico do jogador troca de dono.
///
/// Por isso essas telas buscam pelo campo UserName, e não pela busca ampla do Search, que casa
/// nome e e-mail e devolve gente parecida. O Search continua existindo para a tela de
/// administração de usuários, onde procurar por nome é o comportamento desejado.
/// </summary>
public class UserServiceUserNameSearchTests
{
    private static UserService Sut(AppDbContext db)
        => new(db, Mock.Of<IRepositoryBase<UserEntity>>(), Mock.Of<ILogger<UserService>>());

    private static UserEntity User(string userName, string email, string first = "Primeiro", string last = "Ultimo")
        => new(userName, first, last, email, "hash", null, null);

    [Fact]
    public async Task UserName_MatchesByHandlePrefix()
    {
        await using var db = DbContextFactory.Create(nameof(UserName_MatchesByHandlePrefix));
        db.Users.AddRange(
            User("joao.pedro", "a@x.com"),
            User("maria", "b@x.com"));
        await db.SaveChangesAsync();

        var result = await Sut(db).GetAllAsync(
            new ListUsersRequestDto { UserName = "joao" },
            CancellationToken.None);

        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items[0].UserName.Should().Be("joao.pedro");
    }

    [Fact]
    public async Task UserName_IsCaseInsensitive()
    {
        await using var db = DbContextFactory.Create(nameof(UserName_IsCaseInsensitive));
        db.Users.Add(User("Joao.Pedro", "a@x.com"));
        await db.SaveChangesAsync();

        var result = await Sut(db).GetAllAsync(
            new ListUsersRequestDto { UserName = "joao.pedro" },
            CancellationToken.None);

        result.Data!.Items.Should().HaveCount(1);
    }

    /// <summary>
    /// O ponto da mudança: quem busca por handle não pode receber gente que só casou por nome
    /// ou e-mail — é exatamente esse ruído que faz o admin escolher a pessoa errada.
    /// </summary>
    [Fact]
    public async Task UserName_DoesNotMatchNameOrEmail()
    {
        await using var db = DbContextFactory.Create(nameof(UserName_DoesNotMatchNameOrEmail));
        db.Users.AddRange(
            User("handle1", "joao@x.com", first: "Joao", last: "Silva"),
            User("handle2", "outro@x.com", first: "Joana", last: "Joao"));
        await db.SaveChangesAsync();

        var result = await Sut(db).GetAllAsync(
            new ListUsersRequestDto { UserName = "joao" },
            CancellationToken.None);

        result.Data!.Items.Should().BeEmpty();
    }

    /// <summary>Regressão: a tela de administração depende do Search amplo.</summary>
    [Fact]
    public async Task Search_StillMatchesNameAndEmail()
    {
        await using var db = DbContextFactory.Create(nameof(Search_StillMatchesNameAndEmail));
        db.Users.AddRange(
            User("handle1", "joao@x.com", first: "Joao", last: "Silva"),
            User("handle2", "maria@x.com", first: "Maria", last: "Souza"));
        await db.SaveChangesAsync();

        var byName  = await Sut(db).GetAllAsync(new ListUsersRequestDto { Search = "joao" }, CancellationToken.None);
        var byEmail = await Sut(db).GetAllAsync(new ListUsersRequestDto { Search = "maria@x" }, CancellationToken.None);

        byName.Data!.Items.Should().HaveCount(1);
        byEmail.Data!.Items.Should().HaveCount(1);
    }
}
