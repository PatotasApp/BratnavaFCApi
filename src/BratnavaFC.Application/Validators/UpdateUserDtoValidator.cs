using BratnavaFC.Domain.Dtos.Users;
using FluentValidation;

namespace BratnavaFC.Application.Validators;

/// <summary>
/// Valida a atualização de usuário. Todo campo é opcional — ausente significa "não mexer" —
/// então cada regra só roda quando o campo veio preenchido.
///
/// Role e Status ficam de fora: o UserService já os valida com Enum.IsDefined, e trazer isso
/// para cá duplicaria a regra em dois lugares.
/// </summary>
public sealed class UpdateUserDtoValidator : AbstractValidator<UpdateUserDto>
{
    public UpdateUserDtoValidator()
    {
        RuleFor(x => x.UserName)
            .ValidUserName()
            .When(x => !string.IsNullOrWhiteSpace(x.UserName));

        RuleFor(x => x.Email)
            .ValidEmail()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.FirstName)
            .MaximumLength(100).WithMessage("Nome não pode passar de 100 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.FirstName));

        RuleFor(x => x.LastName)
            .MaximumLength(100).WithMessage("Sobrenome não pode passar de 100 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.LastName));

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Telefone não pode passar de 20 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.BirthDate)
            .LessThan(_ => DateTimeOffset.UtcNow).WithMessage("Data de nascimento não pode ser no futuro.")
            .When(x => x.BirthDate.HasValue);
    }
}
