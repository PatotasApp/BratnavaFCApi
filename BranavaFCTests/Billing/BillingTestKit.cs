using BratnavaFC.Application.Billing;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Entities.Billing;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BranavaFC.Tests.Billing;

/// <summary>Montagem comum dos testes de cobrança: banco em memória, relógio fixo, loja simulada.</summary>
internal sealed class BillingTestKit
{
    public static readonly DateTime LaunchedAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    public AppDbContext Db { get; }
    public DateTime Now { get; set; } = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);
    public List<IStoreVerifier> Verifiers { get; } = [];
    public MemoryCache Cache { get; } = new(new MemoryCacheOptions());

    public BillingTestKit(string dbName, bool simulated = true)
    {
        Db = DbContextFactory.Create($"billing_{dbName}_{Guid.NewGuid():N}");
        Db.BillingSettings.Add(BillingSettingsEntity.CreateDefault(LaunchedAt));
        foreach (var store in new[] { BillingStore.Apple, BillingStore.Google, BillingStore.Simulated })
        {
            Db.BillingProducts.Add(new BillingProductEntity(store, "patota_1m", 1, 2990, true));
            Db.BillingProducts.Add(new BillingProductEntity(store, "patota_3m", 3, 8490, true));
            Db.BillingProducts.Add(new BillingProductEntity(store, "patota_12m", 12, 32290, true));
        }
        Db.SaveChanges();

        Verifiers.Add(new SimulatedStoreVerifier(Db, Config(simulated), Env("Development"), () => Now));
    }

    public BillingService Billing() => new(Db, Verifiers, Cache, Options.Create(new BillingOptions()),
        NullLogger<BillingService>.Instance, () => Now);

    public BillingAdminService Admin() => new(Db, Billing(), () => Now);

    public static IConfiguration Config(bool simulated) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Billing:Simulated:Enabled"] = simulated ? "true" : "false" })
        .Build();

    public static IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    public UserEntity AddUser(string name = "Ana")
    {
        var u = new UserEntity($"{name.ToLowerInvariant()}_{Guid.NewGuid():N}"[..20], name, "Teste", $"{Guid.NewGuid():N}@t.com", "hash", null, null);
        Db.Users.Add(u);
        Db.SaveChanges();
        return u;
    }

    public GroupEntity AddGroup(Guid creatorId, DateTime createdAt, string name = "Resenha FC", Guid? id = null)
    {
        var g = new GroupEntity(name, null, creatorId);
        if (id is { } forced) typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(g, forced);
        Db.Groups.Add(g);
        Db.Entry(g).Property(nameof(BaseEntity.CreateDate)).CurrentValue = createdAt;
        g.SetAdmins([creatorId]);
        Db.SaveChanges();
        return g;
    }

    public void SetSettings(bool enforcement = false, bool reminders = false, int trialDays = 30, int graceDays = 3, string reminderDays = "7,3,1,0")
    {
        var s = Db.BillingSettings.Single();
        s.Update(trialDays, graceDays, enforcement, reminders, reminderDays, null);
        Db.SaveChanges();
        Cache.Clear();
    }
}
