# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This is a .NET library that provides extensions and middleware for Amazon.Lambda.AspNetCoreServer.Hosting, designed specifically for AWS Lambda hosting environments. It delivers improved reliability, observability, and developer experience for ASP.NET Core applications running on AWS Lambda.

## Project Structure

- **src/LayeredCraft.Lambda.AspNetCore.HostingExtensions/** - Main library project containing middleware and extensions
- **Middleware/LambdaTimeoutLinkMiddleware.cs** - Core middleware that links Lambda timeout with HTTP request cancellation

## Key Architecture

The main component is `LambdaTimeoutLinkMiddleware` which provides timeout handling by:
- Creating a cancellation token that triggers on either client disconnect or Lambda timeout
- Replacing `HttpContext.RequestAborted` with a linked token during request processing
- Setting appropriate status codes (504 for timeout, 499 for client disconnect)
- Operating as pass-through in local development where `ILambdaContext` is unavailable

## Development Commands

### Build
```bash
dotnet build
```

### Test
```bash
dotnet test
```

### Restore Dependencies
```bash
dotnet restore
```

## Target Frameworks

The project targets both .NET 8.0 and .NET 9.0 (`net8.0;net9.0`).

## Key Dependencies

- **Amazon.Lambda.AspNetCoreServer** (v9.2.0) - AWS Lambda ASP.NET Core hosting
- **LayeredCraft.StructuredLogging** (v1.1.1.8) - Structured logging utilities

## Package Information

This is a packable library (NuGet package) with:
- MIT license
- SourceLink support for debugging
- Symbol packages (.snupkg) for enhanced debugging experience
- Version prefix: 2.0.1