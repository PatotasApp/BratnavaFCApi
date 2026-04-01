using System.Security.Claims;

namespace BratnavaFC.Api.Middleware;

/// <summary>
/// Logs every authenticated API call with userId + IP + endpoint + user-agent.
/// Creates an audit trail: if a user bulk-scrapes the API, their activity is
/// recorded and traceable. Also injects an X-Trace response header so that
/// any exported/shared API response is tied back to the authenticated user.
/// </summary>
public class AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Skip static files, swagger, health checks
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/swagger") || path.StartsWith("/health"))
        {
            await next(context);
            return;
        }

        var userId  = context.User?.FindFirstValue("sub") ?? "anon";
        var ip      = context.Connection.RemoteIpAddress?.ToString() ?? "-";
        var method  = context.Request.Method;
        var ua      = context.Request.Headers.UserAgent.ToString();
        var uaShort = ua.Length > 100 ? ua[..100] : ua;

        // Inject trace header BEFORE response is committed
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey("X-Trace"))
                context.Response.Headers["X-Trace"] = userId[..Math.Min(userId.Length, 8)];
            return Task.CompletedTask;
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await next(context);
        sw.Stop();

        var status = context.Response.StatusCode;

        if (userId != "anon")
        {
            logger.LogInformation(
                "[AUDIT] {Method} {Path} → {Status} | user={UserId} ip={Ip} ms={Ms} ua={UA}",
                method, path, status, userId, ip, sw.ElapsedMilliseconds, uaShort);
        }
        else if (status >= 400)
        {
            // Log unauthenticated errors (brute-force, scanning)
            logger.LogWarning(
                "[AUDIT-ANON] {Method} {Path} → {Status} | ip={Ip} ua={UA}",
                method, path, status, ip, uaShort);
        }
    }
}
