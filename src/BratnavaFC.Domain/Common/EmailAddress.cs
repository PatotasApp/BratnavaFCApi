namespace BratnavaFC.Domain.Common;

/// <summary>
/// Normalização e política de domínio do email.
///
/// O email deixa de ser um dado só nosso quando ele passa a ser a credencial de login
/// no Firebase Auth: lá o endereço tem que ser único (comparado case-insensitive) e
/// precisa ser alcançável, porque é por ele que a verificação de conta acontece.
///
/// A validação de formato NÃO vive aqui — ela é regra de entrada e está nos validators
/// do FluentValidation. Aqui ficam as duas coisas que o validator não resolve:
/// a normalização, que precisa acontecer na escrita da entidade para o índice único do
/// banco significar algo, e a lista de domínios que não servem como credencial.
/// </summary>
public static class EmailAddress
{
    /// <summary>
    /// Domínios reservados por RFC 2606 e serviços de email descartável. Uma conta criada
    /// com um destes nunca recebe o email de verificação do Firebase, então o usuário fica
    /// impossibilitado de verificar a conta — vira um registro morto no banco.
    ///
    /// Deliberadamente curta: não é antifraude, é só um filtro de lixo óbvio. A prova real
    /// de que o endereço existe é a verificação de email do Firebase.
    /// </summary>
    private static readonly string[] BlockedDomains =
    [
        // RFC 2606 / RFC 6761 — reservados para documentação e teste
        "example.com", "example.net", "example.org",
        "test", "invalid", "localhost", "local",

        // Descartáveis
        "mailinator.com", "yopmail.com", "tempmail.com", "temp-mail.org",
        "10minutemail.com", "guerrillamail.com", "trashmail.com", "sharklasers.com",
        "throwawaymail.com", "maildrop.cc", "getnada.com",
    ];

    /// <summary>
    /// Domínios que parecem descartáveis mas são legítimos e precisam passar.
    ///
    /// O relay da Apple é o caso crítico: quando o usuário escolhe "Ocultar meu email"
    /// no Sign in with Apple, é este o endereço que chega — e é o único que temos daquela
    /// pessoa. Bloqueá-lo inviabiliza o login com Apple.
    /// </summary>
    private static readonly string[] AllowedDomains =
    [
        "privaterelay.appleid.com",
    ];

    /// <summary>
    /// Forma canônica: sem espaços nas pontas e em minúsculas.
    ///
    /// Normalizar na escrita é o que torna o índice único do banco suficiente — sem isso,
    /// "Joao@x.com" e "joao@x.com" convivem no Postgres, que compara string
    /// case-sensitive, e colidem no Firebase, que não compara.
    /// </summary>
    public static string Normalize(string email) =>
        email.Trim().ToLowerInvariant();

    /// <summary>
    /// True quando o domínio do endereço é reservado ou descartável. Assume email já
    /// sintaticamente válido — quem garante isso é o validator.
    /// </summary>
    public static bool HasBlockedDomain(string email)
    {
        var normalized = Normalize(email);
        var atIndex = normalized.LastIndexOf('@');

        if (atIndex < 0 || atIndex == normalized.Length - 1)
            return false;

        var domain = normalized[(atIndex + 1)..];

        if (AllowedDomains.Contains(domain))
            return false;

        // Casa o domínio inteiro ou qualquer subdomínio dele, para que "mail.example.com"
        // e o TLD reservado ".test" caiam junto com "example.com".
        return BlockedDomains.Any(blocked =>
            domain == blocked || domain.EndsWith($".{blocked}", StringComparison.Ordinal));
    }
}
