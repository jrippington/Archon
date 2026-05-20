# Copilot Instructions: Frontend

## Scope
Guidance for Blazor UI development, styling, accessibility, performance.

## General Principles
- Blazor components: small, focused, reusable.
- Extract non-UI logic into services.
- Prefer parameters over cascading values.
- Respect light/dark themes via CSS variables.

## Styling
- Add component-scoped CSS files for each new component.
- Avoid global styles unless absolutely required.
- Use semantic class names; leverage Bootstrap spacing/utilities.
- Document non-obvious style choices with comments.

## Theming
- Use CSS custom properties for colors and theme values.
- Test in both light and dark themes before merge.

## Responsive & Accessible Design
- Use `rem` / `em` for sizing.
- Semantic HTML structure.
- ARIA attributes where needed.
- Test keyboard navigation and screen readers.

## Component Structure
- Keep render fragments concise.
- Use partial classes or code-behind for complex logic.
- Dispose of event subscriptions (`IDisposable`).
- Prefer `@key` for lists to stabilize diffing.

## Error Handling
- Use try/catch inside event handlers.
- Provide user-friendly error messages.
- Use error boundaries for isolating failures.

## Performance Practices
- Avoid unnecessary state changes that trigger re-render.
- Use virtualization for large lists/grids.
- Defer heavy work to background services / APIs.
- Measure and refactor hot paths.

## Accessibility Checklist
- Keyboard focus order logical.
- ARIA roles only when native semantics insufficient.
- Color contrast meets WCAG AA.
- Forms: associate labels with inputs.

## Security (UI Layer)
- Validate user input client-side (as convenience) but rely on server validation.
- Avoid exposing secrets in frontend code.

## Testing (Frontend)
- Unit test complex component logic (see testing instructions file).
- Snapshot / rendering tests for visual regressions where feasible.

## Documentation
- Provide usage examples for reusable components.
- Include parameter tables if complex.

(Architecture-wide conventions in architecture instructions.)
