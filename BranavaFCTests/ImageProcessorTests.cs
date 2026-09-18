using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace BranavaFC.Tests;

public sealed class ImageProcessorTests
{
    private readonly ImageProcessor _sut = new();

    // ── Avatar: quadrado, recortado, JPEG ─────────────────────────────────────

    [Theory]
    [InlineData(2000, 1000)]  // paisagem
    [InlineData(1000, 2000)]  // retrato
    [InlineData(64, 64)]      // menor que o alvo: ainda assim normaliza para 512
    public async Task NormalizeAsync_Avatar_AlwaysProducesA512Square(int width, int height)
    {
        await using var source = CreatePng(width, height);

        using var result = await _sut.NormalizeAsync(ImageKind.Avatar, source, CancellationToken.None);

        result.Position = 0;
        using var image = await Image.LoadAsync(result);
        Assert.Equal(512, image.Width);
        Assert.Equal(512, image.Height);
    }

    [Fact]
    public async Task NormalizeAsync_Avatar_ReencodesAsJpeg()
    {
        await using var source = CreatePng(800, 800);

        using var result = await _sut.NormalizeAsync(ImageKind.Avatar, source, CancellationToken.None);

        result.Position = 0;
        Assert.IsType<JpegFormat>(await Image.DetectFormatAsync(result));
    }

    // ── Logo: encaixada, sem corte, PNG com alfa ──────────────────────────────

    /// <summary>
    /// Logo NÃO é recortada em quadrado. Recortar um wordmark no centro cortaria o nome do
    /// grupo pelas laterais.
    /// </summary>
    [Fact]
    public async Task NormalizeAsync_Logo_PreservesAspectRatioWithoutCropping()
    {
        await using var source = CreatePng(1000, 500);

        using var result = await _sut.NormalizeAsync(ImageKind.Logo, source, CancellationToken.None);

        result.Position = 0;
        using var image = await Image.LoadAsync(result);
        Assert.Equal(512, image.Width);
        Assert.Equal(256, image.Height);
    }

    [Fact]
    public async Task NormalizeAsync_Logo_ReencodesAsPng()
    {
        await using var source = CreatePng(800, 800);

        using var result = await _sut.NormalizeAsync(ImageKind.Logo, source, CancellationToken.None);

        result.Position = 0;
        Assert.IsType<PngFormat>(await Image.DetectFormatAsync(result));
    }

    /// <summary>
    /// A razão de logo sair em PNG e não JPEG. Logo com fundo transparente recodificada em
    /// JPEG ganharia um fundo preto sólido, e isso é irreversível.
    /// </summary>
    [Fact]
    public async Task NormalizeAsync_Logo_KeepsTransparency()
    {
        await using var source = CreateTransparentPng(400, 400);

        using var result = await _sut.NormalizeAsync(ImageKind.Logo, source, CancellationToken.None);

        result.Position = 0;
        using var image = await Image.LoadAsync<Rgba32>(result);
        Assert.Equal(0, image[0, 0].A);
    }

    // ── Comum ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A validação por magic bytes saiu dos services: quem decide se é imagem agora é o
    /// decoder. Um arquivo que não decodifica precisa virar erro de request, não 500.
    /// </summary>
    [Theory]
    [InlineData(ImageKind.Avatar)]
    [InlineData(ImageKind.Logo)]
    public async Task NormalizeAsync_RejectsContentThatIsNotAnImage(ImageKind kind)
    {
        await using var garbage = new MemoryStream("nem de longe uma imagem"u8.ToArray());

        await Assert.ThrowsAsync<InvalidImageException>(
            () => _sut.NormalizeAsync(kind, garbage, CancellationToken.None));
    }

    [Fact]
    public async Task NormalizeAsync_ShrinksAnOversizedPhoto()
    {
        await using var source = CreatePng(2000, 2000);
        var originalLength = source.Length;

        using var result = await _sut.NormalizeAsync(ImageKind.Avatar, source, CancellationToken.None);

        Assert.True(
            result.Length < originalLength,
            $"esperado menor que {originalLength} bytes, veio {result.Length}");
    }

    /// <summary>
    /// Imagem com ruído determinístico, não cor sólida: cor sólida comprime a quase nada em
    /// PNG e faria o teste de redução de tamanho comparar dois arquivos minúsculos, passando
    /// ou falhando por acidente do compressor.
    /// </summary>
    private static MemoryStream CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var rng = new Random(42);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgba32((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
            }
        });

        return Save(image);
    }

    private static MemoryStream CreateTransparentPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgba32(0, 0, 0, 0);
            }
        });

        return Save(image);
    }

    private static MemoryStream Save(Image<Rgba32> image)
    {
        var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }
}
