using Amazon.Lambda.AspNetCoreServer;
using Amazon.Lambda.Core;
using LayeredCraft.StructuredLogging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LayeredCraft.Lambda.AspNetCore.Hosting.Middleware;

/// <summary>
/// Middleware that links Lambda timeout with HTTP request cancellation for proper timeout handling.
/// </summary>
/// <remarks>
/// This middleware creates a cancellation token that is triggered when either:
/// <list type="number">
///   <item>
///     <description>The client disconnects or the server aborts the request (original <see cref="HttpContext.RequestAborted"/> token).</description>
///   </item>
///   <item>
///     <description>Lambda timeout approaches (calculated from <see cref="ILambdaContext.RemainingTime"/> with a safety buffer).</description>
///   </item>
/// </list>
/// The middleware replaces <see cref="HttpContext.RequestAborted"/> with the linked token during request processing,
/// enabling downstream code to respond to Lambda timeouts through standard <see cref="System.Threading.CancellationToken"/> patterns.
/// In local development environments where <see cref="ILambdaContext"/> is unavailable, the middleware
/// operates as a pass-through with only the original <see cref="HttpContext.RequestAborted"/> token active.
/// </remarks>
public sealed class LambdaTimeoutLinkMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LambdaTimeoutLinkMiddleware> _logger;
    private readonly TimeSpan _buffer;

    /// <summary>
    /// Non-standard status code used by some proxies (e.g., nginx) to indicate the client closed the connection.
    /// </summary>
    private const int ClientClosedRequest = 499;

    /// <summary>
    /// Initializes a new instance of the <see cref="LambdaTimeoutLinkMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    /// <param name="logger">The logger for recording timeout events.</param>
    /// <param name="safetyBuffer">
    /// The safety buffer to subtract from the Lambda timeout. Defaults to 250ms to allow graceful shutdown/flush.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="next"/> or <paramref name="logger"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="safetyBuffer"/> is negative.</exception>
    public LambdaTimeoutLinkMiddleware(
        RequestDelegate next,
        ILogger<LambdaTimeoutLinkMiddleware> logger,
        TimeSpan? safetyBuffer = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var buffer = safetyBuffer ?? TimeSpan.FromMilliseconds(250);
        if (buffer < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(safetyBuffer), "Safety buffer cannot be negative.");
        _buffer = buffer;
    }

    /// <summary>
    /// Processes the HTTP request with Lambda timeout–aware cancellation.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// On cancellation due to Lambda timeout or client disconnect, this middleware will stop the pipeline,
    /// set an appropriate status code (504 for timeout, 499 for client disconnect), and avoid writing a body.
    /// </remarks>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Store original token (client disconnects, server aborts, etc.)
        var original = context.RequestAborted;

        // ILambdaContext is provided by the AWS hosting shim; null in local dev/Kestrel.
        var lambdaContext = context.Items.TryGetValue(AbstractAspNetCoreFunction.LAMBDA_CONTEXT, out var ctxObj)
            ? ctxObj as ILambdaContext
            : null;

        // Create timeout CTS from RemainingTime (never fires locally)
        using var timeoutCts = lambdaContext is null
            ? new CancellationTokenSource(TimeSpan.FromDays(1)) // effectively "never" in local dev
            : new CancellationTokenSource(GetTimeLeft(lambdaContext.RemainingTime, _buffer));

        // Link both: client abort OR timeout -> cancel
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(original, timeoutCts.Token);

        // Replace RequestAborted with the linked token so downstream sees the combined semantics
        context.RequestAborted = linkedCts.Token;

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            // Determine the cancellation source for clearer telemetry
            var byTimeout = timeoutCts.IsCancellationRequested;

            _logger.Warning(
                "Request cancelled ({Reason}). Path: {Path}, RemainingTimeMs: {RemainingMs}",
                byTimeout ? "Lambda timeout" : "Client disconnect",
                context.Request.Path,
                lambdaContext?.RemainingTime.TotalMilliseconds);

            // Finish gracefully: set only a status code; DO NOT write a body
            if (context.Response.HasStarted) return;
            try
            {
                context.Response.Headers.Clear();
                context.Response.ContentLength = 0;
                context.Response.StatusCode = byTimeout
                    ? StatusCodes.Status504GatewayTimeout
                    : ClientClosedRequest; // 499
            }
            catch
            {
                // Best effort; swallow any response write issues on a canceled stream.
            }

            // Swallow to avoid "Unknown error responding to request: TaskCanceledException"
        }
        finally
        {
            // Restore original token (defensive)
            context.RequestAborted = original;
        }
    }

    /// <summary>
    /// Calculates the time remaining before Lambda timeout, accounting for the safety buffer.
    /// </summary>
    /// <param name="remaining">The remaining Lambda execution time from <see cref="ILambdaContext.RemainingTime"/>.</param>
    /// <param name="buffer">The safety buffer to subtract.</param>
    /// <returns>
    /// The effective timeout duration for the cancellation token, or <see cref="TimeSpan.Zero"/>
    /// if insufficient time remains.
    /// </returns>
    private static TimeSpan GetTimeLeft(TimeSpan remaining, TimeSpan buffer)
        => remaining > buffer ? remaining - buffer : TimeSpan.Zero;
}
