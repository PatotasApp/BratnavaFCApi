namespace BratnavaFC.Application.Services;

/// <summary>
/// O que a patota pode mandar. Quem abre o arquivo é um administrador da plataforma, na
/// máquina dele — por isso a extensão tem que bater com a assinatura do conteúdo, e o
/// Content-Type gravado sai da extensão, nunca do que o cliente declarou.
/// </summary>
public static class SpreadsheetFileRules
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    /// <summary>Folga para o envelope multipart em volta do arquivo.</summary>
    public const long MaxRequestBytes = MaxSizeBytes + 1024 * 1024;

    /// <summary>Envios aguardando análise por patota — trava contra enxurrada, não contra uso normal.</summary>
    public const int MaxPendingPerGroup = 20;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel",
        [".csv"] = "text/csv",
        [".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
        [".pdf"] = "application/pdf",
    };

    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    public static IReadOnlyCollection<string> AllowedExtensions => ContentTypes.Keys;

    public static string? ContentTypeOf(string extension) => ContentTypes.GetValueOrDefault(extension);

    /// <summary>Nome exibido e usado no download: sem pasta, sem caractere de controle.</summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? "").Replace('\\', '/').Split('/').Last();
        name = new string(name.Where(c => !char.IsControl(c) && c != '"').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(name) ? "planilha" : name;
    }

    /// <summary>
    /// Confere os primeiros bytes. xlsx e ods são ZIP; xls é o contêiner OLE antigo do
    /// Office; pdf começa com %PDF; csv é texto.
    /// </summary>
    public static bool ContentMatchesExtension(string extension, ReadOnlySpan<byte> head)
    {
        return extension.ToLowerInvariant() switch
        {
            ".xlsx" or ".ods" => head.StartsWith("PK\u0003\u0004"u8),
            ".xls" => head.StartsWith(OleSignature),
            ".pdf" => head.StartsWith("%PDF-"u8),
            // UTF-16 (com BOM) tem bytes nulos por natureza e é o que alguns Excel exportam;
            // fora isso, byte nulo indica binário renomeado para .csv.
            ".csv" => head.Length > 0
                      && (head.StartsWith(Utf16LeBom) || head.StartsWith(Utf16BeBom) || !head.Contains((byte)0)),
            _ => false,
        };
    }
}
