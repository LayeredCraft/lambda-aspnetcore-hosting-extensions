using System.Reflection;
using Amazon.Lambda.AspNetCoreServer;
using Amazon.Lambda.Core;
using AutoFixture.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace LayeredCraft.Lambda.AspNetCore.HostingExtensions.Tests.TestKit.SpecimenBuilders;

public class HttpContextSpecimenBuilder : ISpecimenBuilder
{
    public object Create(object request, ISpecimenContext context)
    {
        return request switch
        {
            ParameterInfo parameterInfo when parameterInfo.ParameterType == typeof(HttpContext) => parameterInfo.Name?.ToLowerInvariant()
                switch
                {
                    "nulllambdacontext" or "nolambdacontext" => CreateDefaultHttpContext(context, hasLambdaContext: false),
                    "withlambdacontext" or "lambdacontext" => CreateDefaultHttpContext(context, hasLambdaContext: true),
                    "responsestarted" => CreateDefaultHttpContext(context, hasLambdaContext: true, responseStarted: true),
                    "precancelledcontext" => CreateDefaultHttpContext(context, hasLambdaContext: true, preCancelled: true),
                    _ => CreateDefaultHttpContext(context)
                },
            Type type when type == typeof(HttpContext) => CreateDefaultHttpContext(context),
            _ => new NoSpecimen()
        };
    }
    
    private static HttpContext CreateDefaultHttpContext(ISpecimenContext context, bool hasLambdaContext = true, bool responseStarted = false, bool preCancelled = false)
    {
        var httpContext = new DefaultHttpContext
        {
            Request =
            {
                // Configure request with valid defaults
                Method = "GET",
                Path = "/api/test",
                Scheme = "https",
                Host = new HostString("localhost")
            }
        };

        // Initialize response headers collection
        httpContext.Response.Headers.Clear();

        if (hasLambdaContext)
        {
            var lambdaContext = context.Resolve(typeof(ILambdaContext));
            httpContext.Items[AbstractAspNetCoreFunction.LAMBDA_CONTEXT] = lambdaContext;
        }

        if (responseStarted)
        {
            // Simulate response already started by setting StatusCode
            httpContext.Response.StatusCode = 200;
            // In a real scenario, writing to the response would start it, but we can't easily simulate that
            // The middleware checks HasStarted, so we need to simulate that condition
            httpContext.Features.Set<IHttpResponseFeature>(new MockStartedResponseFeature());
        }

        if (preCancelled)
        {
            // Create a pre-cancelled token
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            httpContext.RequestAborted = cts.Token;
        }

        return httpContext;
    }

    /// <summary>
    /// Mock response feature that simulates a started response
    /// </summary>
    private class MockStartedResponseFeature : IHttpResponseFeature
    {
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true; // This is the key property we need
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public string? ReasonPhrase { get; set; }
        public int StatusCode { get; set; } = 200;

        public void OnCompleted(Func<object, Task> callback, object state) { }
        public void OnStarting(Func<object, Task> callback, object state) { }
    }
}