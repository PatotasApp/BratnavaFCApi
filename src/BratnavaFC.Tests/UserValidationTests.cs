using BratnavaFC.Application.Validators;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using Xunit;

namespace BratnavaFC.Tests;

/// <summary>
/// Cobre os gates que preparam o cadastro para o Firebase Auth: normalização na entidade,
/// formato de email com TLD real, bloqueio de domínio descartável e liberação explícita do
/// relay da Apple.
/// </summary>
public class UserValidationTests
{
    // ── Normalização na entidade ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Joao", "joao")]
    [InlineData("  JOAO  ", "joao")]
    [InlineData("JoAo.Silva", "joao.silva")]
    public void SetUserName_NormalizesToLowerCase(string input, string expected)
    {
        var user = MakeUser(userName: input);

        Assert.Equal(expected, user.UserName);
    }

    [Theory]
    [InlineData("Joao@Gmail.COM", "joao@gmail.com")]
    [InlineData("  joao@gmail.com  ", "joao@gmail.com")]
    public void SetEmail_NormalizesToLowerCase(string input, string expected)
    {
        var user = MakeUser(email: input);

        Assert.Equal(expected, user.Email);
    }

    /// <summary>
    /// O login busca o username já em minúsculas. Antes da normalização na escrita, criar
    /// "Joao" gravava "Joao" e a busca por "joao" nunca encontrava — o usuário ficava sem
    /// acesso com qualquer caixa digitada.
    /// </summary>
    [Fact]
    public void SetUserName_MakesUserReachableByLowercaseLookup()
    {
        var user = MakeUser(userName: "Joao");

        Assert.Equal("Joao".Trim().ToLowerInvariant(), user.UserName);
    }

    // ── Formato de email ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("joao@gmail.com")]
    [InlineData("joao.silva+tag@empresa.com.br")]
    [InlineData("j@sub.dominio.io")]
    [InlineData("abc123def@privaterelay.appleid.com")]
    public void CreateUser_AcceptsValidEmail(string email)
    {
        var result = ValidateCreate(email: email);

        Assert.True(result.IsValid, $"esperava '{email}' válido, erros: {Messages(result)}");
    }

    [Theory]
    [InlineData("", "vazio")]
    [InlineData("joao", "sem @")]
    [InlineData("joao@n", "sem TLD")]
    [InlineData("joao@localhost", "sem TLD")]
    [InlineData("joao@dominio.c", "TLD de 1 caractere")]
    [InlineData("joao@@gmail.com", "dois @")]
    [InlineData("joao silva@gmail.com", "espaço na parte local")]
    [InlineData("joao@gmail..com", "pontos consecutivos")]
    [InlineData("joao@.gmail.com", "domínio começa com ponto")]
    [InlineData("joao@-gmail.com", "domínio começa com hífen")]
    public void CreateUser_RejectsMalformedEmail(string email, string reason)
    {
        var result = ValidateCreate(email: email);

        Assert.False(result.IsValid, $"esperava '{email}' rejeitado ({reason})");
    }

    // ── Domínios bloqueados ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("joao@example.com")]
    [InlineData("joao@mailinator.com")]
    [InlineData("joao@yopmail.com")]
    [InlineData("joao@mail.example.com")]
    public void CreateUser_RejectsDisposableOrReservedDomain(string email)
    {
        var result = ValidateCreate(email: email);

        Assert.False(result.IsValid, $"esperava '{email}' rejeitado por domínio bloqueado");
    }

    /// <summary>
    /// Quando o usuário escolhe "Ocultar meu email" no Sign in with Apple, este é o único
    /// endereço que recebemos. Bloqueá-lo inviabilizaria o login com Apple.
    /// </summary>
    [Fact]
    public void ApplePrivateRelay_IsNotBlocked()
    {
        Assert.False(EmailAddress.HasBlockedDomain("abc123@privaterelay.appleid.com"));
    }

    [Fact]
    public void BlockedDomainCheck_IsCaseInsensitive()
    {
        Assert.True(EmailAddress.HasBlockedDomain("Joao@MAILINATOR.com"));
    }

    // ── Username ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("jo", "curto demais")]
    [InlineData("joao silva", "contém espaço")]
    [InlineData("joao@silva", "caractere não permitido")]
    [InlineData("", "vazio")]
    public void CreateUser_RejectsInvalidUserName(string userName, string reason)
    {
        var result = ValidateCreate(userName: userName);

        Assert.False(result.IsValid, $"esperava '{userName}' rejeitado ({reason})");
    }

    [Theory]
    [InlineData("joao")]
    [InlineData("joao.silva")]
    [InlineData("joao_silva-99")]
    [InlineData("joão")]
    public void CreateUser_AcceptsValidUserName(string userName)
    {
        var result = ValidateCreate(userName: userName);

        Assert.True(result.IsValid, $"esperava '{userName}' válido, erros: {Messages(result)}");
    }

    // ── Senha ────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateUser_RejectsPasswordShorterThanFirebaseMinimum()
    {
        var result = ValidateCreate(password: "12345");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ChangePassword_RejectsNewPasswordEqualToCurrent()
    {
        var result = new ChangePasswordDtoValidator().Validate(new ChangePasswordDto
        {
            CurrentPassword = "senha123",
            NewPassword = "senha123"
        });

        Assert.False(result.IsValid);
    }

    // ── Update ───────────────────────────────────────────────────────────────────

    /// <summary>Campo ausente significa "não mexer", então um DTO vazio é válido.</summary>
    [Fact]
    public void UpdateUser_AcceptsEmptyDto()
    {
        var result = new UpdateUserDtoValidator().Validate(new UpdateUserDto());

        Assert.True(result.IsValid, Messages(result));
    }

    [Fact]
    public void UpdateUser_RejectsMalformedEmailWhenProvided()
    {
        var result = new UpdateUserDtoValidator().Validate(new UpdateUserDto { Email = "joao@n" });

        Assert.False(result.IsValid);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static UserEntity MakeUser(string userName = "joao", string email = "joao@gmail.com") =>
        new(userName, "Joao", "Silva", email, "hash", null, null);

    private static FluentValidation.Results.ValidationResult ValidateCreate(
        string userName = "joao",
        string email = "joao@gmail.com",
        string password = "senha123") =>
        new CreateUserDtoValidator().Validate(
            new CreateUserDto(userName, "Joao", "Silva", email, password, null, null));

    private static string Messages(FluentValidation.Results.ValidationResult result) =>
        string.Join(" | ", result.Errors.Select(e => e.ErrorMessage));
}
