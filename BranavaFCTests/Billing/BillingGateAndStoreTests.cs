using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BratnavaFC.Api.Billing;
using BratnavaFC.Application.Billing;
using BratnavaFC.Application.Billing.Apple;
using BratnavaFC.Application.Billing.Google;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BranavaFC.Tests.Billing;

public sealed class BillingGateFilterTests
{
    private static async Task<IActionResult?> Run(BillingTestKit kit, string method, Guid groupId, string role = "User",
        bool allowAttribute = false, object? body = null, bool useRoute = true)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], "test"));

        var route = new RouteData();
        if (useRoute) route.Values["groupId"] = groupId.ToString();

        var descriptor = new ControllerActionDescriptor
        {
            EndpointMetadata = allowAttribute ? [new AllowWhenGroupExpiredAttribute()] : [],
        };
        var actionContext = new ActionContext(http, route, descriptor);
        var args = new Dictionary<string, object?>();
        if (body != null) args["dto"] = body;
        var ctx = new ActionExecutingContext(actionContext, [], args, controller: new object());

        var called = false;
        await new BillingGateFilter(kit.Billing(), kit.Db).OnActionExecutionAsync(ctx, () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        });

        return called ? null : ctx.Result;
    }

    private sealed record BodyWithGroup(Guid GroupId, string Name);

    private static (BillingTestKit Kit, Guid GroupId) ExpiredGroup(string name, bool enforcement)
    {
        var kit = new BillingTestKit(name);
        kit.SetSettings(enforcement: enforcement);
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now.AddDays(-40));
        return (kit, g.Id);
    }

    [Fact]
    public async Task InterruptorDesligado_DeixaPassar()
    {
        var (kit, gid) = ExpiredGroup(nameof(InterruptorDesligado_DeixaPassar), enforcement: false);
        (await Run(kit, "POST", gid)).Should().BeNull();
    }

    [Fact]
    public async Task Vencida_EscritaRecebe402ComCodigo()
    {
        var (kit, gid) = ExpiredGroup(nameof(Vencida_EscritaRecebe402ComCodigo), enforcement: true);

        var result = await Run(kit, "POST", gid);

        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(402);
        obj.Value.Should().BeOfType<BillingGateFilter.PatotaVencidaResponse>()
            .Which.Code.Should().Be(BillingCodes.PatotaVencida);
    }

    [Fact]
    public async Task Vencida_LeituraContinua()
    {
        var (kit, gid) = ExpiredGroup(nameof(Vencida_LeituraContinua), enforcement: true);
        (await Run(kit, "GET", gid)).Should().BeNull();
    }

    [Fact]
    public async Task Vencida_GodModeEAcoesLiberadasPassam()
    {
        var (kit, gid) = ExpiredGroup(nameof(Vencida_GodModeEAcoesLiberadasPassam), enforcement: true);
        (await Run(kit, "DELETE", gid, role: "GodMode")).Should().BeNull();
        (await Run(kit, "POST", gid, allowAttribute: true)).Should().BeNull();
    }

    [Fact]
    public async Task Vencida_PatotaPeloCorpoTambemBloqueia()
    {
        var (kit, gid) = ExpiredGroup(nameof(Vencida_PatotaPeloCorpoTambemBloqueia), enforcement: true);
        (await Run(kit, "POST", gid, body: new BodyWithGroup(gid, "x"), useRoute: false)).Should().BeOfType<ObjectResult>();
    }

    [Fact]
    public async Task DentroDaTolerancia_Passa()
    {
        var kit = new BillingTestKit(nameof(DentroDaTolerancia_Passa));
        kit.SetSettings(enforcement: true);
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now.AddDays(-31));
        (await Run(kit, "POST", g.Id)).Should().BeNull();
    }
}

/// <summary>
/// Cadeia de teste no formato da Apple (raiz, intermediário com o OID WWDR, folha com o OID
/// de compra no app) para exercitar a conferência de verdade, sem a raiz real da Apple.
/// </summary>
internal sealed class FakeAppleChain : IDisposable
{
    public X509Certificate2 Root { get; }
    public X509Certificate2 Intermediate { get; }
    public X509Certificate2 Leaf { get; }
    private readonly ECDsa _leafKey;

    public FakeAppleChain(bool leafOid = true)
    {
        var now = DateTimeOffset.UtcNow;
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootReq = new CertificateRequest("CN=Fake Apple Root", rootKey, HashAlgorithmName.SHA256);
        rootReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        Root = rootReq.CreateSelfSigned(now.AddDays(-1), now.AddYears(5));

        using var intKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var intReq = new CertificateRequest("CN=Fake WWDR", intKey, HashAlgorithmName.SHA256);
        intReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        intReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        intReq.CertificateExtensions.Add(new X509Extension(AppleJwsVerifier.IntermediateOid, [5, 0], false));
        using var intPub = intReq.Create(Root, now.AddDays(-1), now.AddYears(4), [1, 2, 3, 4]);
        Intermediate = intPub.CopyWithPrivateKey(intKey);

        _leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafReq = new CertificateRequest("CN=Fake StoreKit", _leafKey, HashAlgorithmName.SHA256);
        leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        if (leafOid) leafReq.CertificateExtensions.Add(new X509Extension(AppleJwsVerifier.LeafOid, [5, 0], false));
        Leaf = leafReq.Create(Intermediate, now.AddDays(-1), now.AddYears(3), [5, 6, 7, 8]);
    }

    public string RootSha256 => Convert.ToHexString(SHA256.HashData(Root.RawData)).ToLowerInvariant();

    public string Sign(object payload)
    {
        var header = new { alg = "ES256", x5c = new[] { Leaf, Intermediate, Root }.Select(c => Convert.ToBase64String(c.RawData)).ToArray() };
        var h = AppleJwsVerifier.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var p = AppleJwsVerifier.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var sig = _leafKey.SignData(Encoding.ASCII.GetBytes($"{h}.{p}"), HashAlgorithmName.SHA256);
        return $"{h}.{p}.{AppleJwsVerifier.Base64UrlEncode(sig)}";
    }

    public void Dispose()
    {
        Root.Dispose();
        Intermediate.Dispose();
        Leaf.Dispose();
        _leafKey.Dispose();
    }
}

public sealed class AppleJwsVerifierTests
{
    [Fact]
    public void AssinaturaValida_DevolveOPayload()
    {
        using var chain = new FakeAppleChain();
        var jws = chain.Sign(new { transactionId = "1000" });

        var payload = new AppleJwsVerifier([chain.RootSha256]).Verify(jws);

        payload.GetProperty("transactionId").GetString().Should().Be("1000");
    }

    [Fact]
    public void RaizNaoFixada_Recusa()
    {
        using var chain = new FakeAppleChain();
        var act = () => new AppleJwsVerifier([AppleBillingOptionsDefaultRoot]).Verify(chain.Sign(new { a = 1 }));
        act.Should().Throw<StoreVerificationException>().WithMessage("*raiz da Apple*");
    }

    private static string AppleBillingOptionsDefaultRoot => new AppleBillingOptions().TrustedRootSha256[0];

    [Fact]
    public void PayloadAlterado_Recusa()
    {
        using var chain = new FakeAppleChain();
        var parts = chain.Sign(new { price = 1 }).Split('.');
        var forged = AppleJwsVerifier.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { price = 999 }));

        var act = () => new AppleJwsVerifier([chain.RootSha256]).Verify($"{parts[0]}.{forged}.{parts[2]}");
        act.Should().Throw<StoreVerificationException>().WithMessage("*Assinatura*");
    }

    [Fact]
    public void FolhaSemOidDaApple_Recusa()
    {
        using var chain = new FakeAppleChain(leafOid: false);
        var act = () => new AppleJwsVerifier([chain.RootSha256]).Verify(chain.Sign(new { a = 1 }));
        act.Should().Throw<StoreVerificationException>().WithMessage("*compra no app*");
    }

    [Fact]
    public void Transacao_ConverteCamposDaApple()
    {
        var group = Guid.NewGuid();
        var json = JsonSerializer.SerializeToElement(new
        {
            transactionId = "2000", originalTransactionId = "1000", productId = "patota_1m", bundleId = "br.com.patotasapp",
            purchaseDate = 1_762_000_000_000L, expiresDate = 1_764_600_000_000L, appAccountToken = group.ToString(),
            type = "Auto-Renewable Subscription", price = 29900, currency = "BRL", environment = "Sandbox",
        });
        var renewal = JsonSerializer.SerializeToElement(new { autoRenewStatus = 1 });

        var tx = AppleStoreVerifier.FromPayloads(json, renewal, null, "br.com.patotasapp");

        tx.StoreSubscriptionId.Should().Be("1000");
        tx.TransactionId.Should().Be("2000");
        tx.GroupHint.Should().Be(group);
        tx.PriceMicros.Should().Be(29_900_000);
        tx.AutoRenewEnabled.Should().BeTrue();
        tx.Environment.Should().Be("Sandbox");

        var other = () => AppleStoreVerifier.FromPayloads(json, renewal, null, "outro.app");
        other.Should().Throw<StoreVerificationException>();
    }
}

public sealed class GooglePlayVerifierTests
{
    [Fact]
    public void SubscriptionV2_ConverteCampos()
    {
        var group = Guid.NewGuid();
        var json = JsonSerializer.SerializeToElement(new
        {
            startTime = "2026-11-01T10:00:00Z",
            subscriptionState = "SUBSCRIPTION_STATE_CANCELED",
            latestOrderId = "GPA.1234",
            acknowledgementState = "ACKNOWLEDGEMENT_STATE_PENDING",
            externalAccountIdentifiers = new { obfuscatedExternalAccountId = group.ToString() },
            lineItems = new[]
            {
                new
                {
                    productId = "patota_1m", expiryTime = "2026-12-01T10:00:00Z", latestSuccessfulOrderId = "GPA.1234..0",
                    autoRenewingPlan = new { autoRenewEnabled = false, recurringPrice = new { currencyCode = "BRL", units = "29", nanos = 900_000_000 } },
                },
            },
        });

        var tx = GooglePlayVerifier.FromSubscriptionV2(json, "token-1", "patota_1m");

        tx.TransactionId.Should().Be("GPA.1234..0");
        tx.SubscriptionState.Should().Be(StoreSubscriptionState.Active); // cancelou só a renovação
        tx.AutoRenewEnabled.Should().BeFalse();
        tx.NeedsAcknowledge.Should().BeTrue();
        tx.GroupHint.Should().Be(group);
        tx.PriceMicros.Should().Be(29_900_000);
        tx.ExpiresAt.Should().Be(new DateTime(2026, 12, 1, 10, 0, 0, DateTimeKind.Utc));
    }
}

public sealed class BillingNotificationServiceTests
{
    private const string Bundle = "br.com.patotasapp";

    private static BillingNotificationService Create(BillingTestKit kit, FakeAppleChain chain) => new(
        kit.Db, kit.Billing(),
        Options.Create(new BillingOptions { Apple = new AppleBillingOptions { BundleId = Bundle } }),
        NullLogger<BillingNotificationService>.Instance,
        new AppleJwsVerifier([chain.RootSha256]));

    private static object Transaction(string txId, Guid group, DateTime purchased, DateTime expires, DateTime? revoked = null) => new
    {
        transactionId = txId, originalTransactionId = "orig-1", productId = "patota_1m", bundleId = Bundle,
        purchaseDate = new DateTimeOffset(purchased).ToUnixTimeMilliseconds(),
        expiresDate = new DateTimeOffset(expires).ToUnixTimeMilliseconds(),
        revocationDate = revoked is { } r ? new DateTimeOffset(r).ToUnixTimeMilliseconds() : (long?)null,
        appAccountToken = group.ToString(), type = "Auto-Renewable Subscription", price = 29900, currency = "BRL",
        environment = "Production",
    };

    private static string Notification(FakeAppleChain chain, string uuid, string type, object transaction) => chain.Sign(new
    {
        notificationType = type,
        notificationUUID = uuid,
        data = new { bundleId = Bundle, signedTransactionInfo = chain.Sign(transaction), signedRenewalInfo = chain.Sign(new { autoRenewStatus = 1 }) },
    });

    [Fact]
    public async Task Apple_RenovacaoEReembolso()
    {
        using var chain = new FakeAppleChain();
        var kit = new BillingTestKit(nameof(Apple_RenovacaoEReembolso));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);
        var svc = Create(kit, chain);
        var t0 = DateTime.SpecifyKind(kit.Now, DateTimeKind.Utc);

        (await svc.HandleAppleAsync(Notification(chain, "n1", "SUBSCRIBED", Transaction("t1", g.Id, t0, t0.AddMonths(1))), default))
            .Should().Be(StoreNotificationOutcome.Ok);
        (await svc.HandleAppleAsync(Notification(chain, "n2", "DID_RENEW", Transaction("t2", g.Id, t0.AddMonths(1), t0.AddMonths(2))), default))
            .Should().Be(StoreNotificationOutcome.Ok);
        // Reenvio do mesmo aviso: ignorado.
        await svc.HandleAppleAsync(Notification(chain, "n2", "DID_RENEW", Transaction("t2", g.Id, t0.AddMonths(1), t0.AddMonths(2))), default);

        kit.Db.BillingPurchases.Count().Should().Be(2);
        (await kit.Db.GroupBillings.AsNoTracking().SingleAsync()).PaidUntil.Should().Be(t0.AddMonths(2));

        await svc.HandleAppleAsync(Notification(chain, "n3", "REFUND", Transaction("t2", g.Id, t0.AddMonths(1), t0.AddMonths(2), revoked: t0.AddDays(40))), default);

        (await kit.Db.GroupBillings.AsNoTracking().SingleAsync()).PaidUntil.Should().Be(t0.AddMonths(1));
        kit.Db.StoreEvents.Count(e => e.Status == StoreEventStatus.Processed).Should().Be(3);
    }

    [Fact]
    public async Task Apple_AssinaturaInvalida_RegistraFalhaERecusa()
    {
        using var chain = new FakeAppleChain();
        using var other = new FakeAppleChain();
        var kit = new BillingTestKit(nameof(Apple_AssinaturaInvalida_RegistraFalhaERecusa));
        var svc = Create(kit, chain);

        var outcome = await svc.HandleAppleAsync(other.Sign(new { notificationType = "DID_RENEW", notificationUUID = "x" }), default);

        outcome.Should().Be(StoreNotificationOutcome.Rejected);
        kit.Db.StoreEvents.Single().Status.Should().Be(StoreEventStatus.Failed);
    }

    [Fact]
    public async Task Apple_SemPatota_FicaNoPainelDeProblemas()
    {
        using var chain = new FakeAppleChain();
        var kit = new BillingTestKit(nameof(Apple_SemPatota_FicaNoPainelDeProblemas));
        var svc = Create(kit, chain);

        var outcome = await svc.HandleAppleAsync(Notification(chain, "n1", "SUBSCRIBED", Transaction("t1", Guid.NewGuid(), kit.Now, kit.Now.AddMonths(1))), default);

        outcome.Should().Be(StoreNotificationOutcome.Ok);
        var problems = await kit.Admin().GetProblemsAsync(default);
        problems.FailedEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task Google_ReembolsoPeloAviso()
    {
        using var chain = new FakeAppleChain();
        var kit = new BillingTestKit(nameof(Google_ReembolsoPeloAviso));
        var user = kit.AddUser();
        var g = kit.AddGroup(user.Id, kit.Now);
        await kit.Billing().ApplyVerifiedAsync(new VerifiedStoreTransaction(BillingStore.Google, "token-1", "GPA.9", "patota_1m",
            kit.Now, kit.Now.AddMonths(1), StoreSubscriptionState.Active, true, false, null, null, g.Id, null, null, "Production", false),
            g.Id, user.Id, default);

        var data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
        {
            packageName = "br.com.patotasapp",
            voidedPurchaseNotification = new { purchaseToken = "token-1", orderId = "GPA.9", productType = 1, refundType = 1 },
        }));
        var body = JsonSerializer.Serialize(new { message = new { data, messageId = "m-1" }, subscription = "s" });

        (await Create(kit, chain).HandleGoogleAsync(body, default)).Should().Be(StoreNotificationOutcome.Ok);

        (await kit.Db.GroupBillings.AsNoTracking().SingleAsync()).PaidUntil.Should().BeNull();
        kit.Db.BillingPurchases.Single().State.Should().Be(BillingPurchaseState.Refunded);
    }
}
