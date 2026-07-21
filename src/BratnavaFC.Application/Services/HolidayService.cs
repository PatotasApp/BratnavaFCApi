using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Diagnostics;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Calendar;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Busca feriados nacionais do Brasil na BrasilAPI e armazena em cache de memoria.
/// Registrado como Singleton; injete apenas dependencias Singleton/Transient seguras.
/// </summary>
public sealed class HolidayService : IHolidayService
{
    private const string BaseUrl = "https://brasilapi.com.br/api/feriados/v1/";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HolidayService> _logger;

    public HolidayService(
        IHttpClientFactory httpFactory,
        IMemoryCache cache,
        ILogger<HolidayService> logger)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<HolidayDto>>> GetHolidaysAsync(int year, CancellationToken ct = default)
    {
        var cacheKey = $"holidays:{year}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<HolidayDto>? cached) && cached is not null)
            return Result<IReadOnlyList<HolidayDto>>.Ok(cached);

        try
        {
            var client = _httpFactory.CreateClient("BrasilApi");
            var raw = await client.GetFromJsonAsync<BrasilApiHoliday[]>($"{BaseUrl}{year}", ct);

            var list = raw is null
                ? Array.Empty<HolidayDto>()
                : (IReadOnlyList<HolidayDto>)raw
                    .Where(h => !string.IsNullOrWhiteSpace(h.Date) && !string.IsNullOrWhiteSpace(h.Name))
                    .Select(h => new HolidayDto(h.Date!, h.Name!))
                    .ToArray();

            var expiry = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration,
                Priority = CacheItemPriority.Low,
                Size = list.Count,
            };
            _cache.Set(cacheKey, list, expiry);

            return Result<IReadOnlyList<HolidayDto>>.Ok(list);
        }
        catch (Exception ex)
        {
            DependencyStatusMonitor.RecordWarning(
                "brasil-api",
                $"Falha ao buscar feriados da BrasilAPI para o ano {year}. O calendario sera exibido sem feriados.",
                ex);
            _logger.LogWarning(ex, "Falha ao buscar feriados da BrasilAPI para o ano {Year}. O calendario sera exibido sem feriados.", year);
            return Result<IReadOnlyList<HolidayDto>>.Ok(Array.Empty<HolidayDto>());
        }
    }

    private sealed class BrasilApiHoliday
    {
        [JsonPropertyName("date")]
        public string? Date { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }
    }
}
