using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.MatchCard;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class MatchCardService : IMatchCardService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MatchCardService> _log;
    private readonly string? _apiKey;
    private readonly string _templateDir;

    // Meses em pt-BR
    private static readonly string[] Months =
    [
        "Janeiro","Fevereiro","Março","Abril","Maio","Junho",
        "Julho","Agosto","Setembro","Outubro","Novembro","Dezembro"
    ];

    public MatchCardService(
        IHttpClientFactory httpFactory,
        IConfiguration cfg,
        ILogger<MatchCardService> log)
    {
        _httpFactory  = httpFactory;
        _log          = log;
        _apiKey       = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                        ?? cfg["OpenAI:ApiKey"];
        _templateDir  = cfg["OpenAI:TemplateDir"]
                        ?? Path.Combine(AppContext.BaseDirectory, "Templates");
    }

    public async Task<Result<string>> GenerateCardAsync(GenerateMatchCardDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return Result<string>.Fail("Chave da API OpenAI não configurada.");

        var isResult = dto.Template == "match_result";
        var templateFile = isResult ? "match_result.png" : "match_preview.png";
        var templatePath = Path.Combine(_templateDir, templateFile);

        if (!File.Exists(templatePath))
            return Result<string>.Fail($"Template '{templateFile}' não encontrado em '{_templateDir}'.");

        // ── Construir o prompt ──
        var prompt = BuildPrompt(dto, isResult);
        _log.LogInformation("[MatchCard] Gerando card '{Template}' — prompt: {Prompt}", dto.Template, prompt);

        // ── Ler template como base64 ──
        var templateBytes = await File.ReadAllBytesAsync(templatePath, ct);
        var templateB64   = Convert.ToBase64String(templateBytes);

        // ── Chamar OpenAI API (Chat Completions com image input/output) ──
        try
        {
            var imageBase64 = await CallOpenAiImageAsync(prompt, templateB64, ct);

            if (string.IsNullOrWhiteSpace(imageBase64))
                return Result<string>.Fail("OpenAI retornou resposta vazia.");

            return Result<string>.Ok(imageBase64);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[MatchCard] Erro ao chamar OpenAI");
            return Result<string>.Fail($"Erro ao gerar imagem: {ex.Message}");
        }
    }

    public Result<string> GetPrompt(GenerateMatchCardDto dto)
        => Result<string>.Ok(BuildPrompt(dto, dto.Template == "match_result"));

    // ─── Build Prompt ────────────────────────────────────────────────────────

    private static string BuildPrompt(GenerateMatchCardDto dto, bool isResult)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are editing a soccer match card image. You MUST keep the EXACT same layout, style, fonts, stadium background, Bratnava logo, jersey design, 'icone' text on jerseys, and overall design from the template image.");
        sb.AppendLine("Only change: (1) text content, (2) jersey/shirt colors, (3) player names in the panels.");
        sb.AppendLine();

        if (isResult)
        {
            sb.AppendLine("=== CARD TYPE: MATCH RESULT ===");
            sb.AppendLine("Title text: FIM DE JOGO!");
            sb.AppendLine($"Score text: {dto.TeamAGoals ?? 0} x {dto.TeamBGoals ?? 0}");
            if (!string.IsNullOrWhiteSpace(dto.WinnerTeamName))
                sb.AppendLine($"Winner: {dto.WinnerTeamName}");
            if (!string.IsNullOrWhiteSpace(dto.MvpName))
                sb.AppendLine($"MVP: {dto.MvpName}");
        }
        else
        {
            sb.AppendLine("=== CARD TYPE: MATCH PREVIEW ===");
            if (!string.IsNullOrWhiteSpace(dto.PlayedAt) && DateTime.TryParse(dto.PlayedAt, out var d))
            {
                var month = d.Month >= 1 && d.Month <= 12 ? Months[d.Month - 1] : d.Month.ToString();
                sb.AppendLine($"Date text: Hoje - {d.Day} de {month}, {d.Year} - {d.Hour:D2}:{d.Minute:D2}");
            }
        }

        // ── Left team ──
        sb.AppendLine();
        sb.AppendLine("=== LEFT TEAM ===");
        sb.AppendLine($"Jersey color: {dto.TeamAColorHex} ({dto.TeamAName})");
        sb.AppendLine($"Panel header: TIME {dto.TeamAName.ToUpperInvariant()}");
        sb.AppendLine("Player list (write each name on its own row inside the panel, in this EXACT order):");

        // Goleiros primeiro, depois jogadores de linha
        var teamAOrdered = dto.TeamAPlayers
            .OrderByDescending(p => p.IsGoalkeeper)
            .ToList();
        for (int i = 0; i < teamAOrdered.Count; i++)
        {
            var p = teamAOrdered[i];
            var role = p.IsGoalkeeper ? "GOALKEEPER" : "PLAYER";
            var icon = p.IsGoalkeeper ? "goal net icon (🥅)" : "soccer ball icon (⚽)";
            sb.AppendLine($"  Row {i + 1}: {icon} {p.Name}  [{role}]");
        }

        // ── Right team ──
        sb.AppendLine();
        sb.AppendLine("=== RIGHT TEAM ===");
        sb.AppendLine($"Jersey color: {dto.TeamBColorHex} ({dto.TeamBName})");
        sb.AppendLine($"Panel header: TIME {dto.TeamBName.ToUpperInvariant()}");
        sb.AppendLine("Player list (write each name on its own row inside the panel, in this EXACT order):");

        var teamBOrdered = dto.TeamBPlayers
            .OrderByDescending(p => p.IsGoalkeeper)
            .ToList();
        for (int i = 0; i < teamBOrdered.Count; i++)
        {
            var p = teamBOrdered[i];
            var role = p.IsGoalkeeper ? "GOALKEEPER" : "PLAYER";
            var icon = p.IsGoalkeeper ? "goal net icon (🥅)" : "soccer ball icon (⚽)";
            sb.AppendLine($"  Row {i + 1}: {icon} {p.Name}  [{role}]");
        }

        // ── Rules ──
        sb.AppendLine();
        sb.AppendLine("=== CRITICAL RULES ===");
        sb.AppendLine("1. PLAYER ORDER: Goalkeepers MUST be the FIRST row in each team panel. Use 🥅 icon for goalkeepers, ⚽ icon for regular players.");
        sb.AppendLine("2. Each row must show: [icon] [Player Name] — the icon on the left, then the player's name as text NEXT to it on the same row. Do NOT put the icon on a separate row from the name.");
        sb.AppendLine("3. SPELLING: Write each player name EXACTLY as listed above. No changes, no typos, no abbreviations.");
        sb.AppendLine("4. JERSEY COLORS: Fill the left jersey with " + dto.TeamAColorHex + " and the right jersey with " + dto.TeamBColorHex + ". Keep the same shirt shape, 'icone' text, and Bratnava badge.");
        sb.AppendLine("5. PANEL COLORS: The left panel header background should match the left jersey color. The right panel header background should match the right jersey color.");
        sb.AppendLine("6. BACKGROUND: Keep the stadium background blurred exactly as in the template.");
        sb.AppendLine("7. LOGO: Keep the Bratnava logo at the top exactly as it is.");
        sb.AppendLine("8. EMPTY ROWS: If there are fewer players than rows in the template, leave the remaining rows empty (just the divider lines, no text).");
        sb.AppendLine("9. OUTPUT: Portrait image, 1080x1350 pixels, suitable for Instagram.");

        return sb.ToString();
    }

    // ─── OpenAI API Call ─────────────────────────────────────────────────────

    private async Task<string?> CallOpenAiImageAsync(string prompt, string templateBase64, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        // Usar a API de Chat Completions com gpt-4o que suporta input e output de imagem
        var requestBody = new
        {
            model = "gpt-4o",
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = $"data:image/png;base64,{templateBase64}",
                                detail = "high"
                            }
                        },
                        new
                        {
                            type = "text",
                            text = prompt
                        }
                    }
                }
            },
            // Solicitar output de imagem
            response_format = new { type = "image" },
            max_tokens = 4096
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _log.LogError("[MatchCard] OpenAI retornou {Status}: {Body}", response.StatusCode, responseBody);
            throw new Exception($"OpenAI API error {response.StatusCode}: {responseBody}");
        }

        // Parse da resposta — extrair base64 da imagem
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        // Tentar extrair de choices[0].message.content (pode ser array ou string)
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var message = choices[0].GetProperty("message");

            // Se content é array (multimodal output)
            if (message.TryGetProperty("content", out var contentEl))
            {
                if (contentEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in contentEl.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "image_url")
                        {
                            if (part.TryGetProperty("image_url", out var imgUrl) &&
                                imgUrl.TryGetProperty("url", out var urlEl))
                            {
                                var url = urlEl.GetString() ?? "";
                                // Se é data URI, extrair base64
                                if (url.StartsWith("data:image"))
                                {
                                    var commaIdx = url.IndexOf(',');
                                    return commaIdx >= 0 ? url[(commaIdx + 1)..] : url;
                                }
                                // Se é URL, baixar e converter
                                return await DownloadAsBase64(client, url, ct);
                            }
                        }
                    }
                }
                else if (contentEl.ValueKind == JsonValueKind.String)
                {
                    // Pode ser um URL direto
                    var contentStr = contentEl.GetString();
                    if (contentStr?.StartsWith("data:image") == true)
                    {
                        var commaIdx = contentStr.IndexOf(',');
                        return commaIdx >= 0 ? contentStr[(commaIdx + 1)..] : contentStr;
                    }
                }
            }
        }

        // Tentar formato da Images API (data[0].b64_json ou data[0].url)
        if (root.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
        {
            var first = data[0];
            if (first.TryGetProperty("b64_json", out var b64El))
                return b64El.GetString();
            if (first.TryGetProperty("url", out var urlEl2))
                return await DownloadAsBase64(client, urlEl2.GetString()!, ct);
        }

        _log.LogWarning("[MatchCard] Formato de resposta inesperado: {Body}", responseBody);
        throw new Exception("Formato de resposta da OpenAI não reconhecido.");
    }

    private static async Task<string> DownloadAsBase64(HttpClient client, string url, CancellationToken ct)
    {
        var bytes = await client.GetByteArrayAsync(url, ct);
        return Convert.ToBase64String(bytes);
    }
}
