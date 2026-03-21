namespace BratnavaFC.Domain.Common;

public record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Message,
    string? Error,
    List<string> Errors
);
