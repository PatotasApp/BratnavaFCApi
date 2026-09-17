namespace BratnavaFC.Infrastructure.Cloudflare;

/// <summary>
/// Storage das imagens públicas do app — avatares de usuário e logos de grupo. Um serviço
/// só, com o tipo vindo por parâmetro, porque os dois compartilham bucket, política de
/// acesso e pipeline de normalização; só mudam o prefixo e o formato de saída.
///
/// Separado de <see cref="IReplayUrlService"/> de propósito: replay é vídeo em bucket
/// privado servido por URL assinada e revogável. Mesmo R2, políticas opostas.
/// </summary>
public interface IImageStorageService
{
    /// <summary>Falso quando o storage está desabilitado no ambiente.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Host público do bucket, já sem barra final. Existe separado de
    /// <see cref="BuildPublicUrl"/> porque há projeções LINQ traduzidas para SQL que precisam
    /// montar a URL: chamada de método não traduz, mas concatenação de string sim — o mesmo
    /// recurso que o código já usava para montar "/api/Groups/" + id + "/logo".
    /// </summary>
    string PublicBaseUrl { get; }

    /// <summary>Normaliza a imagem, sobe para o bucket e devolve a object key gravada.</summary>
    Task<string> UploadAsync(ImageKind kind, Guid ownerId, Stream image, CancellationToken ct);

    Task DeleteAsync(string objectKey, CancellationToken ct);

    /// <summary>Compõe a URL pública a partir da key. Nunca lança: é chamado dentro de
    /// projeções de lista, onde uma exceção derrubaria o endpoint inteiro.</summary>
    string BuildPublicUrl(string objectKey);
}
