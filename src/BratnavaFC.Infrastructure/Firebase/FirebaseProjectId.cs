using System.Text.Json;
using FirebaseAdmin;

namespace BratnavaFC.Infrastructure.Firebase;

/// <summary>
/// Resolve o ProjectId do Firebase sem exigir configuração nova: ele já vem no service
/// account JSON. O <c>FirebaseApp.GetProjectId()</c> do SDK faria isso, mas é internal.
/// </summary>
public static class FirebaseProjectId
{
    /// <summary>Lê o campo <c>project_id</c> do service account JSON.</summary>
    public static string? FromServiceAccountJson(string serviceAccountJson)
    {
        if (string.IsNullOrWhiteSpace(serviceAccountJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(serviceAccountJson);

            return document.RootElement.TryGetProperty("project_id", out var projectId)
                ? projectId.GetString()
                : null;
        }
        catch (JsonException)
        {
            // JSON malformado já vai estourar na criação da credencial, com mensagem melhor.
            return null;
        }
    }

    /// <summary>
    /// ProjectId do app já inicializado. Cai para as env vars padrão do Google Cloud quando
    /// o app subiu por Application Default Credentials, que não carregam o project_id.
    /// </summary>
    public static string? FromInitializedApp()
        => FirebaseApp.DefaultInstance?.Options?.ProjectId
           ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
           ?? Environment.GetEnvironmentVariable("GCLOUD_PROJECT");
}
