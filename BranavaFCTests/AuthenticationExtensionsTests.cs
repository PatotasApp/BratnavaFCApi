using BratnavaFC.Api.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BranavaFC.Tests;

/// <summary>
/// O projectId usado para validar os ID tokens vem do service account JSON e de mais lugar
/// nenhum. Antes havia uma segunda fonte, Firebase:ProjectId no appsettings, que vencia o
/// JSON: as duas podiam discordar em silêncio e o sintoma era todo request respondendo 401,
/// sem nada no log explicando por quê — o Admin SDK falando com um projeto e o validador
/// esperando token de outro.
///
/// Os testes passam pelas variáveis padrão do Google Cloud porque é o ramo de
/// FirebaseProjectId.FromInitializedApp() que não exige subir um FirebaseApp real (que
/// precisaria de uma chave RSA válida e deixaria estado estático global para o resto da
/// suíte). O ramo do service account JSON está coberto em FirebaseProjectIdTests.
/// </summary>
public class AuthenticationExtensionsTests
{
    private const string CloudProject = "GOOGLE_CLOUD_PROJECT";
    private const string GcloudProject = "GCLOUD_PROJECT";

    [Fact]
    public void AddJwtAuthentication_DerivesIssuerAndAudienceFromTheFirebaseProject()
    {
        using var _ = new ProjectEnvironment(cloudProject: "development-d04ef");
        var services = new ServiceCollection();

        services.AddJwtAuthentication();

        var options = Resolve(services);
        options.Authority.Should().Be("https://securetoken.google.com/development-d04ef");
        options.TokenValidationParameters.ValidAudience.Should().Be("development-d04ef");
    }

    /// <summary>
    /// Sem credencial, a aplicação não sobe — e a mensagem precisa dizer o que configurar.
    /// Antes dava para seguir em frente preenchendo só o appsettings, o que mascarava a
    /// ausência do Firebase Admin até alguém tentar excluir uma conta ou mandar um push.
    /// </summary>
    [Fact]
    public void AddJwtAuthentication_WhenNoFirebaseProjectIsResolvable_FailsWithActionableMessage()
    {
        using var _ = new ProjectEnvironment(cloudProject: null);
        var services = new ServiceCollection();

        var act = () => services.AddJwtAuthentication();

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*FIREBASE_SERVICE_ACCOUNT_JSON*")
           .WithMessage("*FIREBASE_SERVICE_ACCOUNT_JSON_PATH*");
    }

    private static JwtBearerOptions Resolve(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        return provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    /// <summary>Troca as variáveis do ambiente e devolve as originais ao fim do teste.</summary>
    private sealed class ProjectEnvironment : IDisposable
    {
        private readonly string? _cloudProject = Environment.GetEnvironmentVariable(CloudProject);
        private readonly string? _gcloudProject = Environment.GetEnvironmentVariable(GcloudProject);

        public ProjectEnvironment(string? cloudProject)
        {
            Environment.SetEnvironmentVariable(CloudProject, cloudProject);
            Environment.SetEnvironmentVariable(GcloudProject, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(CloudProject, _cloudProject);
            Environment.SetEnvironmentVariable(GcloudProject, _gcloudProject);
        }
    }
}
