# Copilot Instructions: Testing

## Scope
Guidance for unit, integration, E2E, load/performance testing across solution.

## Folder Layout
- `/tests/unit`: Unit tests
- `/tests/integration`: Integration tests
- `/tests/e2e`: End-to-end tests
- `/tests/load`: Load / performance tests

## General Principles
- Test behavior, not implementation details.
- Isolate external dependencies (mocks, stubs, fakes).
- Naming: `MethodName_WhenCondition_ShouldOutcome` for unit tests.

## Unit Testing
- Focus on pure domain/service logic.
- Mock infrastructure / external clients.
- Cover error scenarios and edge cases.

## Integration Testing
- Exercise real data flow with minimal mocks.
- Use in-memory / containerized dependencies where feasible.
- Verify serialization, routing, filters/middleware.

## E2E Testing
- Validate user journeys across UI + backend.
- Prefer stable data fixtures.
- Include accessibility checks where possible.

## Load & Performance Testing
- Identify critical endpoints and flows.
- Define baseline metrics and thresholds.
- Automate regular runs (CI integration optional).

## Test Data Management
- Use builders/factories for complex objects.
- Clean up side effects deterministically.

## Error Scenario Coverage
- Invalid inputs
- Auth / permission failures
- Timeouts / transient faults (with retry policy tests)

## Mocking Strategy
- Interfaces for services -> use mocking frameworks.
- For external HTTP: use test servers / handlers.

## Frontend Component Testing
- Logic extraction into services enables pure unit tests.
- Render tests for interactive components (see frontend instructions).

## Documentation
- Keep a brief README per test project for scope & execution instructions.

(Architecture & domain references in architecture instructions.)
