namespace BratnavaFC.Infrastructure.Spreadsheets;

/// <summary>
/// Armazenamento em disco para rodar a API local sem R2. Em produção o disco da máquina
/// do Fly é efêmero — por isso fora de Development só é usado se configurado de propósito.
/// </summary>
public sealed class LocalDiskSpreadsheetStorage : ISpreadsheetStorage
{
    private readonly string _root;

    public LocalDiskSpreadsheetStorage(string rootPath)
    {
        _root = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_root);
    }

    public string Description => $"disk:{_root}";

    public async Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        var path = PathOf(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (content.CanSeek) content.Position = 0;
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct)
    {
        var path = PathOf(objectKey);
        if (!File.Exists(path))
            throw new FileNotFoundException("Arquivo não encontrado no disco.", objectKey);

        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        var path = PathOf(objectKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathOf(string objectKey)
    {
        if (!SpreadsheetObjectKeys.IsValid(objectKey))
            throw new ArgumentException("Chave de planilha inválida.", nameof(objectKey));

        var full = Path.GetFullPath(Path.Combine(_root, objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Chave de planilha fora da pasta de armazenamento.", nameof(objectKey));
        return full;
    }
}
