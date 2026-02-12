using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

internal static class DbContextFactory
{
    public static AppDbContext Create(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .EnableSensitiveDataLogging()
            .Options;

        return new AppDbContext(options);
    }
}
