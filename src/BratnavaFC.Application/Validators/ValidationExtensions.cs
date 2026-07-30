using FluentValidation;

namespace BratnavaFC.Application.Validators;

public static class ValidationExtensions
{
    /// <summary>
    /// Roda o validator e devolve as mensagens de erro, ou null quando tudo passou.
    ///
    /// Os services traduzem essa lista em <c>Result.Fail</c> para que o erro saia no mesmo
    /// envelope do resto da API. É por isso que a validação é chamada explicitamente em vez
    /// de automática no pipeline do MVC: a auto-validação responderia
    /// ValidationProblemDetails, um formato diferente do que o front já consome.
    /// </summary>
    public static async Task<List<string>?> CollectErrorsAsync<T>(
        this IValidator<T> validator,
        T instance,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);

        return result.IsValid
            ? null
            : result.Errors.Select(failure => failure.ErrorMessage).ToList();
    }
}
