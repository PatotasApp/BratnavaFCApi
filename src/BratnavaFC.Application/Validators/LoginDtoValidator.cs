using BratnavaFC.Domain.Dtos.Authentication;
using FluentValidation;

namespace BratnavaFC.Application.Validators;

/// <summary>
/// Só obrigatoriedade: no login não se valida formato nem força de senha, porque a
/// credencial já existe e qualquer regra nova aqui trancaria quem está cadastrado.
///
/// O que isso resolve é o corpo incompleto — hoje um request sem Username chega em
/// <c>request.Username.Trim()</c> e vira NullReferenceException, ou seja 500 em vez de 400.
/// </summary>
public sealed class LoginDtoValidator : AbstractValidator<LoginDto>
{
    public LoginDtoValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Nome de usuário é obrigatório.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Senha é obrigatória.");
    }
}
