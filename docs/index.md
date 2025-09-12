---
layout: default
title: LayeredCraft Lambda ASP.NET Core Hosting Extensions
---

[![Build Status](https://github.com/LayeredCraft/lambda-aspnetcore-hosting-extensions/actions/workflows/build.yaml/badge.svg)](https://github.com/LayeredCraft/lambda-aspnetcore-hosting-extensions/actions/workflows/build.yaml)
[![NuGet](https://img.shields.io/nuget/v/LayeredCraft.Lambda.AspNetCore.HostingExtensions.svg)](https://www.nuget.org/packages/LayeredCraft.Lambda.AspNetCore.HostingExtensions/)
[![Downloads](https://img.shields.io/nuget/dt/LayeredCraft.Lambda.AspNetCore.HostingExtensions.svg)](https://www.nuget.org/packages/LayeredCraft.Lambda.AspNetCore.HostingExtensions/)

Extensions and middleware for Amazon.Lambda.AspNetCoreServer.Hosting. Provides ASP.NET Core components designed specifically for AWS Lambda hosting, delivering improved reliability, observability, and developer experience.

## Key Features

- **⏱️ Lambda Timeout Handling**: Intelligent timeout middleware that links Lambda execution limits with HTTP request cancellation
- **🔄 Graceful Shutdown**: Proper handling of approaching Lambda timeouts with configurable safety buffers
- **🛠️ Developer Experience**: Standard CancellationToken patterns work seamlessly in Lambda environments
- **🧪 Local Development**: Pass-through behavior when running outside Lambda (Kestrel, IIS Express)
- **📊 Observability**: Structured logging with detailed timeout and cancellation telemetry

## Installation

```bash
dotnet add package LayeredCraft.Lambda.AspNetCore.HostingExtensions
```

## Quick Start

```csharp
using LayeredCraft.Lambda.AspNetCore.Hosting.Extensions;

public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    // Add Lambda timeout-aware cancellation early in the pipeline
    app.UseLambdaTimeoutLinkedCancellation();
    
    // Your other middleware
    app.UseRouting();
    app.UseEndpoints(endpoints =>
    {
        endpoints.MapControllers();
    });
}
```

## Available Middleware

### [Lambda Timeout Middleware](middleware/lambda-timeout-middleware.md)

Comprehensive timeout handling for Lambda environments with:

- Linked cancellation tokens (client disconnect + Lambda timeout)
- Configurable safety buffers for graceful shutdown
- Appropriate HTTP status code handling (504/499)
- Structured logging for observability
- Local development pass-through mode

## Documentation

- **[Examples](examples/index.md)** - Real-world usage examples and patterns

## How It Works

The library provides middleware that integrates with AWS Lambda's execution model to handle timeouts gracefully. When Lambda execution approaches its timeout limit, the middleware triggers cancellation tokens that downstream code can respond to using standard .NET patterns.

### Key Benefits

1. **Prevents Lambda Cold Timeouts**: Graceful shutdown before Lambda forcibly terminates
2. **Standard Patterns**: Use familiar `CancellationToken` APIs everywhere
3. **Observability**: Detailed logging helps diagnose timeout issues
4. **Development Friendly**: Works seamlessly in local and Lambda environments

## Requirements

- **.NET 8.0** or **.NET 9.0**
- **Amazon.Lambda.AspNetCoreServer** 9.2.0+
- **LayeredCraft.StructuredLogging** 1.1.1.8+

## Contributing

See the main [README](https://github.com/LayeredCraft/lambda-aspnetcore-hosting-extensions#contributing) for contribution guidelines.

## License

This project is licensed under the [MIT License](https://github.com/LayeredCraft/lambda-aspnetcore-hosting-extensions/blob/main/LICENSE).