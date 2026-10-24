using Microsoft.AspNetCore.Diagnostics;

namespace ForeignExchangeRateAPI.Common;

/// <summary>
/// Logs unhandled exceptions with the request that caused them.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler {

    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) {
        _logger = logger;
    }

    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken) {
        _logger.LogError(exception, "Unhandled exception on {Method} {Path}.", httpContext.Request.Method, httpContext.Request.Path);

        return ValueTask.FromResult(false);
    }
}
