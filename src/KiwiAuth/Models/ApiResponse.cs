namespace KiwiAuth.Models;

public class ApiResponse
{
    public bool Success { get; init; }
    public object? Data { get; init; }
    public ApiError? Error { get; init; }

    public static ApiResponse Ok(object? data = null) =>
        new() { Success = true, Data = data };

    public static ApiResponse Fail(string code, string message) =>
        new() { Success = false, Error = new ApiError { Code = code, Message = message } };
}

public class ApiError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
