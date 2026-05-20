# Copilot Instructions: Backend

## Scope
Guidance for API, services, background processing, security.

## Backend Development Principles
- Prefer Minimal APIs over Controllers to reduce boilerplate.
- Consistent .NET target with nullable reference types enabled.
- Clear separation: endpoints, domain logic, infrastructure.

## API Design
- Explicit request/response models (no leaking internal domain entities).
- Source-generated JSON serialization contexts for performance.
- Provide versioning strategy when breaking changes loom.

## Configuration & Environment
- Create local settings files (exclude secrets from source control).
- Use storage emulators for local development (Azurite etc.).
- Manage environment-specific API keys and secrets securely.

## Security & Authentication
- Centralize authentication configuration.
- Enforce authorization at endpoint or minimal API route group level.
- Input validation on all external-facing boundaries.

## Error Handling & Logging
- Global exception handling middleware / filters.
- Log structured messages (correlation IDs where applicable).
- User-friendly error payloads (avoid leaking internals).

## Performance
- Cache where appropriate (document strategy).
- Async all I/O-bound operations.
- Avoid synchronous over async wrappers.

## Data & Domain
- Document domain entities and relationships (see architecture file).
- Organize models by domain folder.
- Use adapters for external dependencies.

## Background Processing
- Use hosted services / workers for long-running tasks.
- Offload heavy compute from request pipeline.

## Development Patterns
- Dependency Injection for all services.
- Keep service interfaces lean and purposeful.
- Separate concerns: mapping, validation, business rules.

## Testing Hooks
- Provide abstractions to aid mocking (interfaces, virtual members).
- Use mock project(s) for external dependency simulation.

## Integration Points
- Document API endpoints (purpose, auth, examples).
- Caching and background processing strategies.
- Cross-component communication & shared contracts maintained in shared library.

(High-level architectural context in architecture instructions.)
