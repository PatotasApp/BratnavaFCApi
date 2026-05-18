using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Authentication;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BratnavaFC.Application.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly PasswordHasher<UserEntity> _passwordHasher;
    private readonly IConfiguration _configuration;

    public AuthenticationService(
        AppDbContext db,
        ILogger<AuthenticationService> logger,
        PasswordHasher<UserEntity> passwordHasher,
        IConfiguration configuration)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<Result<TokenDto>> LoginAsync(LoginDto request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim().ToLower();
        var user = await _db.Users.FirstOrDefaultAsync(x => x.UserName == username, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning("Login attempt failed: user '{Username}' not found.", username);
            return Result<TokenDto>.Fail("Usuário ou senha incorretos.");
        }

        var verify = _passwordHasher.VerifyHashedPassword(user, user.Password, request.Password);

        if (verify == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Login attempt failed: invalid password for user '{Username}'.", username);
            return Result<TokenDto>.Fail("Usuário ou senha incorretos.");
        }

        var refreshToken = new RefreshTokenEntity
        {
            Token = GenerateRefreshToken(),
            Expiration = DateTime.UtcNow.AddDays(7),
            UserId = user.Id
        };

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync(cancellationToken);

        var jwt = CreateToken(user, _configuration);

        return Result<TokenDto>.Ok(new TokenDto(jwt, refreshToken.Token));
    }

    public async Task<Result<TokenDto>> RefreshTokenAsync(RefreshTokenDto request, CancellationToken cancellationToken)
    {
        var refreshToken = await _db.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Token == request.RefreshToken, cancellationToken);

        if (refreshToken is null)
        {
            _logger.LogWarning("Refresh token attempt failed: token not found.");
            return Result<TokenDto>.Fail("User is logged out, try again.");
        }

        if (DateTime.UtcNow > refreshToken.Expiration)
        {
            _logger.LogWarning("Refresh token attempt failed: token expired.");
            return Result<TokenDto>.Fail("Refresh token expired.");
        }

        var user = refreshToken.User;

        var newJwt = CreateToken(user, _configuration);
        var newRefreshToken = GenerateRefreshToken();

        refreshToken.Token = newRefreshToken;
        refreshToken.Expiration = DateTime.UtcNow.AddDays(7);

        await _db.SaveChangesAsync(cancellationToken);

        return Result<TokenDto>.Ok(new TokenDto(newJwt, newRefreshToken));
    }

    private static string CreateToken(UserEntity user, IConfiguration configuration)
    {
        var secretKey = configuration["Jwt:SecretKey"];

        var issuer = configuration["Jwt:Issuer"] ?? "TeamManagement";
        var audience = configuration["Jwt:Audience"] ?? "account";
        var expiresSeconds = int.TryParse(configuration["Jwt:ExpiresSeconds"], out var s) ? s : 300;

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var role = user.Role.ToString();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName.Trim().ToLower()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.FirstName),
                new Claim("role", role)
            }),
            Expires = DateTime.UtcNow.AddSeconds(expiresSeconds),
            SigningCredentials = credentials,
            Issuer = issuer,
            Audience = audience,

        };

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(tokenDescriptor);
    }

    private static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
