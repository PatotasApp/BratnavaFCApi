namespace BratnavaFC.Infrastructure.Cloudflare;

public interface IImageProcessor
{
    /// <summary>
    /// Decodifica a imagem recebida e devolve a versão normalizada para o tipo pedido.
    /// Lança <see cref="InvalidImageException"/> quando o conteúdo não é uma imagem que a
    /// biblioteca consegue decodificar.
    /// </summary>
    Task<MemoryStream> NormalizeAsync(ImageKind kind, Stream source, CancellationToken ct);
}

public sealed class InvalidImageException : Exception
{
    public InvalidImageException()
        : base("Envie uma imagem JPEG, PNG ou WebP válida.") { }
}
