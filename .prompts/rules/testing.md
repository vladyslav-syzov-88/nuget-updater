---
description: Testing guidelines for the Account Management Service
globs: **/*Tests*/**/*.cs
---

# Testing Guidelines

## Framework

- **NUnit** for test framework
- **Moq** for mocking dependencies
- **coverlet** for code coverage

## Structure

- Test project: `Telenor.Api.AccountManagement.Tests`
- Mirror the domain folder structure from the main project (e.g., `BillingAccountManagement/`, `PaymentManagement/`)
- Test classes should test a single manager or component

## Conventions

- Internal types are accessible via `InternalsVisibleTo` (configured in the service `.csproj`)
- The test project references the service project directly
- Use `[ExcludeFromCodeCoverage]` assembly attribute (already configured in test `.csproj`)

## Coverage

- Minimum required **linear code coverage: 80%**
- Ensure new or modified code is covered by tests before submitting changes

## Running Tests

```bash
dotnet test
dotnet test --logger:trx                         # TRX output (used in Docker build)
dotnet test --collect:"XPlat Code Coverage"       # With coverage
```
