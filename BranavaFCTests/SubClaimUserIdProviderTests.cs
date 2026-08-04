using System.Security.Claims;
using BratnavaFC.Api.Realtime;
using FluentAssertions;

namespace BranavaFC.Tests;

/// <summary>
/// Peça de falha silenciosa: se o id devolvido aqui não casar EXATAMENTE com a string passada
/// em <c>Clients.User(userId.ToString())</c>, o SignalR não acha destinatário e o evento do
/// sininho desaparece — sem exceção, sem log, sem nada. Daí os testes de formato.
/// </summary>
public class SubClaimUserIdProviderTests
{
    private static readonly Guid UserId = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    [Fact]
    public void Reads_the_sub_claim()
    {
        SubClaimUserIdProvider.FromPrincipal(Principal(("sub", UserId.ToString())))
            .Should().Be(UserId.ToString());
    }

    [Theory]
    // O SignalR compara por igualdade exata de string, então o formato do que chega na claim
    // não pode vazar para o resultado: caixa alta, chaves e parênteses precisam normalizar
    // todos para o mesmo "d" que Guid.ToString() produz.
    [InlineData("3F2504E0-4F89-11D3-9A0C-0305E82C3301")]
    [InlineData("{3f2504e0-4f89-11d3-9a0c-0305e82c3301}")]
    [InlineData("(3f2504e0-4f89-11d3-9a0c-0305e82c3301)")]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301")]
    public void Normalizes_any_accepted_guid_format_to_the_emitter_format(string claimValue)
    {
        SubClaimUserIdProvider.FromPrincipal(Principal(("sub", claimValue)))
            .Should().Be(UserId.ToString(), "e o emissor manda Clients.User(userId.ToString())");
    }

    [Fact]
    public void Ignores_ClaimTypes_NameIdentifier()
    {
        // O provider PADRÃO do SignalR usa NameIdentifier. Este JWT não popula essa claim —
        // se algum dia passar a popular com outro valor, não é ela que manda.
        SubClaimUserIdProvider
            .FromPrincipal(Principal((ClaimTypes.NameIdentifier, UserId.ToString())))
            .Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nao-e-guid")]
    public void Returns_null_for_an_unusable_sub(string claimValue)
    {
        SubClaimUserIdProvider.FromPrincipal(Principal(("sub", claimValue)))
            .Should().BeNull();
    }

    [Fact]
    public void Returns_null_without_a_principal()
    {
        SubClaimUserIdProvider.FromPrincipal(null).Should().BeNull();
        SubClaimUserIdProvider.FromPrincipal(new ClaimsPrincipal()).Should().BeNull();
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value))));
}
