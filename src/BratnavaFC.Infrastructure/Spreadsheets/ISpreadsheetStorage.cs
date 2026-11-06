namespace BratnavaFC.Infrastructure.Spreadsheets;

/// <summary>
/// Onde ficam as planilhas que as patotas mandam. PRIVADO, ao contrário das imagens: o
/// arquivo tem nome, telefone e pagamento de gente da patota, então nunca tem URL pública
/// — só sai pela API, para os administradores da plataforma.
/// </summary>
public interface ISpreadsheetStorage
{
    /// <summary>Para log: "r2:bucket" ou "disk:caminho".</summary>
    string Description { get; }

    Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct);

    /// <summary>Abre o arquivo para leitura. Lança <see cref="FileNotFoundException"/> se não existir.</summary>
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct);

    /// <summary>Apaga; arquivo que já não existe não é erro.</summary>
    Task DeleteAsync(string objectKey, CancellationToken ct);
}

public static class SpreadsheetObjectKeys
{
    public const string Prefix = "group-spreadsheets";

    /// <summary>group-spreadsheets/{patota}/{guid}{.ext}: o nome original nunca entra na key.</summary>
    public static string New(Guid groupId, string extension)
        => $"{Prefix}/{groupId:N}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

    /// <summary>Só aceita keys no formato que este sistema gera (sem "..", sem barra invertida).</summary>
    public static bool IsValid(string? objectKey)
        => !string.IsNullOrWhiteSpace(objectKey)
           && objectKey.StartsWith(Prefix + "/", StringComparison.Ordinal)
           && !objectKey.Contains("..", StringComparison.Ordinal)
           && !objectKey.Contains('\\')
           && objectKey.Split('/').Length == 3;
}
