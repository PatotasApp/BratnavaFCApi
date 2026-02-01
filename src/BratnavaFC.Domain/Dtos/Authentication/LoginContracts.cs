namespace BratnavaFC.Domain.Dtos.Authentication;

public static class LoginContracts
{
    public sealed record RefreshTokenRequest(string RefreshToken);
    public sealed record Request(string Email, string Password);
    public sealed record Response(string Token, string RefreshToken);
}
