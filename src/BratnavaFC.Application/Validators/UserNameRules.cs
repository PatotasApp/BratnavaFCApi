using System.Text.RegularExpressions;
using FluentValidation;

namespace BratnavaFC.Application.Validators;

/// <summary>
/// Regra de username compartilhada entre criação e atualização.
///
/// O username é identificador de login, então não aceita espaço no meio nem caractere que
/// mude de forma ao ser normalizado. Espaço nas pontas, ao contrário, é erro de digitação:
/// a entidade os remove ao gravar, então as regras avaliam o valor já trimado. A caixa é
/// irrelevante aqui — a canonização para minúsculas acontece na escrita
/// (ver <c>UserEntity.SetUserName</c>).
/// </summary>
public static class UserNameRules
{
    private const int MinLength = 3;
    private const int MaxLength = 50;

    /// <summary>Letras (inclusive acentuadas), dígitos, ponto, underscore e hífen.</summary>
    private static readonly Regex Pattern = new(
        @"^[\p{L}\p{N}._-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IRuleBuilderOptions<T, string?> ValidUserName<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .NotEmpty()
                .WithMessage("Nome de usuário é obrigatório.")
            .Must(userName => Length(userName) >= MinLength)
                .WithMessage($"Nome de usuário precisa de ao menos {MinLength} caracteres.")
            .Must(userName => Length(userName) <= MaxLength)
                .WithMessage($"Nome de usuário não pode passar de {MaxLength} caracteres.")
            .Must(userName => Matches(userName))
                .WithMessage("Nome de usuário aceita apenas letras, números, ponto, underscore e hífen.");

    // Entrada vazia é reportada pelo NotEmpty; os predicados a tratam como válida para não
    // empilhar mensagens sobre o mesmo problema.

    private static int Length(string? userName) =>
        string.IsNullOrWhiteSpace(userName) ? MinLength : userName.Trim().Length;

    private static bool Matches(string? userName) =>
        string.IsNullOrWhiteSpace(userName) || Pattern.IsMatch(userName.Trim());
}
