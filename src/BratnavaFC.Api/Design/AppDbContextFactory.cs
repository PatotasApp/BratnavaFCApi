using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BratnavaFC.Api.Design;

/// <summary>
/// Usado só pelas ferramentas do EF (migrations add, database update,
/// has-pending-model-changes). Sem ele, o dotnet-ef sobe o host completo do ASP.NET, e o
/// host agora exige credencial do Firebase para montar a autenticação — o que quebraria
/// tanto a geração local de migration quanto os três steps de EF no fly-deploy.yml, que
/// não têm FIREBASE_SERVICE_ACCOUNT_JSON.
///
/// A connection string aqui é irrelevante para gerar migration, e é sobrescrita pelo
/// --connection que o CI passa em database update.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackConnection =
        "Host=localhost;Port=5432;Database=bratnavafc;Username=bratnava;Password=bratnava;";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
            ?? FallbackConnection;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
