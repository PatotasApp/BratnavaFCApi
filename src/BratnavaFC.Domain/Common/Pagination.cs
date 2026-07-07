namespace BratnavaFC.Domain.Common;

/// <summary>Normalização padrão dos parâmetros de paginação usados nas listagens.</summary>
public static class Pagination
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize     = 100;

    /// <summary>Garante page >= 1 e pageSize dentro de [1, 100] (default 20).</summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize)
        => (Math.Max(page, 1), Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize));
}
