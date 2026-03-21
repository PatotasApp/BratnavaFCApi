namespace BratnavaFC.Domain.Common;

public abstract class ResultBase
{
    public bool Success { get; protected init; }
    public string? Message { get; protected init; }
    public string? Error { get; protected init; }
    public List<string> Errors { get; protected init; } = [];
    public ResultStatus Status { get; protected init; } = ResultStatus.Ok;
}

public class Result<T> : ResultBase
{
    public T? Data { get; private init; }

    private Result() { }

    public static Result<T> Ok(T data, string? message = null,
        ResultStatus status = ResultStatus.Ok) => new()
    {
        Success = true, Data = data, Message = message, Status = status
    };

    public static Result<T> Fail(string error,
        ResultStatus status = ResultStatus.BadRequest,
        List<string>? errors = null) => new()
    {
        Success = false, Error = error, Status = status, Errors = errors ?? []
    };
}

public class Result : ResultBase
{
    private Result() { }

    public static Result Ok(string? message = null) => new()
    {
        Success = true, Message = message, Status = ResultStatus.Ok
    };

    public static Result Fail(string error,
        ResultStatus status = ResultStatus.BadRequest,
        List<string>? errors = null) => new()
    {
        Success = false, Error = error, Status = status, Errors = errors ?? []
    };
}

public enum ResultStatus
{
    Ok           = 200,
    Created      = 201,
    BadRequest   = 400,
    Unauthorized = 401,
    Forbidden    = 403,
    NotFound     = 404,
}
