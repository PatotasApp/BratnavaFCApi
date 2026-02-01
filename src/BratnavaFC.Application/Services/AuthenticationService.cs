using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Authentication;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BratnavaFC.Application.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly AppDbContext appDbContext;
    private readonly ILogger<UserService> _logger;
    private readonly PasswordHasher<UserEntity> _passwordHasher;

    public AuthenticationService(AppDbContext appDbContext, ILogger<UserService> logger, PasswordHasher<UserEntity> passwordHasher)
    {
        this.appDbContext = appDbContext;
        _logger = logger;
        _passwordHasher = passwordHasher;
    }

    public async Task<LoginContracts.Response> LoginAsync(LoginContracts.Request request, CancellationToken cancellationToken)
    {
        try
        {
            var user = await appDbContext.Users.FirstOrDefaultAsync(x => x.Email == request.Email, cancellationToken);

            if (user is null)
            {
                throw new ApplicationException("User not found");
            }

            var passwordVerificationResult = _passwordHasher.VerifyHashedPassword(user, user.Password, request.Password);

            if (passwordVerificationResult is PasswordVerificationResult.Failed)
            {
                throw new ApplicationException("Invalid user or password.");
            }

            var refreshToken = new RefreshTokenEntity
            {
                Token = GenerateRefreshToken(),
                Expiration = DateTime.UtcNow.AddHours(7),
                UserId = user.Id
            };

            appDbContext.RefreshTokens.Add(refreshToken);
            await appDbContext.SaveChangesAsync(cancellationToken);

            return new LoginContracts.Response(CreateToken(user), refreshToken.Token);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to login/authenticate user.");
            throw;
        }
    }

    public async Task<LoginContracts.Response> RefreshTokenAsync(LoginContracts.RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var refreshToken = await appDbContext.RefreshTokens.Include(x => x.User).FirstOrDefaultAsync(x => x.Token == request.RefreshToken, cancellationToken);

            if (refreshToken is null)
            {
                throw new ApplicationException("User is logged out, try again.");
            }

            if (DateTime.UtcNow > refreshToken.Expiration)
            {
                throw new ApplicationException("Refresh token expired.");
            }

            var newToken = CreateToken(refreshToken.User);
            var newRefreshToken = GenerateRefreshToken();

            refreshToken.Token = newRefreshToken;
            refreshToken.Expiration = DateTime.UtcNow.AddDays(7);

            await appDbContext.SaveChangesAsync(cancellationToken);

            return new LoginContracts.Response(newToken, newRefreshToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error trying to refresh token/authenticate user.");
            throw;
        }
    }

    private static string CreateToken(UserEntity user)
    {
        string secretKey = "any_secret_for_now_but_certainly_will_be_a_huge_key_later_I_ensure_you"; //configuraton
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.FirstName),
                ]),
            Expires = DateTime.UtcNow.AddSeconds(300),
            SigningCredentials = credentials,
            Issuer = "TeamManagement",
            Audience = "account"
        };

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(tokenDescriptor);
    }

    private static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
