namespace BratnavaFC.Infrastructure.Spreadsheets;

public sealed class SpreadsheetStorageOptions
{
    public const string SectionName = "SpreadsheetStorage";

    /// <summary>
    /// "r2" ou "disk". Vazio = R2 fora de Development; em Development, R2 só quando as
    /// credenciais existem, senão disco.
    /// </summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// Bucket PRIVADO do R2. O padrão é o goal-replays, que já é privado e já existe nos
    /// dois ambientes; as keys ficam sob "group-spreadsheets/", longe dos clipes.
    /// </summary>
    public string BucketName { get; set; } = "goal-replays";

    /// <summary>Pasta do armazenamento em disco. Vazio = App_Data/spreadsheets na raiz da API.</summary>
    public string LocalPath { get; set; } = "";
}
