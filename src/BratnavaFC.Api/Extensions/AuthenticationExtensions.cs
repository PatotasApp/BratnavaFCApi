using System.IdentityModel.Tokens.Jwt;
using BratnavaFC.Infrastructure.Firebase;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace BratnavaFC.Api.Extensions;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Valida os ID tokens emitidos pelo Firebase Auth. As chaves públicas RS256 da Google
    /// são baixadas e rotacionadas pelo próprio middleware a partir do discovery document
    /// em {Authority}/.well-known/openid-configuration — não há chave simétrica local.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        // Validar um ID token exige somente o identificador público do projeto — a credencial
        // administrativa serve a push e custom claims, não ao middleware JWT. Mesmo assim o
        // projectId sai do service account JSON e de mais lugar nenhum: ter uma segunda fonte
        // em appsettings permitia que as duas discordassem em silêncio, e o sintoma era todo
        // request respondendo 401 sem dizer por quê — o Admin SDK falando com um projeto e o
        // validador esperando token de outro.
        //
        // Exige AddInfrastructure() antes desta chamada, que é quem inicializa o FirebaseApp.
        var projectId = FirebaseProjectId.FromInitializedApp();

        if (string.IsNullOrWhiteSpace(projectId))
            throw new InvalidOperationException(
                "ProjectId do Firebase não resolvido: o Firebase Admin não inicializou. " +
                "Defina FIREBASE_SERVICE_ACCOUNT_JSON (conteúdo) ou " +
                "FIREBASE_SERVICE_ACCOUNT_JSON_PATH (caminho do arquivo).");

        var issuer = $"https://securetoken.google.com/{projectId}";

        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = issuer;

                // Precisa continuar false. Com true, o handler RENOMEIA a claim "sub" para
                // NameIdentifier — e o NameIdentifier pertence à identidade INTERNA, escrita
                // pelo FirebaseIdentityMiddleware. Deixar o UID do Firebase entrar ali faria
                // os controllers lerem a identidade externa achando que é a interna.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = projectId,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    // As custom claims do Firebase caem na raiz do payload, então a role
                    // injetada via SetCustomUserClaimsAsync alimenta o mesmo nome que os
                    // [Authorize(Roles = ...)] já usam.
                    RoleClaimType = "role",
                    NameClaimType = "name"
                };

                options.Events = new JwtBearerEvents
                {
                    // Elementos <video src> e o cliente SignalR não mandam header
                    // Authorization; ambos passam o token na query string.
                    OnMessageReceived = context =>
                    {
                        var path = context.Request.Path.Value ?? string.Empty;

                        if (path.Contains("/stream", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = context.Request.Query["t"].FirstOrDefault();

                            if (!string.IsNullOrEmpty(token))
                                context.Token = token;
                        }
                        else if (path.Contains("/hubs/realtime", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = context.Request.Query["access_token"].FirstOrDefault();

                            if (!string.IsNullOrEmpty(token))
                                context.Token = token;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
