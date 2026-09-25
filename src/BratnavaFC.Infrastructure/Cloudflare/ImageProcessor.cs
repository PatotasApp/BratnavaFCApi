using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace BratnavaFC.Infrastructure.Cloudflare;

/// <summary>
/// Normaliza toda imagem pública para no máximo 512px. Antes desta migração o arquivo era
/// gravado cru no Postgres, com até 5 MB, e servido inteiro em toda lista — uma tela de 20
/// jogadores podia baixar dezenas de MB. Recodificar na entrada mata isso na origem.
/// </summary>
public sealed class ImageProcessor : IImageProcessor
{
    public const int Dimension = 512;
    public const int JpegQuality = 85;

    public async Task<MemoryStream> NormalizeAsync(ImageKind kind, Stream source, CancellationToken ct)
    {
        Image image;
        try
        {
            image = await Image.LoadAsync(source, ct);
        }
        catch (Exception ex) when (ex is ImageFormatException or NotSupportedException)
        {
            // Esta é a única validação de "isto é mesmo uma imagem?" que resta. A checagem por
            // magic bytes que existia aprovava um arquivo com cabeçalho certo e corpo
            // corrompido, que só quebraria na hora de exibir.
            throw new InvalidImageException();
        }

        using (image)
        {
            image.Mutate(ctx => ctx
                // Foto de celular costuma vir com a rotação só no EXIF. Sem isto o avatar
                // aparece deitado, porque a saída não carrega o EXIF de entrada.
                .AutoOrient()
                .Resize(ResizeOptionsFor(kind)));

            var output = new MemoryStream();

            if (kind == ImageKind.Logo)
                // PNG preserva o canal alfa. Logo costuma ter fundo transparente, e JPEG
                // achataria isso num fundo preto.
                await image.SaveAsPngAsync(output, new PngEncoder(), ct);
            else
                await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = JpegQuality }, ct);

            output.Position = 0;
            return output;
        }
    }

    /// <summary>
    /// Avatar é recortado em quadrado: rosto centralizado, e toda lista do app assume
    /// proporção 1:1. Logo é apenas encaixada dentro de 512 sem corte — recortar um wordmark
    /// no quadrado cortaria o nome do grupo pela metade.
    /// </summary>
    private static ResizeOptions ResizeOptionsFor(ImageKind kind) => kind switch
    {
        ImageKind.Avatar => new ResizeOptions
        {
            Size = new Size(Dimension, Dimension),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center,
        },
        ImageKind.Logo => new ResizeOptions
        {
            Size = new Size(Dimension, Dimension),
            Mode = ResizeMode.Max,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de imagem desconhecido."),
    };
}
