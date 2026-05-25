using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using BratnavaFC.Application.Services;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using Microsoft.AspNetCore.Identity;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Common;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using BratnavaFC.Api;
using BratnavaFC.Api.Middleware;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Hangfire;
using Hangfire.PostgreSql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter()));

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BratnavaFC API",
        Version = "v1"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Informe: Bearer {seu_token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});


// =====================
// 🔥 CORS LIBERADO TOTAL
// =====================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Content-Range", "Accept-Ranges", "Content-Length", "Content-Type");
    });
});


// =====================
// RATE LIMITING
// Protege contra scraping automatizado de dados via API.
// Limite por usuário autenticado (sub do JWT); fallback por IP para anônimos.
// =====================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"success\":false,\"message\":\"Muitas requisições. Tente novamente em instantes.\",\"errors\":[]}", ct);
    };

    // Autenticados: 120 req / 60 s (janela deslizante, 6 segmentos de 10 s)
    // Equivale a ~2 req/s em média — suficiente para uso normal,
    // inviável para varredura automática.
    options.AddPolicy("PerUser", context =>
    {
        var userId = context.User?.FindFirstValue("sub");
        if (!string.IsNullOrEmpty(userId))
        {
            return RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"user:{userId}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit          = 120,
                    Window               = TimeSpan.FromSeconds(60),
                    SegmentsPerWindow    = 6,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,
                });
        }

        // Anônimos: limite mais restrito por IP
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: $"ip:{ip}",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit          = 30,
                Window               = TimeSpan.FromSeconds(60),
                SegmentsPerWindow    = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            });
    });
});


// =====================
// DATABASE
// =====================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));


// =====================
// HANGFIRE
// =====================
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        options => options.UseNpgsqlConnection(connectionString!),
        new PostgreSqlStorageOptions
        {
            SchemaName        = "hangfire",
            QueuePollInterval = TimeSpan.FromHours(1)
        }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount             = 2;
    options.Queues                  = ["default"];
    options.SchedulePollingInterval = TimeSpan.FromMinutes(5);
});


// =====================
// DEPENDENCY INJECTION
// =====================
builder.Services.AddScoped(typeof(IRepositoryBase<>), typeof(RepositoryBase<>));
builder.Services.AddScoped<IMatchService, MatchService>();
builder.Services.AddScoped<IPlayerStatsService, PlayerStatsService>();
builder.Services.AddScoped<ITeamColorService, TeamColorService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPlayerService, PlayerService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<TeamGenerationService>();
builder.Services.AddScoped<PasswordHasher<UserEntity>>();
builder.Services.AddScoped<IGroupSettingsService, GroupSettingsService>();
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<IFinancialTransactionService, FinancialTransactionService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IPollService, PollService>();
builder.Services.AddScoped<IPushService, PushService>();
builder.Services.AddScoped<IAbsenceService, AbsenceService>();
builder.Services.AddScoped<IBetService, BetService>();
builder.Services.AddScoped<IMatchCardService, MatchCardService>();
builder.Services.AddScoped<IClipCleanupJob, ClipCleanupJob>();
builder.Services.AddScoped<INotificationScheduler, NotificationScheduler>();
builder.Services.AddScoped<IMatchReminderJob, MatchReminderJob>();
builder.Services.AddScoped<IPollReminderJob, PollReminderJob>();
builder.Services.AddScoped<ICalendarReminderJob, CalendarReminderJob>();
builder.Services.AddScoped<IBirthdayNotificationJob, BirthdayNotificationJob>();

// =====================
// FIREBASE ADMIN
// =====================
// Prioridade 1: Base64 via FIREBASE_SERVICE_ACCOUNT_B64  (recomendado — sem problemas de quoting)
// Prioridade 2: JSON via FIREBASE_SERVICE_ACCOUNT_JSON ou Firebase:ServiceAccountJson
// Prioridade 3: Arquivo via Firebase:ServiceAccountPath
// Prioridade 4: Application Default Credentials (GCP/Cloud Run)
var startupLogger = LoggerFactory.Create(l => l.AddConsole()).CreateLogger("Startup");

static string? DecodeB64(string? b64)
{
    if (string.IsNullOrWhiteSpace(b64)) return null;
    try   { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
    catch { return null; }
}

var firebaseJson =
    DecodeB64(Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_B64"))
    ?? Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_JSON")
    ?? builder.Configuration["Firebase:ServiceAccountJson"];

var firebasePath = builder.Configuration["Firebase:ServiceAccountPath"];

try
{
    if (!string.IsNullOrWhiteSpace(firebaseJson))
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromJson(firebaseJson),
        });
        startupLogger.LogInformation("[Firebase] Inicializado via ServiceAccountJson.");
    }
    else if (!string.IsNullOrWhiteSpace(firebasePath) && File.Exists(firebasePath))
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(firebasePath),
        });
        startupLogger.LogInformation("[Firebase] Inicializado via ServiceAccountPath.");
    }
    else
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.GetApplicationDefault(),
        });
        startupLogger.LogInformation("[Firebase] Inicializado via Application Default Credentials.");
    }
}
catch (Exception ex)
{
    startupLogger.LogWarning(ex,
        "[Firebase] Não pôde ser inicializado. Push notifications estarão indisponíveis. " +
        "Verifique FIREBASE_SERVICE_ACCOUNT_B64, FIREBASE_SERVICE_ACCOUNT_JSON ou Firebase:ServiceAccountPath.");
}

// =====================
// REDIS
// =====================
var redisConnectionString =
    Environment.GetEnvironmentVariable("REDIS_URL")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? "localhost:6379";

var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
redisOptions.ConnectTimeout = 10_000;  // 10s para estabelecer conexão
redisOptions.SyncTimeout    = 40_000;  // 40s — comporta o BLOCK de 30s + margem de 10s

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisOptions));
builder.Services.AddScoped<IMatchEventPublisher, RedisMatchEventPublisher>();
builder.Services.AddSingleton<IReplayUrlService, R2ReplayUrlService>();
builder.Services.AddHostedService<ReplayStreamConsumerService>();


// =====================
// HOLIDAY SERVICE (BrasilAPI)
// =====================
builder.Services.AddHttpClient("BrasilApi", c =>
{
    c.BaseAddress = new Uri("https://brasilapi.com.br/");
    c.DefaultRequestHeaders.Add("Accept", "application/json");
    c.DefaultRequestHeaders.Add("User-Agent", "BratnavaFC/1.0");
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("OpenAI", c =>
{
    c.BaseAddress = new Uri("https://api.openai.com/");
    c.DefaultRequestHeaders.Add("Accept", "application/json");
    c.Timeout = TimeSpan.FromSeconds(120); // Image generation can be slow
});
builder.Services.AddMemoryCache(o => o.SizeLimit = 10_000);
builder.Services.AddSingleton<IHolidayService, HolidayService>();


// =====================
// JWT
// =====================
var jwtSecret = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey nao configurado.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TeamManagement";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "account";

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // 🔥 evita trocar "role" -> ClaimTypes.Role automaticamente
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),

            RoleClaimType = "role",
            NameClaimType = "name"
        };

        // Permite que <video src="...?t=TOKEN"> autentique sem header Authorization
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Path.Value?.Contains("/stream", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var token = context.Request.Query["t"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(token))
                        context.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();


// =====================
// BUILD APP
// =====================
var app = builder.Build();


// =====================
// MIDDLEWARE PIPELINE
// =====================
// 🔥 Exception handler must be FIRST to catch exceptions from all middleware
app.UseExceptionHandler(appError =>
{
    appError.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var response = new ApiResponse<object>(
            false, null, null, "Erro interno no servidor.", []);
        await context.Response.WriteAsJsonAsync(response);
    });
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BratnavaFC API v1");
});

app.UseHttpsRedirection();

// 🔥 CORS TEM QUE VIR ANTES DO AUTH
app.UseCors("AllowAll");

// Rate limiting (antes do auth para bloquear IPs suspeitos cedo)
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Audit trail — registra todas as chamadas autenticadas com userId + IP
// Deve vir APÓS UseAuthentication para que User.FindFirstValue("sub") funcione
app.UseMiddleware<AuditMiddleware>();

// =====================
// HANGFIRE DASHBOARD + JOBS
// =====================
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new BratnavaFC.Api.Auth.HangfireGodModeAuthFilter()]
});

// AddOrUpdate is called on every startup — idempotent.
// Creates the job on first deploy; updates cron/method on subsequent deploys if changed.
// IRecurringJobManager must be used instead of the static RecurringJob API in ASP.NET Core.
var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobs.AddOrUpdate<IClipCleanupJob>(
    recurringJobId: "clip-r2-cleanup",
    methodCall: job => job.ExecuteAsync(CancellationToken.None),
    cronExpression: "0 3 1,15 * *",
    options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

// Notificações de aniversário: todo dia às 08:00 horário de Brasília (UTC-3 = 11:00 UTC)
recurringJobs.AddOrUpdate<IBirthdayNotificationJob>(
    recurringJobId: "birthday-daily",
    methodCall: job => job.ExecuteAsync(CancellationToken.None),
    cronExpression: "0 11 * * *",
    options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

app.MapControllers();

app.Run();