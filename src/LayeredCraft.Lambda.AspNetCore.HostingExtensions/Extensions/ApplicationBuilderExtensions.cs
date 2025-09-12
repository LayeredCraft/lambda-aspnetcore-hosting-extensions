using LayeredCraft.Lambda.AspNetCore.Hosting.Middleware;
using Microsoft.AspNetCore.Builder;

namespace LayeredCraft.Lambda.AspNetCore.Hosting.Extensions;

/// <summary>
/// Extension methods for <see cref="IApplicationBuilder"/> to configure Lambda-specific middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds Lambda timeout-aware cancellation to the application pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="safetyBuffer">
    /// Optional safety buffer to subtract from Lambda timeout. Defaults to 250ms.
    /// This ensures graceful shutdown before Lambda forcibly terminates the function.
    /// </param>
    /// <returns>The application builder for method chaining.</returns>
    /// <remarks>
    /// This middleware links Lambda execution timeout with HTTP request cancellation,
    /// enabling downstream code to respond to approaching timeouts through standard
    /// CancellationToken patterns. In local development environments, operates as
    /// a pass-through with only client disconnect cancellation active.
    /// 
    /// Should be placed early in the middleware pipeline to ensure all downstream
    /// components receive the timeout-aware cancellation token.
    /// </remarks>
    public static IApplicationBuilder UseLambdaTimeoutLinkedCancellation(
        this IApplicationBuilder app, TimeSpan? safetyBuffer = null)
        => app.UseMiddleware<LambdaTimeoutLinkMiddleware>(safetyBuffer);
}