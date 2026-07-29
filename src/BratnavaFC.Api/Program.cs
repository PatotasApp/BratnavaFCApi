using System.Text;
using System.Security.Claims;
using System.Net.Sockets;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using BratnavaFC.Application.Services;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Diagnostics;
using BratnavaFC.Application.TeamGeneration;
using Microsoft.AspNetCore.Identity;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Common;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using BratnavaFC.Api;
using BratnavaFC.Api.Middleware;
using BratnavaFC.Api.Realtime;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Hangfire;
using Hangfire.PostgreSql;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var startupLogger = LoggerFactory.Create(l => l.AddConsole()).CreateLogger("Startup");

startupLogger.LogInformation(
    "[Startup] 01 - Host builder criado. Environment={Environment}",
    builder.Environment.EnvironmentName);

builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

startupLogger.LogInformation("[Startup] 02 - BackgroundServiceExceptionBehavior configurado como Ignore.");

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
// CORS LIBERADO TOTAL
// =====================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders(
                "Content-Range",
                "Accept-Ranges",
                "Content-Length",
                "Content-Type");
    });
});

// =====================
// RATE LIMITING
// =====================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";

        await context.HttpContext.Response.WriteAsync(
            "{\"success\":false,\"message\":\"Muitas requisições. Tente novamente em instantes.\",\"errors\":[]}",
            ct);
    };

    options.AddPolicy("PerUser", context =>
    {
        var userId = context.User?.FindFirstValue("sub");

        if (!string.IsNullOrEmpty(userId))
        {
            return RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"user:{userId}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromSeconds(60),
                    SegmentsPerWindow = 6,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: $"ip:{ip}",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromSeconds(60),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

// =====================
// DATABASE
// =====================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
startupLogger.LogInformation(
    "[Startup] 03 - Configuração de banco carregada. HasDefaultConnection={HasDefaultConnection}",
    !string.IsNullOrWhiteSpace(connectionString));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var hangfireEnabled = CanUseHangfireStorage(connectionString, startupLogger);

// =====================
// HANGFIRE
// =====================
if (hangfireEnabled)
{
    startupLogger.LogInformation("[Startup] 04 - Registrando Hangfire com PostgreSQL.");

    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(
            options => options.UseNpgsqlConnection(connectionString!),
            new PostgreSqlStorageOptions
            {
                SchemaName = "hangfire",
                QueuePollInterval = TimeSpan.FromHours(1)
            }));

    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = 2;
        options.Queues = ["default"];
        options.SchedulePollingInterval = TimeSpan.FromMinutes(5);
    });
}
else
{
    startupLogger.LogWarning("[Startup] 04 - Hangfire não registrado neste startup.");
}

// =====================
// DEPENDENCY INJECTION
// =====================
builder.Services.AddScoped(typeof(IRepositoryBase<>), typeof(RepositoryBase<>));
builder.Services.AddScoped<IMatchService, MatchService>();
builder.Services.AddScoped<IPlayerStatsService, PlayerStatsService>();
builder.Services.AddScoped<ITeamColorService, TeamColorService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPlayerMembershipService, PlayerMembershipService>();
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
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ITeamBuilderService, TeamBuilderService>();
builder.Services.AddScoped<IClipCleanupJob, ClipCleanupJob>();
if (hangfireEnabled)
    builder.Services.AddScoped<INotificationScheduler, NotificationScheduler>();
else
    builder.Services.AddScoped<INotificationScheduler, NoOpNotificationScheduler>();
builder.Services.AddScoped<IMatchReminderJob, MatchReminderJob>();
builder.Services.AddScoped<IPollReminderJob, PollReminderJob>();
builder.Services.AddScoped<ICalendarReminderJob, CalendarReminderJob>();
builder.Services.AddScoped<IBirthdayNotificationJob, BirthdayNotificationJob>();
builder.Services.AddScoped<IMatchNoQuorumReminderJob, MatchNoQuorumReminderJob>();
builder.Services.AddScoped<IMvpVotingReminderJob, MvpVotingReminderJob>();
builder.Services.AddScoped<IMatchAutoFinalizeJob, MatchAutoFinalizeJob>();
builder.Services.AddScoped<IMonthlyPaymentReminderJob, MonthlyPaymentReminderJob>();
builder.Services.AddScoped<IMatchSchedulerJob, MatchSchedulerJob>();
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();
builder.Services.AddSignalR();
startupLogger.LogInformation(
    "[Startup] 05 - Serviços de aplicação registrados. HangfireEnabled={HangfireEnabled}",
    hangfireEnabled);

// =====================
// FIREBASE ADMIN
// =====================
static string? DecodeB64(string? b64)
{
    if (string.IsNullOrWhiteSpace(b64))
        return null;

    try
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
    }
    catch
    {
        return null;
    }
}

static bool CanUseHangfireStorage(string? connectionString, ILogger logger)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        DependencyStatusMonitor.RecordWarning(
            "hangfire",
            "DefaultConnection nao configurada. Hangfire foi desabilitado neste startup.");
        logger.LogWarning("[Hangfire] DefaultConnection não configurada. Hangfire desabilitado neste startup.");
        return false;
    }

    try
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 5,
            CommandTimeout = 5
        };

        using var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Open();

        logger.LogInformation("[Hangfire] Storage PostgreSQL disponível.");
        return true;
    }
    catch (Exception ex) when (ex is NpgsqlException or TimeoutException or SocketException or InvalidOperationException)
    {
        DependencyStatusMonitor.RecordWarning(
            "hangfire",
            "Storage PostgreSQL do Hangfire indisponivel. API seguiu sem Hangfire neste startup.",
            ex);
        logger.LogWarning(ex, "[Hangfire] Storage PostgreSQL indisponível. API seguirá sem Hangfire neste startup.");
        return false;
    }
}

var firebaseJson =
    DecodeB64(Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_B64"))
    ?? Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_JSON")
    ?? builder.Configuration["Firebase:ServiceAccountJson"];

var firebasePath = builder.Configuration["Firebase:ServiceAccountPath"];
startupLogger.LogInformation(
    "[Startup] 06 - Configuração Firebase carregada. HasJson={HasJson} HasPath={HasPath}",
    !string.IsNullOrWhiteSpace(firebaseJson),
    !string.IsNullOrWhiteSpace(firebasePath));

try
{
    if (!string.IsNullOrWhiteSpace(firebaseJson))
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromJson(firebaseJson)
        });

        startupLogger.LogInformation("[Firebase] Inicializado via ServiceAccountJson.");
    }
    else if (!string.IsNullOrWhiteSpace(firebasePath) && File.Exists(firebasePath))
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(firebasePath)
        });

        startupLogger.LogInformation("[Firebase] Inicializado via ServiceAccountPath.");
    }
    else
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.GetApplicationDefault()
        });

        startupLogger.LogInformation("[Firebase] Inicializado via Application Default Credentials.");
    }
}
catch (Exception ex)
{
    DependencyStatusMonitor.RecordWarning(
        "firebase",
        "Firebase nao pode ser inicializado. Push notifications estarao indisponiveis.",
        ex);
    startupLogger.LogWarning(
        ex,
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
startupLogger.LogInformation(
    "[Startup] 07 - Configuração Redis carregada. Source={RedisSource}",
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REDIS_URL")) ? "REDIS_URL" : "ConfigurationOrDefault");

builder.Services.AddSingleton<IRedisConnectionProvider>(sp =>
    new RedisConnectionProvider(
        redisConnectionString,
        sp.GetRequiredService<ILogger<RedisConnectionProvider>>()));

builder.Services.AddScoped<IMatchEventPublisher>(sp =>
{
    var streamKey =
        Environment.GetEnvironmentVariable("REPLAY_EVENTS_STREAM")
        ?? builder.Configuration["ReplayEvents:StreamKey"]
        ?? "replay_events";

    return new RedisMatchEventPublisher(
        sp.GetRequiredService<IRedisConnectionProvider>(),
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ILogger<RedisMatchEventPublisher>>(),
        streamKey);
});
builder.Services.AddSingleton<IReplayUrlService, R2ReplayUrlService>();
builder.Services.AddHostedService<ReplayStreamConsumerService>();
startupLogger.LogInformation("[Startup] 08 - Serviços de replay/Redis registrados.");

// =====================
// HOLIDAY SERVICE
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
    c.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddMemoryCache(o => o.SizeLimit = 10_000);
builder.Services.AddSingleton<IHolidayService, HolidayService>();
startupLogger.LogInformation("[Startup] 09 - HttpClients, cache e HolidayService registrados.");

// =====================
// JWT
// =====================
var jwtSecret = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey nao configurado.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TeamManagement";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "account";
startupLogger.LogInformation(
    "[Startup] 10 - Configuração JWT carregada. Issuer={Issuer} Audience={Audience}",
    jwtIssuer,
    jwtAudience);

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)),
            RoleClaimType = "role",
            NameClaimType = "name"
        };

        options.Events = new JwtBearerEvents
        {
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

builder.Services.AddAuthorization();
startupLogger.LogInformation("[Startup] 11 - Autenticação e autorização registradas.");

// =====================
// BUILD APP
// =====================
startupLogger.LogInformation("[Startup] 12 - Iniciando builder.Build().");
var app = builder.Build();
startupLogger.LogInformation("[Startup] 13 - builder.Build() concluído.");

// =====================
// MIDDLEWARE PIPELINE
// =====================
startupLogger.LogInformation("[Startup] 14 - Configurando middleware pipeline.");
app.UseExceptionHandler(appError =>
{
    appError.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler");

        logger.LogError(
            ex,
            "Unhandled exception — {Method} {Path}",
            context.Request.Method,
            context.Request.Path);

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var isDev = app.Environment.IsDevelopment();

        var errorMessage = isDev && ex is not null
            ? $"[{ex.GetType().Name}] {ex.Message}"
            : "Erro interno no servidor.";

        var response = new ApiResponse<object>(
            false,
            null,
            null,
            errorMessage,
            []);

        await context.Response.WriteAsJsonAsync(response);
    });
});

app.UseSwagger();

app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BratnavaFC API v1");
});

// Não redireciona HTTP para HTTPS no ambiente local.
// Isso permite o celular acessar a API pelo IP da rede.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowAll");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<AuditMiddleware>();

// =====================
// HANGFIRE DASHBOARD + JOBS
// =====================
if (hangfireEnabled)
{
    startupLogger.LogInformation("[Startup] 15 - Registrando Hangfire Dashboard e jobs recorrentes.");

    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new BratnavaFC.Api.Auth.HangfireGodModeAuthFilter()]
    });

    var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();

    recurringJobs.AddOrUpdate<IClipCleanupJob>(
        recurringJobId: "clip-r2-cleanup",
        methodCall: job => job.ExecuteAsync(CancellationToken.None),
        cronExpression: "0 3 1,15 * *",
        options: new RecurringJobOptions
        {
            TimeZone = TimeZoneInfo.Utc
        });

    recurringJobs.AddOrUpdate<IMonthlyPaymentReminderJob>(
      recurringJobId: "monthly-payment-reminder",
      methodCall: job => job.ExecuteAsync(CancellationToken.None),
      cronExpression: "0 13 10,20 * *",
      options: new RecurringJobOptions
      {
          TimeZone = TimeZoneInfo.Utc
      });

    recurringJobs.RemoveIfExists("match-scheduler");
    recurringJobs.RemoveIfExists("birthday-daily");

    startupLogger.LogInformation("[Startup] 16 - Hangfire Dashboard e jobs recorrentes registrados.");
}
else
{
    startupLogger.LogWarning("[Startup] 15 - Hangfire Dashboard e jobs recorrentes não registrados neste startup.");
}

app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    environment = app.Environment.EnvironmentName
}));

app.MapControllers();
app.MapHub<RealtimeHub>("/hubs/realtime");

startupLogger.LogInformation("[Startup] 17 - Endpoints mapeados. Iniciando app.Run().");
app.Run();
