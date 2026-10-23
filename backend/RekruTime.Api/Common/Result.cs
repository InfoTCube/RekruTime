namespace RekruTime.Api.Common;

public enum ErrorType { Validation, NotFound, Conflict, Gone, BadRequest, BadGateway, Unauthorized, Forbidden, TooManyRequests }

// A business-rule failure. Services return it; endpoints turn it into HTTP (ResultHttp).
public sealed record Error(ErrorType Type, string Message, string? Field = null)
{
    public static Error Validation(string field, string message) => new(ErrorType.Validation, message, field);
    public static Error NotFound(string message = "Not found.") => new(ErrorType.NotFound, message);
    public static Error Conflict(string message) => new(ErrorType.Conflict, message);
    public static Error Gone(string message) => new(ErrorType.Gone, message);
    public static Error BadRequest(string message) => new(ErrorType.BadRequest, message);
    public static Error BadGateway(string message) => new(ErrorType.BadGateway, message);
    public static Error Unauthorized() => new(ErrorType.Unauthorized, "");
    public static Error Forbidden(string message) => new(ErrorType.Forbidden, message);
    public static Error TooManyRequests(string message) => new(ErrorType.TooManyRequests, message);
}

// Either a value or an Error. `return dto;` and `return Error.NotFound();` both convert implicitly.
public readonly struct Result<T>
{
    public T? Value { get; }
    public Error? Error { get; }

    Result(T value) => Value = value;
    Result(Error error) => Error = error;

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new(error);
}

// Success without a value (→ 204), or an Error.
public readonly struct Result
{
    public Error? Error { get; }

    Result(Error error) => Error = error;

    public static Result Ok => default;
    public static implicit operator Result(Error error) => new(error);
}
