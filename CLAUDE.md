# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands
- Build: `dotnet build`
- Run: `dotnet run --project LnProxyApi.App`
- Run tests: `dotnet test`
- Run single test: `dotnet test --filter "FullyQualifiedName=LnProxyApi.App.Tests.{TestClassName}.{TestMethodName}"`
- Format code: `dotnet format`

## Code Style Guidelines
- **Naming**: PascalCase for classes/methods, camelCase for variables, prefix private fields with `_`
- **Nullability**: Use nullable reference types (`string?`) and required keyword where appropriate
- **Exceptions**: Catch specific exceptions, include descriptive error messages, log before rethrowing
- **Imports**: System imports first, followed by third-party, then project namespaces
- **Formatting**: 4-space indentation, braces on new lines, blank line between methods
- **Pattern**: Use dependency injection for services, follow controller-service pattern
- **Error Handling**: Return appropriate HTTP status codes with meaningful error objects
- **Testing**: Use NUnit, follow Arrange-Act-Assert pattern, name tests as `Method_Condition_Outcome`

## Project Structure
- ASP.NET Core Web API using .NET 8.0
- NUnit for testing with Moq for mocking
- gRPC for Lightning Network communication