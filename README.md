# LayeredCraft Lambda ASP.NET Core Hosting Extensions

[![Build Status](https://github.com/LayeredCraft/lambda-aspnetcore-hosting-extensions/actions/workflows/build.yaml/badge.svg)](https://github.com/LayeredCraft/lambda-aspnetcore-hosting-extensions/actions/workflows/build.yaml)
[![NuGet](https://img.shields.io/nuget/v/LayeredCraft.Lambda.AspNetCore.HostingExtensions.svg)](https://www.nuget.org/packages/LayeredCraft.Lambda.AspNetCore.HostingExtensions/)
[![Downloads](https://img.shields.io/nuget/dt/LayeredCraft.Lambda.AspNetCore.HostingExtensions.svg)](https://www.nuget.org/packages/LayeredCraft.Lambda.AspNetCore.HostingExtensions/)

Extensions and middleware for Amazon.Lambda.AspNetCoreServer.Hosting. Provides ASP.NET Core components designed specifically for AWS Lambda hosting, delivering improved reliability, observability, and developer experience.

## Features

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

### Basic Timeout Middleware

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

### With Custom Safety Buffer

```csharp
public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    // Custom safety buffer of 500ms for cleanup operations
    app.UseLambdaTimeoutLinkedCancellation(TimeSpan.FromMilliseconds(500));
    
    app.UseRouting();
    app.UseEndpoints(endpoints =>
    {
        endpoints.MapControllers();
    });
}
```

### Using Cancellation in Controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class DataController : ControllerBase
{
    private readonly IDataService _dataService;

    public DataController(IDataService dataService)
    {
        _dataService = dataService;
    }

    [HttpGet]
    public async Task<IActionResult> GetData(CancellationToken cancellationToken)
    {
        try
        {
            // This will be cancelled if Lambda timeout approaches
            var data = await _dataService.GetDataAsync(cancellationToken);
            return Ok(data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Lambda timeout occurred - return appropriate response
            return StatusCode(504, "Request timeout");
        }
    }
}
```

## Documentation

📖 **[Complete Documentation](https://layeredcraft.github.io/lambda-aspnetcore-hosting-extensions/)**

- **[Lambda Timeout Middleware](https://layeredcraft.github.io/lambda-aspnetcore-hosting-extensions/middleware/lambda-timeout-middleware)** - Comprehensive timeout handling for Lambda environments
- **[Examples](https://layeredcraft.github.io/lambda-aspnetcore-hosting-extensions/examples)** - Real-world usage examples and patterns

## How It Works

### Lambda Timeout Linking

The `LambdaTimeoutLinkMiddleware` creates a sophisticated cancellation token that triggers when either:

1. **Client disconnects** or the server aborts the request (standard ASP.NET Core behavior)
2. **Lambda timeout approaches** (calculated from `ILambdaContext.RemainingTime` with safety buffer)

The middleware replaces `HttpContext.RequestAborted` with this linked token, enabling downstream code to respond to Lambda timeouts through standard `CancellationToken` patterns.

### Safety Buffer

The configurable safety buffer (default: 250ms) ensures your application has time to:
- Complete cleanup operations
- Write final log entries
- Return appropriate HTTP status codes
- Flush telemetry data

### Local Development

When running locally (Kestrel, IIS Express) where `ILambdaContext` is unavailable, the middleware operates as a pass-through with only standard client disconnect cancellation active.

## Status Codes

The middleware automatically sets appropriate HTTP status codes on timeout:

- **504 Gateway Timeout**: Lambda execution timeout occurred
- **499 Client Closed Request**: Client disconnected (non-standard but widely recognized)

## Requirements

- **.NET 8.0** or **.NET 9.0**
- **Amazon.Lambda.AspNetCoreServer** 9.2.0+
- **LayeredCraft.StructuredLogging** 1.1.1.8+

## Contributing

We welcome contributions! Please see our [Contributing Guidelines](CONTRIBUTING.md) for details.

### Development Setup

```bash
# Clone the repository
git clone https://github.com/LayeredCraft/lambda-aspnetcore-hosting-extensions.git
cd lambda-aspnetcore-hosting-extensions

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run tests
dotnet test
```

### Code Style

- Follow C# coding conventions
- Use meaningful names for variables and methods
- Add XML documentation for public APIs
- Include unit tests for new features
- Run tests before submitting PRs

## License

This project is licensed under the [MIT License](LICENSE).

## Support

- **Issues**: [GitHub Issues](https://github.com/LayeredCraft/lambda-aspnetcore-hosting-extensions/issues)
- **Discussions**: [GitHub Discussions](https://github.com/LayeredCraft/lambda-aspnetcore-hosting-extensions/discussions)
- **Documentation**: [https://layeredcraft.github.io/lambda-aspnetcore-hosting-extensions/](https://layeredcraft.github.io/lambda-aspnetcore-hosting-extensions/)

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for details on releases and changes.

---

Built with ❤️ by the LayeredCraft team
