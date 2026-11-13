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
    /// <param name="logger">The translated request failure logger.</param>
    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
            if (context.Response.HasStarted)
            {
                throw;
            }
            var status = ex is Models.Exceptions.RoomLimitException ? 409 : 400;
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            _logger.LogInformation(
                "Translated {Method} {RoutePattern} to {StatusCode}: {ExceptionType}, parameter {ParamName}",
                context.Request.Method, route, status, ex.GetType().Name,
                (ex as ArgumentException)?.ParamName);
            var limit = ex is Models.Exceptions.RoomLimitException;
            await Results.Problem(statusCode: limit ? 409 : 400, title: limit ? "LimitExceeded" : "InvalidInput")
                .ExecuteAsync(context).ConfigureAwait(false);
        }
    }

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;
}
