namespace BratnavaFC.Domain.Enums;

/// <summary>
/// Situação da planilha na fila dos administradores da plataforma. A importação é feita
/// por fora (manual); o sistema só guarda o arquivo e acompanha em que pé está.
/// </summary>
public enum GroupSpreadsheetStatus
{
    Received = 0,
    InProgress = 1,
    Finished = 2,
}
