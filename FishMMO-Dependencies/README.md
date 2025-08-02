# FishMMO-Dependencies

This project is a .NET Standard 2.1 class library that provides a collection of dependencies for the FishMMO ecosystem, intended for use in Unity and related projects. The library centralizes and manages third-party packages to simplify dependency management and ensure compatibility across the FishMMO solution.

## Included Packages

The following NuGet packages are referenced in the project:

- **EFCore.NamingConventions**: Adds naming convention support for Entity Framework Core.
- **HtmlAgilityPack**: HTML parser for .NET.
- **Humanizer**: Human-friendly string, date, and number formatting.
- **Microsoft.Bcl.AsyncInterfaces**: Provides async interfaces for .NET Standard.
- **Microsoft.EntityFrameworkCore**: Entity Framework Core ORM.
- **Microsoft.EntityFrameworkCore.Abstractions**: Abstractions for EF Core.
- **Microsoft.EntityFrameworkCore.Design**: Design-time tools for EF Core.
- **Microsoft.EntityFrameworkCore.Relational**: Relational database support for EF Core.
- **Microsoft.EntityFrameworkCore.Tools**: Tools for EF Core migrations and scaffolding.
- **Microsoft.Extensions.Caching.Abstractions** / **Memory**: Caching primitives and in-memory cache.
- **Microsoft.Extensions.Configuration** (+Abstractions, +Json): Configuration framework with JSON support.
- **Microsoft.Extensions.DependencyInjection** (+Abstractions): Dependency injection framework.
- **Microsoft.Extensions.Logging** (+Abstractions): Logging framework.
- **Microsoft.Extensions.Options**: Options pattern support.
- **Microsoft.Extensions.Primitives**: Change tracking primitives.
- **Npgsql**: PostgreSQL database provider.
- **Npgsql.EntityFrameworkCore.PostgreSQL**: PostgreSQL support for EF Core.
- **OpenAI**: OpenAI API client.
- **srp**: Secure Remote Password protocol implementation.
- **StackExchange.Redis**: Redis client library.
- **StackExchange.Redis.Extensions.Core**: Extensions for StackExchange.Redis.
- **System.Collections.Immutable**: Immutable collection types.
- **System.ComponentModel.Annotations**: Data annotation attributes.
- **System.Diagnostics.DiagnosticSource**: Diagnostic instrumentation.
- **System.IO.Hashing**: Hashing utilities.
- **System.Runtime.CompilerServices.Unsafe**: Low-level compiler services.
- **System.Text.Encodings.Web**: Web encoders.
- **System.Text.Json**: High-performance JSON serialization.
- **System.Threading.Channels**: High-performance producer/consumer data structures.

## Build & Usage

1. **Build the Project**
   - Use your preferred .NET build tool (e.g., Visual Studio, `dotnet build`).

2. **DLL Output**
   - After building, the resulting DLLs are copied to the `FishMMO-Unity/Assets/Dependencies` directory for use in Unity.

3. **Custom Target**
   - The `.csproj` includes a custom MSBuild target to automate copying built DLLs (excluding some system assemblies) to the Unity project.