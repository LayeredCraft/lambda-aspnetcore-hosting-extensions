using System.Reflection;
using AutoFixture.Kernel;
using Microsoft.AspNetCore.Http;

namespace LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.TestKit.SpecimenBuilders;

/// <summary>
/// Specimen builder for creating RequestDelegate instances with different behaviors based on parameter names.
/// </summary>
public class RequestDelegateSpecimenBuilder : ISpecimenBuilder
{
    public object Create(object request, ISpecimenContext context)
    {
        if (request is not ParameterInfo parameterInfo || 
            parameterInfo.ParameterType != typeof(RequestDelegate))
        {
            return new NoSpecimen();
        }

        var parameterName = parameterInfo.Name?.ToLowerInvariant();

        return parameterName switch
        {
            // For tests that need to capture the token during execution
            "capturingnext" or "capturingdelegate" => CreateCapturingRequestDelegate(),
            
            // For tests that just need to verify it was called
            "trackingnext" or "trackingdelegate" => CreateTrackingRequestDelegate(),
            
            // For timeout cancellation testing
            "timeoutdelegate" or "timeoutcancelling" => CreateTimeoutCancellingDelegate(),
            
            // For client disconnect cancellation testing
            "disconnectdelegate" or "disconnectcancelling" => CreateClientDisconnectDelegate(),
            
            // Default: simple delegate that just completes
            _ => CreateSimpleRequestDelegate()
        };
    }

    private static RequestDelegate CreateCapturingRequestDelegate()
    {
        return ctx =>
        {
            // Store captured data in HttpContext.Items for test access
            ctx.Items["NextCalled"] = true;
            ctx.Items["CapturedToken"] = ctx.RequestAborted;
            return Task.CompletedTask;
        };
    }

    private static RequestDelegate CreateTrackingRequestDelegate()
    {
        return ctx =>
        {
            ctx.Items["NextCalled"] = true;
            return Task.CompletedTask;
        };
    }

    private static RequestDelegate CreateTimeoutCancellingDelegate()
    {
        return async ctx =>
        {
            // Wait for cancellation token to be triggered (by timeout)
            // This simulates a long-running operation that gets cancelled by timeout
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ctx.RequestAborted);
            }
            catch (OperationCanceledException ex)
            {
                // Re-throw with the same token that cancelled us
                throw new OperationCanceledException("Operation was cancelled", ex, ctx.RequestAborted);
            }
        };
    }

    private static RequestDelegate CreateClientDisconnectDelegate()
    {
        return ctx =>
        {
            // Simulate client disconnect by throwing with the context's RequestAborted token
            // This will ensure the middleware sees it as a client disconnect rather than timeout
            throw new OperationCanceledException(ctx.RequestAborted);
        };
    }

    private static RequestDelegate CreateSimpleRequestDelegate()
    {
        return _ => Task.CompletedTask;
    }
}