---
description: C# coding conventions for the Account Management Service
globs: **/*.cs
---

# C# Conventions

## General

- Target framework: .NET 9
- Use file-scoped namespaces
- Use `default` language version
- Follow the existing namespace convention: `Telenor.Api.AccountManagement.Service.{Domain}.{Layer}`

## Naming

- Async methods must be suffixed with `Async`
- Interfaces are prefixed with `I` and placed in `Abstract/` subdirectories
- DTOs are suffixed with `Dto` and placed in `Models/` directories under the repository layer
- Validators are suffixed with `Validator`

## Patterns

- Use constructor injection for dependencies
- Use `IReadOnlyList<T>` for return types of collection queries
- Use records for API models; prefer `with` expressions for immutable updates
- Use the decorator pattern for cross-cutting concerns (e.g., caching)
- Register services via `ServiceCollectionExtensions` with `AddXxx()` extension methods
- Mark infrastructure code with `[ExcludeFromCodeCoverage]` (controllers, DI registration, Program.cs)

## Controllers

- Inherit from `ApiControllerBase`
- Use `[ApiVersion]` attribute with URL path versioning
- Annotate with `[SwaggerOperation]` and `[SwaggerParameter]` for documentation
- Use `[Produces]` and `[ProducesResponseType]` attributes for all endpoints
- Keep controllers thin — delegate business logic to manager classes

## Error Handling

- Use `ApiError` for error responses
- Define domain errors in `BusinessLogic/Exceptions/AccountManagementErrors.cs`
- Throw exceptions from business logic; let middleware handle HTTP responses
