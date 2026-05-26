namespace BratnavaFC.Domain.Dtos.Authentication;

public record ResetPasswordDto(string Email, string Code, string NewPassword);
