# Copilot Instructions: Architecture

## Purpose
High-level structural and technology guidance for the solution.

## Technology Stack
- .NET (latest C# features, file-scoped namespaces)
- Blazor for web UI
- ASP.NET Core for APIs
- .NET Aspire for orchestration / service discovery
- Bootstrap + custom CSS variables for theming

## Core Projects
- Main web app
- Backend APIs
- Shared models / services / utilities
- Service orchestration
- Unit / integration tests

## External Integrations
- [TBD]: [DESCRIPTION]

## Folder Structure
- `/.azure`: Azure deployment config
- `/src`: Source code
- `/infra`: Infrastructure as Code
- `/docs`: Documentation
 - `/docs/specs`: Specifications (versioned)
 - `/docs/plans`: Plans (versioned)
 - `/docs/plans/api`, `/docs/plans/ui`, `/docs/plans/backend`, `/docs/plans/shared`, `/docs/plans/infra`, `/docs/plans/tests`
- `/tests`: Test projects/assets
- `azure.yaml`: Main AZD config

### Source Code Layout
- `/src/api`: Backend APIs
- `/src/web`: Frontend web
- `/src/shared`: Shared libraries
- `/src/functions`: Azure Functions
- `/src/workers`: Background services
- `/infra/`: Infrastructure assets

### Example AZD Config
```yaml
name: [PROJECT_NAME]
infra:
 provider: bicep
 path: infra
 module: main
services:
 api:
 project: src/api/[API_PROJECT_NAME]
 language: csharp
 host: appservice
 web:
 project: src/web/[WEB_PROJECT_NAME]
 language: js
 host: staticwebapp
```

## Project Setup Convention
Use namespace-based folder/project structure: `[Company].[Project].[Component].[Function]`.

## Code Style (Global)
- Prefer async/await
- Use nullable reference types
- Use `var` for local variables
- Implement `IDisposable` for event handlers/subscriptions
- Use latest C# features
- Naming: PascalCase (public), camelCase (private)
- Use dependency injection and interfaces
- Using directives order: `System`, `Microsoft`, then app namespaces (alphabetical)

## File Organization
- Group related files by feature / domain
- Use meaningful names
- Keep consistent structure

## Architectural Patterns (Cross-Cutting)
- Source-generated JSON serialization contexts
- Clear separation of concerns (UI, API, shared contracts)
- Service discovery via Aspire

## Domain Architecture & Data Flow
- Document key domain entities and relationships
- Organize models by domain
- Use mock services and adapters for external dependencies
- Document data collection, normalization, caching

## Integration Points
- Service orchestration & discovery
- Environment configuration management
- API design (request/response, caching, background processing)
- Cross-component communication and shared contracts

## Authentication & Security Patterns (Overview)
- Document auth & access control per service
- Manage API keys and secrets per environment

(Backend-specific implementation details live in backend instructions.)
