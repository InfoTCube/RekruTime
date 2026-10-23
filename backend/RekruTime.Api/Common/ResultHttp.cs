using System.Security.Claims;

namespace RekruTime.Api.Common;

// The only place that knows how service results map to HTTP – every error is a ProblemDetails body.
public static class ResultHttp
{
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult>? ok = null) =>
        result.Error is { } e ? e.ToHttp() : ok?.Invoke(result.Value!) ?? Results.Ok(result.Value);

    public static IResult ToHttp(this Result result) => result.Error is { } e ? e.ToHttp() : Results.NoContent();

    public static IResult ToHttp(this Error e) => e.Type switch
    {
        ErrorType.Validation => Results.ValidationProblem(new Dictionary<string, string[]> { [e.Field ?? ""] = [e.Message] }),
        ErrorType.NotFound => Results.Problem(e.Message, statusCode: 404),
        ErrorType.Conflict => Results.Problem(e.Message, statusCode: 409),
        ErrorType.Gone => Results.Problem(e.Message, statusCode: 410),
        ErrorType.BadGateway => Results.Problem(e.Message, statusCode: 502),
        ErrorType.Unauthorized => Results.Unauthorized(),
        ErrorType.Forbidden => Results.Problem(e.Message, statusCode: 403),
        ErrorType.TooManyRequests => Results.Problem(e.Message, statusCode: 429),
        _ => Results.Problem(e.Message, statusCode: 400),
    };

    public static string Id(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
