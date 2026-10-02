using BratnavaFC.Infrastructure.Firebase;
using FluentAssertions;

namespace BranavaFC.Tests;

/// <summary>
/// Desde que Firebase:ProjectId saiu do appsettings, este parse é a única origem do
/// identificador que valida todos os ID tokens: errar aqui derruba a autenticação inteira.
/// Retornar null não é falha silenciosa — AddJwtAuthentication transforma null em exceção
/// de startup dizendo qual variável configurar.
/// </summary>
public class FirebaseProjectIdTests
{
    [Fact]
    public void FromServiceAccountJson_ReadsTheProjectId()
    {
        const string json = """
            { "type": "service_account", "project_id": "local-a5c59", "client_email": "x@y.z" }
            """;

        FirebaseProjectId.FromServiceAccountJson(json).Should().Be("local-a5c59");
    }

    /// <summary>
    /// Um JSON truncado por copiar/colar pela metade não pode derrubar o processo aqui: a
    /// criação da credencial estoura logo em seguida, com mensagem bem melhor que a do parser.
    /// </summary>
    [Fact]
    public void FromServiceAccountJson_WhenJsonIsMalformed_ReturnsNull()
    {
        FirebaseProjectId.FromServiceAccountJson("{ \"project_id\": ").Should().BeNull();
    }

    [Fact]
    public void FromServiceAccountJson_WhenProjectIdIsAbsent_ReturnsNull()
    {
        FirebaseProjectId.FromServiceAccountJson("""{ "type": "service_account" }""")
            .Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FromServiceAccountJson_WhenThereIsNoJson_ReturnsNull(string? json)
    {
        FirebaseProjectId.FromServiceAccountJson(json!).Should().BeNull();
    }
}
