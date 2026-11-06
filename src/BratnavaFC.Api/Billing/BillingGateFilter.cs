using System.Collections.Concurrent;
using System.Reflection;
using BratnavaFC.Application.Billing;
using BratnavaFC.Domain.Common;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Api.Billing;

/// <summary>
/// Libera a ação mesmo com a patota vencida: pagar, sair, apagar a patota, mudar quem
/// administra (para alguém poder pagar), denunciar conteúdo.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowWhenGroupExpiredAttribute : Attribute;

/// <summary>
/// Somente leitura para patota vencida: com o interruptor ligado no painel, qualquer
/// escrita (POST/PUT/PATCH/DELETE) numa patota que passou do vencimento + tolerância é
/// recusada com 402 e o código <c>PATOTA_VENCIDA</c>. Leitura continua liberada, e o
/// GodMode nunca é barrado.
///
/// A patota da requisição vem, nesta ordem: do parâmetro de rota <c>groupId</c>; do
/// <c>playerId</c> da rota (ações do jogador); de uma propriedade <c>GroupId</c> do corpo.
/// Ação sem patota (conta do usuário, push, avisos das lojas) passa direto.
/// </summary>
public sealed class BillingGateFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> GroupIdProperties = new();

    private readonly IBillingService _billing;
    private readonly AppDbContext _db;

    public BillingGateFilter(IBillingService billing, AppDbContext db)
    {
        _billing = billing;
        _db = db;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (SafeMethods.Contains(context.HttpContext.Request.Method)
            || context.HttpContext.User.Identity?.IsAuthenticated != true
            || context.HttpContext.User.IsInRole("GodMode")
            || context.ActionDescriptor.EndpointMetadata.OfType<AllowWhenGroupExpiredAttribute>().Any())
        {
            await next();
            return;
        }

        var ct = context.HttpContext.RequestAborted;
        var (enforcement, _) = await _billing.GetGateSettingsAsync(ct);
        if (!enforcement)
        {
            await next();
            return;
        }

        var groupId = await ResolveGroupIdAsync(context, ct);
        if (groupId == null)
        {
            await next();
            return;
        }

        var snap = await _billing.GetSnapshotAsync(groupId.Value, ct);
        if (snap is { IsReadOnly: true })
        {
            context.Result = new ObjectResult(new PatotaVencidaResponse(
                false, null, null,
                "O plano desta patota venceu. Ela está em modo somente leitura até um administrador ou financeiro pagar.",
                [BillingCodes.PatotaVencida],
                BillingCodes.PatotaVencida,
                groupId.Value))
            {
                StatusCode = StatusCodes.Status402PaymentRequired,
            };
            return;
        }

        await next();
    }

    private async Task<Guid?> ResolveGroupIdAsync(ActionExecutingContext context, CancellationToken ct)
    {
        var route = context.RouteData.Values;
        if (route.TryGetValue("groupId", out var g) && Guid.TryParse(g?.ToString(), out var gid))
            return gid;

        if (route.TryGetValue("playerId", out var p) && Guid.TryParse(p?.ToString(), out var pid))
        {
            var fromPlayer = await _db.Players.AsNoTracking().Where(x => x.Id == pid).Select(x => (Guid?)x.GroupId).FirstOrDefaultAsync(ct);
            if (fromPlayer != null) return fromPlayer;
        }

        foreach (var arg in context.ActionArguments.Values)
        {
            if (arg == null || arg is string || arg.GetType().IsPrimitive) continue;
            var prop = GroupIdProperties.GetOrAdd(arg.GetType(), t =>
                t.GetProperty("GroupId", BindingFlags.Public | BindingFlags.Instance) is { } pi
                && (pi.PropertyType == typeof(Guid) || pi.PropertyType == typeof(Guid?))
                    ? pi
                    : null);
            if (prop?.GetValue(arg) is Guid value && value != Guid.Empty)
                return value;
        }

        return null;
    }

    /// <summary>Mesmo envelope do resto da API, mais o <c>code</c> para o app reconhecer sem ler texto.</summary>
    public sealed record PatotaVencidaResponse(
        bool Success, object? Data, string? Message, string? Error, List<string> Errors, string Code, Guid GroupId);
}
