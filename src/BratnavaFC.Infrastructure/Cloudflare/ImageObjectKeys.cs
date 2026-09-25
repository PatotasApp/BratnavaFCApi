namespace BratnavaFC.Infrastructure.Cloudflare;

/// <summary>
/// Composição de key e URL pública das imagens. Separado do serviço de storage porque é
/// lógica pura: dá para testá-la sem subir cliente S3 nenhum, e é onde mora o clássico bug
/// de barra duplicada.
/// </summary>
public static class ImageObjectKeys
{
    /// <summary>
    /// Monta a key de uma imagem nova: <c>{prefixo}/{ownerId}/{guid}{extensão}</c>.
    ///
    /// O GUID no nome não é enfeite: ele torna a key imutável, que é o que autoriza o objeto
    /// a ser servido com Cache-Control immutable, e dá 122 bits de entropia no caminho,
    /// tornando a URL não-enumerável mesmo em bucket público.
    /// </summary>
    public static string New(ImageKind kind, Guid ownerId)
        => $"{ImageKinds.PrefixOf(kind)}/{ownerId}/{Guid.NewGuid()}{ImageKinds.ExtensionOf(kind)}";

    public static string BuildPublicUrl(string publicBaseUrl, string objectKey)
        => $"{publicBaseUrl.TrimEnd('/')}/{objectKey.TrimStart('/')}";
}
