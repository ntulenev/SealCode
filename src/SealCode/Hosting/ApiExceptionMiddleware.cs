namespace SealCode.Hosting;

/// <summary>
/// Applies the ApiExceptionMiddleware policy to a request.
/// </summary>
internal sealed class ApiExceptionMiddleware
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiExceptionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next request handler in the pipeline.</param>
    public ApiExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Processes the current request.
    /// </summary>
    /// <param name="context">The current request context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or BadHttpRequestException or Models.Exceptions.RoomLimitException)
        {
            var limit = ex is Models.Exceptions.RoomLimitException;
            await Results.Problem(statusCode: limit ? 409 : 400, title: limit ? "LimitExceeded" : "InvalidInput")
                .ExecuteAsync(context).ConfigureAwait(false);
        }
    }

    private readonly RequestDelegate _next;
}
