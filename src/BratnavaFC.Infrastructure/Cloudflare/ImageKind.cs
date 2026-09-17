namespace BratnavaFC.Infrastructure.Cloudflare;

/// <summary>
/// Tipos de imagem pública que o app guarda no R2. Avatar e logo dividem bucket porque
/// dividem política de acesso — os dois são públicos. Replay não entra aqui: é privado e
/// servido por URL assinada, e política de acesso é configuração de bucket, não de pasta.
/// </summary>
public enum ImageKind
{
    Avatar = 1,
    Logo = 2,
}

public static class ImageKinds
{
    /// <summary>
    /// Pasta raiz de cada tipo dentro do bucket. Fica no CÓDIGO e não em configuração: o
    /// valor é o mesmo em todo ambiente, então virar secret só somaria chance de erro de
    /// digitação. O que varia por ambiente — bucket e host público — esse sim é config.
    /// </summary>
    public static string PrefixOf(ImageKind kind) => kind switch
    {
        ImageKind.Avatar => "avatars",
        ImageKind.Logo => "logos",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de imagem desconhecido."),
    };

    /// <summary>
    /// Avatar vira JPEG; logo vira PNG. A diferença é transparência: logo costuma ser PNG
    /// com fundo transparente, e recodificar em JPEG achataria o alfa num fundo preto.
    /// </summary>
    public static string ExtensionOf(ImageKind kind) => kind switch
    {
        ImageKind.Avatar => ".jpg",
        ImageKind.Logo => ".png",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de imagem desconhecido."),
    };

    public static string ContentTypeOf(ImageKind kind) => kind switch
    {
        ImageKind.Avatar => "image/jpeg",
        ImageKind.Logo => "image/png",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de imagem desconhecido."),
    };
}
