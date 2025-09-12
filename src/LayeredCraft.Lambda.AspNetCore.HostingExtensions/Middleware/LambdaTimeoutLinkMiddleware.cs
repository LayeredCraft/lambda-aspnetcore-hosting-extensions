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

    var original = context.RequestAborted;

    var lambdaContext = context.Items.TryGetValue(AbstractAspNetCoreFunction.LAMBDA_CONTEXT, out var ctxObj)
        ? ctxObj as ILambdaContext
        : null;

    var timeoutDuration = lambdaContext is null
        ? TimeSpan.FromDays(1)
        : GetTimeLeft(lambdaContext.RemainingTime, _buffer);

    // Short-circuit: already out of time
    if (timeoutDuration <= TimeSpan.Zero)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            context.Response.ContentLength = 0;
        }
        return;
    }

    using var timeoutCts = new CancellationTokenSource(timeoutDuration);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(original, timeoutCts.Token);

    var cancelledByTimeout = false;
    var cancelledByClient  = false;
    DateTimeOffset? timeoutAt = null, clientCancelAt = null;

    using var _regClient  = original.Register(() => { cancelledByClient  = true; clientCancelAt = DateTimeOffset.UtcNow; });
    using var _regTimeout = timeoutCts.Token.Register(() => { cancelledByTimeout = true; timeoutAt   = DateTimeOffset.UtcNow; });

    // Expose the linked token to downstream
    context.RequestAborted = linkedCts.Token;

    try
    {
        await _next(context).ConfigureAwait(false);

        // If something cancelled but downstream didn't throw, finalize here
        if (linkedCts.IsCancellationRequested && !context.Response.HasStarted)
        {
            var byTimeout =
                cancelledByTimeout && !cancelledByClient
                || (cancelledByTimeout && cancelledByClient && timeoutAt <= clientCancelAt);

            context.Response.StatusCode = byTimeout
                ? StatusCodes.Status504GatewayTimeout
                : ClientClosedRequest; // consider 504 here too
            context.Response.ContentLength = 0;
        }
    }
    catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
    {
        var byTimeout =
            cancelledByTimeout && !cancelledByClient
            || (cancelledByTimeout && cancelledByClient && timeoutAt <= clientCancelAt);

        _logger.LogWarning(
            "Request cancelled ({Reason}). Path: {Path}, RemainingTimeMs: {RemainingMs}, TimeoutDurationMs: {TimeoutMs}",
            byTimeout ? "Lambda timeout" : "Client disconnect",
            context.Request.Path,
            lambdaContext?.RemainingTime.TotalMilliseconds,
            timeoutDuration.TotalMilliseconds);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = byTimeout
                ? StatusCodes.Status504GatewayTimeout
                : ClientClosedRequest; // or 504
            context.Response.ContentLength = 0;
        }
        // swallow; cancelled pipeline
    }
    finally
    {
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
