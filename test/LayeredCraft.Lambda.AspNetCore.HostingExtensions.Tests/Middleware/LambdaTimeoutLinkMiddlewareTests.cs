using System.Net;
using Amazon.Lambda.Core;
using AutoFixture.Xunit3;
using LayeredCraft.Lambda.AspNetCore.Hosting.Middleware;
using LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.TestKit.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.Middleware;

public class LambdaTimeoutLinkMiddlewareTests
{
    [Theory]
    [MiddlewareAutoData]
    public void Constructor_WithValidParameters_ShouldCreateInstance(RequestDelegate next,
        ILogger<LambdaTimeoutLinkMiddleware> logger, TimeSpan timeout)
    {
        // Act
        var middleware = new LambdaTimeoutLinkMiddleware(next, logger, timeout);

        // Assert
        middleware.Should().NotBeNull("Middleware should be created with valid parameters");
    }

    /// <summary>
    /// Verifies that constructor throws when next delegate is null.
    /// </summary>
    [Theory]
    [MiddlewareAutoData]
    public void Constructor_WithNullNext_ShouldThrowArgumentNullException(
        ILogger<LambdaTimeoutLinkMiddleware> logger)
    {
        // Act
        var action = () => new LambdaTimeoutLinkMiddleware(null!, logger);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("next", "Constructor should validate next parameter");
    }

    /// <summary>
    /// Verifies that constructor throws when logger is null.
    /// </summary>
    [Theory]
    [MiddlewareAutoData]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException(
        RequestDelegate next)
    {
        // Act
        var action = () => new LambdaTimeoutLinkMiddleware(next, null!);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger", "Constructor should validate logger parameter");
    }

    /// <summary>
    /// Verifies that constructor throws when safety buffer is negative.
    /// </summary>
    [Theory]
    [MiddlewareAutoData]
    public void Constructor_WithNegativeSafetyBuffer_ShouldThrowArgumentOutOfRangeException(
        RequestDelegate next, ILogger<LambdaTimeoutLinkMiddleware> logger)
    {
        // Arrange
        var negativeSafetyBuffer = TimeSpan.FromMilliseconds(-100);

        // Act
        var action = () => new LambdaTimeoutLinkMiddleware(next, logger, negativeSafetyBuffer);

        // Assert
        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("safetyBuffer")
            .WithMessage("Safety buffer cannot be negative.*");
    }

    /// <summary>
    /// Verifies that when no Lambda context is present (local development),
    /// the middleware operates in pass-through mode with linked cancellation tokens.
    /// </summary>
    [Theory]
    [MiddlewareAutoData]
    public async Task InvokeAsync_WithNoLambdaContext_ShouldCallNextMiddlewareWithLinkedCancellationToken(
        [Frozen] HttpContext nullLambdaContext, [Frozen] RequestDelegate capturingNext,
        ILogger<LambdaTimeoutLinkMiddleware> logger)
    {
        // Arrange
        var originalToken = nullLambdaContext.RequestAborted;
        var sut = new LambdaTimeoutLinkMiddleware(capturingNext, logger);

        // Act
        await sut.InvokeAsync(nullLambdaContext);

        // Assert
        var nextCalled = nullLambdaContext.Items.ContainsKey("NextCalled") &&
                         (bool)nullLambdaContext.Items["NextCalled"]!;
        var tokenDuringExecution = nullLambdaContext.Items["CapturedToken"] as CancellationToken? ?? CancellationToken.None;

        nextCalled.Should().BeTrue("Next middleware should have been called");
        tokenDuringExecution.Should().NotBe(originalToken,
            "Middleware should replace RequestAborted with linked token during execution");
        nullLambdaContext.RequestAborted.Should().Be(originalToken,
            "Original RequestAborted token should be restored after execution");
        nullLambdaContext.Items.Should().NotContainKey("LambdaContext",
            "No Lambda context should be present in local development scenario");
    }

    /// <summary>
    /// Verifies that InvokeAsync throws ArgumentNullException when context is null.
    /// </summary>
    [Theory]
    [MiddlewareAutoData]
    public async Task InvokeAsync_WithNullContext_ShouldThrowArgumentNullException(
        RequestDelegate next, ILogger<LambdaTimeoutLinkMiddleware> logger)
    {
        // Arrange
        var sut = new LambdaTimeoutLinkMiddleware(next, logger);

        // Act
        var action = async () => await sut.InvokeAsync(null!);

        // Assert
        await action.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("context", "InvokeAsync should validate context parameter");
    }

    /// <summary>
    /// Verifies that when Lambda context is present, the middleware links both 
    /// timeout and request cancellation tokens properly.
    /// </summary>
    [Theory]
    [MiddlewareAutoData]
    public async Task InvokeAsync_WithLambdaContext_ShouldLinkTimeoutAndRequestTokens(
        [Frozen] HttpContext withLambdaContext, [Frozen] RequestDelegate capturingNext,
        [Frozen] ILambdaContext lambdaContext,
        ILogger<LambdaTimeoutLinkMiddleware> logger)
    {
        // Arrange
        lambdaContext.RemainingTime.Returns(TimeSpan.FromMinutes(5)); // Ensure sufficient time for normal execution
        var originalToken = withLambdaContext.RequestAborted;
        var sut = new LambdaTimeoutLinkMiddleware(next: capturingNext, logger);

        // Act
        await sut.InvokeAsync(withLambdaContext);

        // Assert
        var nextCalled = withLambdaContext.Items.ContainsKey("NextCalled") &&
                         (bool)withLambdaContext.Items["NextCalled"]!;
        var tokenDuringExecution = withLambdaContext.Items["CapturedToken"] as CancellationToken? ?? CancellationToken.None;

        nextCalled.Should().BeTrue("Next middleware should have been called");
        tokenDuringExecution.Should().NotBe(originalToken,
            "Middleware should replace RequestAborted with linked token during execution");
        withLambdaContext.RequestAborted.Should().Be(originalToken,
            "Original RequestAborted token should be restored after execution");
        withLambdaContext.Items.Should().ContainKey("LambdaContext",
            "Lambda context should be present in Lambda environment scenario");
    }

    /// <summary>
    /// Verifies that when Lambda timeout triggers cancellation during execution,
    /// the middleware handles it gracefully with proper status code and logging.
    /// </summary>
    [Theory]
    [MiddlewareAutoData]
    public async Task InvokeAsync_WithLambdaTimeoutCancellation_ShouldSetGatewayTimeoutStatus(
        [Frozen] HttpContext withLambdaContext, [Frozen] RequestDelegate timeoutDelegate,
        [Frozen] ILambdaContext lambdaContext,
        ILogger<LambdaTimeoutLinkMiddleware> logger)
    {
        // Arrange
        lambdaContext.RemainingTime.Returns(TimeSpan.FromMilliseconds(50)); // Very short timeout
        var sut = new LambdaTimeoutLinkMiddleware(timeoutDelegate, logger, TimeSpan.FromMilliseconds(10));

        // Act
        await sut.InvokeAsync(withLambdaContext);

        // Assert
        withLambdaContext.Response.StatusCode.Should().Be(StatusCodes.Status504GatewayTimeout,
            "Lambda timeout should result in 504 Gateway Timeout status code");
        withLambdaContext.Response.ContentLength.Should().Be(0,
            "Response body should be empty on timeout");
    }

    /// <summary>
    /// Verifies that when client disconnect triggers cancellation during execution,
    /// the middleware handles it gracefully with proper status code.
    /// </summary>
    [Theory]
    [MiddlewareAutoData]
    public async Task InvokeAsync_WithClientDisconnectCancellation_ShouldSetClientClosedStatus(
        [Frozen] HttpContext preCancelledContext, [Frozen] RequestDelegate disconnectDelegate,
        [Frozen] ILambdaContext lambdaContext,
        ILogger<LambdaTimeoutLinkMiddleware> logger)
    {
        // Arrange  
        lambdaContext.RemainingTime.Returns(TimeSpan.FromMinutes(5)); // Long timeout to avoid timeout trigger
        preCancelledContext.RequestAborted.IsCancellationRequested.Should().BeTrue("Context should have pre-cancelled token");
        var sut = new LambdaTimeoutLinkMiddleware(disconnectDelegate, logger, TimeSpan.FromMinutes(1)); // Large buffer

        // Act
        await sut.InvokeAsync(preCancelledContext);

        // Assert
        preCancelledContext.Response.StatusCode.Should().Be(499,
            "Client disconnect should result in 499 Client Closed Request status code");
        preCancelledContext.Response.ContentLength.Should().Be(0,
            "Response body should be empty on client disconnect");
    }

    /// <summary>
    /// Verifies that when response has already started, the middleware doesn't try to modify it.
    /// </summary>
    [Theory]
    [MiddlewareAutoData]
    public async Task InvokeAsync_WithResponseAlreadyStarted_ShouldNotModifyResponse(
        [Frozen] HttpContext responseStarted, [Frozen] RequestDelegate timeoutDelegate,
        [Frozen] ILambdaContext lambdaContext,
        ILogger<LambdaTimeoutLinkMiddleware> logger)
    {
        // Arrange
        lambdaContext.RemainingTime.Returns(TimeSpan.FromMilliseconds(50)); // Short timeout
        responseStarted.Response.HasStarted.Should().BeTrue("Response should be already started");
        var originalStatusCode = responseStarted.Response.StatusCode; // Should be 200 from MockStartedResponseFeature
        
        var sut = new LambdaTimeoutLinkMiddleware(timeoutDelegate, logger, TimeSpan.FromMilliseconds(10));

        // Act
        await sut.InvokeAsync(responseStarted);

        // Assert - Response should remain unchanged when already started
        responseStarted.Response.StatusCode.Should().Be(originalStatusCode,
            "Status code should not be modified when response has already started");
    }

    /// <summary>
    /// Verifies that when time has already expired, the middleware short-circuits immediately.
    /// </summary>
    [Theory]
    [MiddlewareAutoData]
    public async Task InvokeAsync_WithExpiredTime_ShouldShortCircuitWithTimeoutStatus(
        [Frozen] HttpContext withLambdaContext, [Frozen] RequestDelegate trackingNext,
        [Frozen] ILambdaContext lambdaContext,
        ILogger<LambdaTimeoutLinkMiddleware> logger)
    {
        // Arrange
        lambdaContext.RemainingTime.Returns(TimeSpan.FromMilliseconds(100)); // Short time
        var sut = new LambdaTimeoutLinkMiddleware(trackingNext, logger, TimeSpan.FromMilliseconds(200)); // Buffer larger than remaining
        
        // Act
        await sut.InvokeAsync(withLambdaContext);

        // Assert
        var nextCalled = withLambdaContext.Items.ContainsKey("NextCalled");
        nextCalled.Should().BeFalse("Next middleware should not be called when time is already expired");
        withLambdaContext.Response.StatusCode.Should().Be(StatusCodes.Status504GatewayTimeout,
            "Should immediately return 504 when time is already expired");
        withLambdaContext.Response.ContentLength.Should().Be(0,
            "Should set content length to 0");
    }
}