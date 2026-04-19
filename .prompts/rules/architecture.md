---
description: Architectural rules and domain organization for the Account Management Service
globs: **/*.cs
---

# Architecture Rules

## Domain Organization

Each domain (BillingAccountManagement, PaymentManagement, PaymentMethodManagement, HardwareAllowanceAccountManagement) is a self-contained module with:

- `Controllers/` - API endpoints
- `BusinessLogic/` - Managers, mappers, validators, models
- `ServiceCollectionExtensions.cs` - DI registration

Shared code lives in the root-level `BusinessLogic/` directory (repositories, models, configuration interfaces).

## API Versioning

- Each domain maintains its own API version (e.g., BillingAccount is at V5, Payment at V3)
- New versions get their own folder (e.g., `V5/`) with controllers and version-specific business logic
- Do not modify existing versioned APIs — create a new version instead

## Dependency Flow

```
Controllers → Managers → Repositories/Clients
                ↓
             Mappers
```

- Controllers depend only on manager interfaces
- Managers orchestrate between repositories and apply business rules
- Repositories handle data access and external service calls
- Mappers transform between layers (DTOs ↔ API models)

## External Service Communication

- External services are accessed via typed HTTP clients (e.g., `AgreementManagementClient`, `PartyManagementClient`)
- Client interfaces are defined in `BusinessLogic/Repositories/Abstract/`
- REST client configuration is loaded from the `restServices` config section

## Caching Strategy

- Redis-based caching via `Telenor.Api.Caching.Redis`
- Caching is applied via the decorator pattern — cached repositories wrap base repositories
- Cache clearing endpoints are exposed for operational use
