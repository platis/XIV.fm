namespace XIV.fm.Server.Api.Http;

public sealed partial class ApiExceptionMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<ApiExceptionMiddleware> logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await this.next(context).ConfigureAwait(false);
        }
        catch (BadHttpRequestException exception)
        {
            LogMalformedRequest(this.logger, context.TraceIdentifier, exception.Message);
            await ApiErrorWriter.WriteAsync(
                context,
                exception.StatusCode,
                "invalid_request",
                "The request is invalid.",
                cancellationToken: context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            LogCanceledRequest(this.logger, context.TraceIdentifier);
        }
        catch (Exception exception)
        {
            LogUnhandledError(this.logger, exception, context.TraceIdentifier);
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "An unexpected server error occurred.",
                cancellationToken: context.RequestAborted).ConfigureAwait(false);
        }
    }

    [LoggerMessage(1, LogLevel.Warning, "Rejected malformed request {RequestId}: {Reason}")]
    private static partial void LogMalformedRequest(ILogger logger, string requestId, string reason);

    [LoggerMessage(2, LogLevel.Debug, "Request {RequestId} was canceled by the client.")]
    private static partial void LogCanceledRequest(ILogger logger, string requestId);

    [LoggerMessage(3, LogLevel.Error, "Unhandled API error for request {RequestId}.")]
    private static partial void LogUnhandledError(ILogger logger, Exception exception, string requestId);
}
