namespace BratnavaFC.Domain.Dtos.Users;

/// <summary>
/// Edição do próprio perfil. Só campos cosméticos.
///
/// NÃO existe campo de e-mail aqui, e um "email" enviado no payload é simplesmente ignorado
/// pela desserialização: o e-mail é gerenciado no Firebase e é read-only nesta fase — mudá-lo
/// pela API abriria caminho para account takeover e dessincronizaria os dois lados.
/// Senha também não: quem troca é o SDK no front-end.
/// </summary>
public sealed class UpdateMeDto
{
    /// <summary>Apelido de identificação no app. Precisa ser único.</summary>
    public string? UserName { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Nulo limpa o telefone.</summary>
    public string? Phone { get; set; }
}
