using BratnavaFC.Domain.Dtos.Users;
using FluentValidation;

namespace BratnavaFC.Application.Validators;

/// <summary>
/// Valida o cadastro de usuário. Antes destas regras, campo obrigatório em branco chegava
/// até o construtor de UserEntity e virava InvalidOperationException — 500 em vez de 400.
/// </summary>
public sealed class CreateUserDtoValidator : AbstractValidator<CreateUserDto>
{
    /// <summary>Mínimo aceito pelo Firebase Auth para senha.</summary>
    private const int MinPasswordLength = 6;

    public CreateUserDtoValidator()
    {
        RuleFor(x => x.UserName).ValidUserName();

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("Nome é obrigatório.")
            .MaximumLength(100).WithMessage("Nome não pode passar de 100 caracteres.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Sobrenome é obrigatório.")
            .MaximumLength(100).WithMessage("Sobrenome não pode passar de 100 caracteres.");

        RuleFor(x => x.Email).ValidEmail();

        // O mínimo do Firebase vale desde já: senha mais curta que isso impediria a criação
        // da credencial lá, e aqui ainda não existe usuário para quebrar.
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Senha é obrigatória.")
            .MinimumLength(MinPasswordLength)
                .WithMessage($"Senha precisa de ao menos {MinPasswordLength} caracteres.");

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Telefone não pode passar de 20 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.BirthDate)
            .LessThan(_ => DateTimeOffset.UtcNow).WithMessage("Data de nascimento não pode ser no futuro.")
            .When(x => x.BirthDate.HasValue);
    }
}
