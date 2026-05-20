# Copilot Instructions: Documentation Pass

## Status
This instruction file is **mandatory** for any task that creates or updates source code in this repository, and for any task that reviews or plans such work.

These requirements are **non-negotiable**. They must be followed in addition to the repository's other coding, architecture, and testing instructions.

## Primary Constraint
- Documentation-pass work must **not change, remove, or otherwise alter any code behaviour**.
- Any change beyond source-code comments is out of scope unless a separate instruction explicitly authorizes non-comment code changes.
- Formatting-only tidy-up is not allowed, except for the minimum formatting directly required to insert comments cleanly.
- All existing non-comment formatting must otherwise be preserved exactly.

## Scope of a Documentation Pass
- Inspect every hand-maintained `.cs` file within the explicitly scoped projects or folders for that run.
- Exclude generated or machine-maintained C# files.
- Exclusions include generated output such as `obj` artifacts, designer/generated output, and source-generator output.
- The scope applies only to the projects or files explicitly named for the work item; do not widen scope implicitly.

## Public XML Documentation Standard
Apply explicit local XML documentation in the source file for all eligible public API surface. Do **not** rely on inherited documentation alone.

### Eligible public API surface
When present in scope, XML documentation must be added or improved for:
- public classes
- public interfaces
- public records
- public enums
- public delegates
- public extension classes
- public methods
- public constructors
- public properties, including trivial DTO, record, options, and configuration properties
- public events
- public indexers
- public operators
- public fields
- public enum members
- public delegate parameters
- public generic type parameters on public generic types and methods via `<typeparam>`

### Partial types
- Every partial declaration file must carry XML comments where that partial declares public API members.

### Extension methods
- The `this` parameter of a public extension method must be documented fully like any other parameter, including important constraints and expectations.

### Tuple-returning APIs
- Public tuple-returning APIs must explicitly document the meaning of individual tuple elements whenever element names alone are not sufficient.

### Obsolete APIs
- Obsolete or deprecated public APIs must still be brought up to the same XML documentation standard while they remain in source.

### XML depth requirements
XML documentation must be high-depth and should include, where applicable:
- `<summary>`
- `<param>` for every parameter
- `<typeparam>` for every public generic type parameter
- `<returns>`
- `<remarks>` where it can be reliably inferred and is useful
- `<exception>` only for exceptions that are explicit or clearly intentional in the code
- `<example>` only for especially complex public APIs in production projects; examples are not required for test projects

### Async and nullability requirements
- Asynchronous public methods must explicitly document cancellation expectations and notable externally visible side effects wherever relevant.
- XML comments should explicitly call out meaningful nullability expectations whenever type annotations alone are not sufficient to explain important null-handling semantics.

## Developer-Level Commenting Standard
Developer comments are mandatory throughout the implementation, not only on public APIs.

- The same developer-level documentation standard applies to internal and other non-public types, not only to public API surface.
- Internal classes, internal records, internal structs, internal interfaces, and other non-public types must carry an explicit type-level comment describing purpose, context, and responsibility.
- Constructors and methods on internal and other non-public types must receive the same explicit local documentation treatment as constructors and methods on public types.
- When XML documentation syntax is supported and keeps the source readable, prefer explicit local XML documentation for internal types and their constructors/methods as well so the code reads in one consistent style.

### Methods and executable logic
- Every method must contain a clear explanatory comment block describing purpose, logical flow, and rationale.
- Every constructor must also contain a clear explanatory comment block describing purpose, dependencies, and initialization intent.
- Multi-step methods must also include step-by-step inline comments where needed to explain the flow and why the code works the way it does.
- This requirement applies even when the implementation is empty, trivial, or apparently self-evident.
- Local functions and non-trivial lambda expressions must also receive developer-level explanatory comments when they contain meaningful logic.

### Non-public members and types
- Non-public types do not require XML documentation by default, but they do require explicit type-level documentation comments and their constructors and methods still require the mandatory developer-level comments.
- Internal classes must not be left undocumented merely because they are not public.
- Private properties, fields, and constants should receive explanatory comments where needed to explain non-obvious purpose, coupling, constraints, or behaviour.

### Top-level programs
- Top-level host bootstrap files such as `Program.cs` must receive developer-level comments throughout top-level statements and local functions, with XML comments used only where C# supports them.

## Test Code Documentation Standard
- Hand-written test host, bootstrap, fixture, and setup code must receive the same developer-comment standard as other hand-written code in scope.
- Test methods must be documented especially carefully so the test scenario, setup, action, assertion intent, and behavioural significance are clear.
- Do not force a rigid `Arrange` / `Act` / `Assert` comment structure if a different explanation is clearer.

## Existing Comment Handling
- Existing weak or inconsistent comments should be rewritten so the in-scope code reads in one consistent documentation style.
- Existing acceptable comments may remain unless they need to be improved to meet this standard.
- Existing `TODO`, `FIXME`, and placeholder comments should not be broadly rewritten or removed; leave acceptable existing comments in place unless they are directly superseded by the required production-quality documentation.

## Content Source of Truth
- Added comments may explain both technical intent and relevant domain/business intent where that materially improves understanding and can be supported by repository code and documentation context.
- Where sources disagree, use this source-of-truth order:
  1. current code behaviour
  2. current wiki guidance
  3. older work-package documentation
- Where exact intent is not directly explicit, infer the most likely intent from the surrounding code and current repository documentation rather than defaulting to minimal wording.
- If material ambiguity still remains after using the code and repository documentation, it is acceptable to indicate that uncertainty in source comments rather than presenting unsupported certainty.

## Delivery and Validation Requirements
- Delivery may consist solely of comment updates; no separate checklist or audit summary is required unless the specific work item asks for one.
- After a documentation-only pass, validation must include:
  - a full solution build
  - a full test suite run
- The work is not complete until those validations succeed or any failures are explained as pre-existing and unrelated.

## Planning and Execution Expectations
- Plans that involve coding work must explicitly reference this instruction file and treat it as mandatory.
- Execution prompts, implementation plans, and coding tasks must not treat documentation as optional polish.
- Code is not acceptable unless this documentation standard is met wherever the current task applies.
