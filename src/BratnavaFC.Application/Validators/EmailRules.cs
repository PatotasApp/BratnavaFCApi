using System.Text.RegularExpressions;
using BratnavaFC.Domain.Common;
using FluentValidation;

namespace BratnavaFC.Application.Validators;

/// <summary>
/// Regra de email compartilhada entre criação e atualização de usuário.
///
/// Não usa o <c>EmailAddress()</c> do FluentValidation: desde a v11 ele apenas verifica se
/// existe um "@" com conteúdo dos dois lados, o mesmo comportamento do EmailAddressAttribute
/// do ASP.NET, então "joao@n" e "admin@localhost" passariam. Como o endereço vira credencial
/// no Firebase Auth e é por ele que a verificação de conta chega, o TLD tem que ser real.
///
/// Todas as regras avaliam o valor já sem espaço nas pontas, porque é assim que a entidade
/// grava — espaço acidental é erro de digitação, não email inválido.
/// </summary>
public static class EmailRules
{
    /// <summary>Limites da RFC 5321: 254 no endereço inteiro, 64 na parte local.</summary>
    private const int MaxTotalLength = 254;
    private const int MaxLocalPartLength = 64;

    /// <summary>
    /// Parte local com os caracteres de uso corrente, domínio em labels alfanuméricos
    /// separados por ponto e TLD só de letras com ao menos 2 caracteres. Exigir 1+ caractere
    /// por label é o que também descarta pontos consecutivos.
    /// </summary>
    private static readonly Regex Pattern = new(
        @"^[A-Za-z0-9._%+-]+@[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IRuleBuilderOptions<T, string?> ValidEmail<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .NotEmpty()
                .WithMessage("Email é obrigatório.")
            .Must(email => Length(email) <= MaxTotalLength)
                .WithMessage($"Email não pode passar de {MaxTotalLength} caracteres.")
            .Must(email => Matches(email))
                .WithMessage("Email inválido.")
            .Must(email => LocalPartLength(email) <= MaxLocalPartLength)
                .WithMessage($"A parte do email antes do @ não pode passar de {MaxLocalPartLength} caracteres.")
            .Must(email => !IsBlocked(email))
                .WithMessage("Use um email real: domínios de teste e descartáveis não são aceitos.");

    // Cada predicado devolve "válido" para entrada vazia: quem reporta esse caso é o
    // NotEmpty, e repetir a checagem aqui empilharia mensagens para o mesmo problema.

    private static int Length(string? email) =>
        email?.Trim().Length ?? 0;

    private static bool Matches(string? email) =>
        string.IsNullOrWhiteSpace(email) || Pattern.IsMatch(email.Trim());

    private static int LocalPartLength(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return 0;

        var atIndex = email.Trim().IndexOf('@');

        return atIndex < 0 ? 0 : atIndex;
    }

    private static bool IsBlocked(string? email) =>
        !string.IsNullOrWhiteSpace(email) && EmailAddress.HasBlockedDomain(email);
}
