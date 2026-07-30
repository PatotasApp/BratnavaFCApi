using BratnavaFC.Domain.Dtos.Users;
using FluentValidation;

namespace BratnavaFC.Application.Validators;

/// <summary>
/// A senha atual só precisa estar presente — validar formato dela trancaria quem tem senha
/// legada curta. Já a nova senha está sendo definida agora, então vale o mínimo do Firebase
/// Auth: senha menor que isso não conseguiria ser criada como credencial lá.
/// </summary>
public sealed class ChangePasswordDtoValidator : AbstractValidator<ChangePasswordDto>
{
    private const int MinPasswordLength = 6;

    public ChangePasswordDtoValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Senha atual é obrigatória.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Nova senha é obrigatória.")
            .MinimumLength(MinPasswordLength)
                .WithMessage($"Nova senha precisa de ao menos {MinPasswordLength} caracteres.")
            .NotEqual(x => x.CurrentPassword)
                .WithMessage("A nova senha precisa ser diferente da atual.");
    }
}
