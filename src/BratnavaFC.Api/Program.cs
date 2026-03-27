using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

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
            .AllowAnyHeader();
    });
});


// =====================
// DATABASE
// =====================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));


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
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IPollService, PollService>();
builder.Services.AddScoped<IPushService, PushService>();

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
// HOLIDAY SERVICE (BrasilAPI)
// =====================
builder.Services.AddHttpClient("BrasilApi", c =>
{
    c.BaseAddress = new Uri("https://brasilapi.com.br/");
    c.DefaultRequestHeaders.Add("Accept", "application/json");
    c.DefaultRequestHeaders.Add("User-Agent", "BratnavaFC/1.0");
    c.Timeout = TimeSpan.FromSeconds(10);
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();