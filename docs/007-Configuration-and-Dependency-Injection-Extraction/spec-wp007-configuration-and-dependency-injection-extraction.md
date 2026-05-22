# WP007 Specification - Configuration and Dependency Injection Extraction

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP007 - Configuration and Dependency Injection Extraction |
| Output Path | `docs/007-Configuration-and-Dependency-Injection-Extraction/spec-wp007-configuration-and-dependency-injection-extraction.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP007 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP007, the Archon work package that extracts configuration usage and dependency-injection facts from analyzed .NET repositories. The package turns modern and legacy configuration patterns, dependency-injection registrations, service-location patterns, options binding, connection strings, and configuration-key usage into deterministic architecture graph facts.

WP007 builds on the prior extraction foundation, project/package extraction, semantic symbol extraction, snapshot orchestration, and Neo4j persistence work packages. It must contribute evidence-backed nodes, relationships, metadata, confidence, warnings, and unknowns through the established extraction pipeline rather than introducing a separate persistence or query path.

### 1.2 Background

Archon exists to provide deterministic, evidence-backed architecture intelligence for modern and legacy .NET estates. Configuration and dependency injection are core to that mission because they reveal runtime service composition, hidden external endpoints, options-driven behavior, connection strings, hosted service registration, HTTP client setup, container choices, and legacy service-location risks that are not fully visible from project references alone.

The controlling work-package sequence requires API-first and MCP-first product capability without introducing Archon Discovery UI implementation. WP007 therefore focuses on backend extraction and graph population only. Human-facing dashboards, pages, visual explorers, and other product UI surfaces remain excluded.

### 1.3 High-Level Scope

WP007 covers:

- Modern Microsoft dependency-injection registration extraction.
- Registration wrapper and extension-method extraction.
- Hosted-service and `HttpClient` registration extraction.
- Legacy container and service-locator detection.
- Service-to-implementation mapping extraction.
- Lifetime and registration-source metadata extraction.
- Constructor dependency correlation with service registrations.
- Modern configuration file and API usage extraction.
- Legacy `.config` and `ConfigurationManager` extraction.
- Options binding and options-consumer extraction.
- Configuration-key, section, provider, and connection-string extraction.
- Evidence, confidence, unknown-state, warning, and error emission.
- Tests for all production behavior introduced by this work package.
- Documentation updates explaining implemented extraction behavior and validation.

WP007 excludes Archon Discovery UI, data-access extraction beyond configuration clues such as connection-string names, runtime endpoint extraction beyond DI/configuration facts, external-integration extraction beyond configuration or registration hints, rule-engine evaluation, API query product surface expansion, MCP tools, markdown export, and snapshot diff.

## 2. System Context

### 2.1 Product Context

Archon accepts API-triggered extraction requests, loads submitted repositories and explicit solution paths, extracts deterministic architecture facts, persists them in Neo4j, and later exposes them through API and MCP surfaces. WP007 contributes the configuration and dependency-injection slice of the architecture graph.

The package must use the single extraction orchestration path created earlier in the sequence. It must not scan arbitrary directories independently of the submitted extraction request, bypass the snapshot contract, or persist data directly outside the established graph persistence adapter.

### 2.2 Source References

WP007 must align with these source materials:

- `docs/foundation/work-packages.md` WP007 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 17.3 for method-level dependencies on configuration keys and services.
- `docs/foundation/archon_full_concept_brief.md` section 17.4 for confidence treatment.
- `docs/foundation/archon_full_concept_brief.md` section 21 for dependency-injection extraction.
- `docs/foundation/archon_full_concept_brief.md` section 22 for modern and legacy configuration extraction.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.6.2, E.6.5, and E.7.3 for configuration, DI, runtime slice enablement, evidence, and metadata.
- `docs/foundation/work-packages.md` completion rules for evidence-backed facts, explicit unknowns, deterministic stable keys, Neo4j as the system of record, tests, documentation, and no Discovery UI.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms that DI and configuration extraction satisfy WP007 scope without UI delivery or deferred mandatory behavior. |
| Architect | Confirms service composition, container use, configuration usage, and evidence modeling are represented consistently in the graph. |
| Developer | Uses extracted facts to understand runtime wiring, service registration, configuration keys, options models, and legacy container usage. |
| Test engineer | Verifies registration detection, configuration detection, evidence quality, confidence, unknown handling, and extraction-pipeline integration. |
| Future API consumer | Depends on persisted facts being complete enough for query APIs in later work packages. |
| Future MCP consumer | Depends on evidence-backed DI/configuration facts for Copilot workflows in later work packages. |

## 3. Component Summary

### 3.1 Dependency Injection Extractor

The dependency-injection extractor detects modern Microsoft DI registrations, hosted-service registrations, `HttpClient` registrations, extension-method wrappers, legacy containers, service locators, manual factory patterns, and service-to-implementation mappings. It contributes service registration facts, constructor dependency correlations, lifetime metadata, registration-source metadata, confidence, evidence, warnings, and unknowns to the shared snapshot accumulator.

### 3.2 Configuration Extractor

The configuration extractor detects modern and legacy configuration files, configuration APIs, options binding, connection-string usage, configuration sections, configuration key references, provider details where detectable, and environment-specific file variants. It contributes `ConfigurationKey` nodes, configuration-use relationships, metadata, evidence, confidence, warnings, and unknowns to the shared snapshot accumulator.

### 3.3 Roslyn and File Artifact Integration

WP007 depends on Roslyn semantic outputs and repository file artifacts from earlier work packages. Roslyn is used for symbol-aware detection of registrations, method calls, options types, constructor parameters, and configuration APIs. File artifact inspection is used for JSON and XML configuration files, `.config` binding redirects, custom XML sections, and connection-string definitions.

### 3.4 Snapshot and Persistence Integration

WP007 must emit facts through the established extraction snapshot contract. The Neo4j persistence adapter remains the only persistence path. The extractors must not issue direct database writes and must not invent facts that cannot be tied to source evidence or represented as explicit unknowns.

## 4. Functional Requirements

### 4.1 Extraction Pipeline Participation

| ID | Requirement |
| --- | --- |
| FR-001 | WP007 shall register configuration and dependency-injection extractors with the existing extraction orchestration path. |
| FR-002 | WP007 extractors shall run only as part of an API-triggered extraction using a repository root directory and explicit solution path list. |
| FR-003 | WP007 extractors shall consume repository, solution, project, semantic symbol, and file artifact context produced by earlier extraction stages. |
| FR-004 | WP007 extractors shall contribute nodes, relationships, evidence, metadata, warnings, and errors to the shared snapshot accumulator. |
| FR-005 | WP007 extractors shall not persist directly to Neo4j, write sidecar extraction files, or introduce an alternate storage model. |
| FR-006 | WP007 output shall be snapshot-scoped and compatible with deterministic stable keys and fingerprints established by prior work packages. |

### 4.2 Modern Microsoft Dependency Injection Detection

| ID | Requirement |
| --- | --- |
| FR-007 | The extractor shall detect `IServiceCollection` registration calls. |
| FR-008 | The extractor shall detect `AddSingleton<TService, TImplementation>()`. |
| FR-009 | The extractor shall detect `AddSingleton<TService>()`. |
| FR-010 | The extractor shall detect `AddSingleton(typeof(TService), typeof(TImplementation))` where symbol resolution is available. |
| FR-011 | The extractor shall detect `AddScoped<TService, TImplementation>()`. |
| FR-012 | The extractor shall detect `AddScoped<TService>()`. |
| FR-013 | The extractor shall detect `AddScoped(typeof(TService), typeof(TImplementation))` where symbol resolution is available. |
| FR-014 | The extractor shall detect `AddTransient<TService, TImplementation>()`. |
| FR-015 | The extractor shall detect `AddTransient<TService>()`. |
| FR-016 | The extractor shall detect `AddTransient(typeof(TService), typeof(TImplementation))` where symbol resolution is available. |
| FR-017 | The extractor shall detect factory registrations and record the service type, lifetime, factory source location, and unresolved implementation unknown where the concrete implementation cannot be determined. |
| FR-018 | The extractor shall detect registrations made through `TryAdd`, `TryAddEnumerable`, `Replace`, and related Microsoft DI extension APIs where supported by symbol analysis. |
| FR-019 | The extractor shall detect registration sources in `Program.cs`, `Startup.cs`, host-builder setup code, module registration methods, and extension methods. |
| FR-020 | The extractor shall capture registration lifetime metadata using the source vocabulary `Singleton`, `Scoped`, `Transient`, or `Unknown`. |

### 4.3 Hosted Service and Worker Registration Detection

| ID | Requirement |
| --- | --- |
| FR-021 | The extractor shall detect `AddHostedService<T>()`. |
| FR-022 | The extractor shall detect registrations of implementations assignable to `IHostedService` where symbol resolution is available. |
| FR-023 | The extractor shall detect registrations of implementations derived from `BackgroundService` where symbol resolution is available. |
| FR-024 | The extractor shall create service registration facts for hosted services and preserve hosted-service-specific metadata. |
| FR-025 | The extractor shall emit relationships that allow later runtime extraction and query work packages to identify hosted service types and their constructor dependencies. |

### 4.4 `HttpClient` Registration Detection

| ID | Requirement |
| --- | --- |
| FR-026 | The extractor shall detect `AddHttpClient()` registrations. |
| FR-027 | The extractor shall detect named `HttpClient` registrations. |
| FR-028 | The extractor shall detect typed `HttpClient` registrations. |
| FR-029 | The extractor shall detect generated or interface-based clients registered through supported Microsoft `HttpClientFactory` patterns where symbol evidence exists. |
| FR-030 | The extractor shall capture client name, typed client type, implementation type, configuration delegate source, base-address configuration clues, and evidence where detectable. |
| FR-031 | The extractor shall represent unknown external target details explicitly when the client target cannot be resolved from code or configuration. |

### 4.5 Extension-Method and Wrapper Registration Detection

| ID | Requirement |
| --- | --- |
| FR-032 | The extractor shall detect extension methods that accept `IServiceCollection` and invoke registration APIs inside their body. |
| FR-033 | The extractor shall attribute registrations discovered inside wrapper extension methods to both the wrapper method and the inner registration call evidence. |
| FR-034 | The extractor shall detect wrapper methods invoked from host startup or module registration code. |
| FR-035 | The extractor shall preserve the wrapper invocation chain where practical so later consumers can explain how a service entered the container. |
| FR-036 | The extractor shall avoid inventing registrations for wrapper methods whose implementation cannot be analyzed. Such wrappers shall produce an explicit warning or unknown registration-source fact where appropriate. |

### 4.6 Legacy Container Detection

| ID | Requirement |
| --- | --- |
| FR-037 | The extractor shall detect Unity usage. |
| FR-038 | The extractor shall detect Autofac usage. |
| FR-039 | The extractor shall detect Castle Windsor usage. |
| FR-040 | The extractor shall detect StructureMap usage. |
| FR-041 | The extractor shall detect Ninject usage. |
| FR-042 | The extractor shall detect SimpleInjector usage. |
| FR-043 | The extractor shall detect CommonServiceLocator usage. |
| FR-044 | The extractor shall detect custom service-locator patterns where evidence exists, including static service resolver calls, global container access, and known project-local locator classes. |
| FR-045 | The extractor shall detect manual factory patterns where code creates service implementations behind an abstraction and uses them as service composition. |
| FR-046 | The extractor shall capture container name, registration method, service type, implementation type, lifetime where available, source location, and confidence. |
| FR-047 | The extractor shall represent unsupported or partially understood container registration forms as explicit unknowns with evidence and unknown reason. |

### 4.7 Service-to-Implementation and Constructor Dependency Mapping

| ID | Requirement |
| --- | --- |
| FR-048 | The extractor shall extract service-to-implementation mappings for all supported registration patterns. |
| FR-049 | The extractor shall connect registered implementation types to constructor dependencies already extracted through Roslyn where symbol identity is available. |
| FR-050 | The extractor shall emit `REGISTERED_AS_SERVICE` relationships from implementation type to service abstraction, or the repository-established equivalent direction if prior domain contracts define direction differently. |
| FR-051 | The extractor shall emit `INJECTS` relationships for constructor-injected dependencies where they are not already emitted by the semantic extraction slice. |
| FR-052 | The extractor shall emit or reuse `DEPENDS_ON` relationships when service registration or constructor usage proves dependency relationships. |
| FR-053 | The extractor shall deduplicate relationships when semantic extraction and DI extraction identify the same fact. |
| FR-054 | The extractor shall preserve evidence links for both registration sites and constructor dependency sites when both exist. |

### 4.8 Modern Configuration File Detection

| ID | Requirement |
| --- | --- |
| FR-055 | The extractor shall detect `appsettings.json`. |
| FR-056 | The extractor shall detect environment-specific `appsettings.*.json` files. |
| FR-057 | The extractor shall parse JSON configuration files to discover hierarchical configuration paths. |
| FR-058 | The extractor shall create `ConfigurationKey` nodes for detected configuration paths that are used or referenced by code. |
| FR-059 | The extractor shall capture configuration file evidence with file path, line span where available, key path, snippet hash, and snippet preview. |
| FR-060 | The extractor shall classify environment-specific configuration source metadata without requiring a specific environment to be active. |
| FR-061 | The extractor shall identify likely external endpoint configuration keys where names or values indicate URL, URI, endpoint, host, base address, queue, topic, storage, or service target semantics. |
| FR-062 | The extractor shall avoid exposing secret values in evidence, metadata, API-ready DTOs, warnings, or generated output. Secret-like values shall be redacted while preserving key names and evidence location. |

### 4.9 Modern Configuration API Usage Detection

| ID | Requirement |
| --- | --- |
| FR-063 | The extractor shall detect `IConfiguration` constructor injection and usage. |
| FR-064 | The extractor shall detect `IConfiguration.GetSection(...)`. |
| FR-065 | The extractor shall detect configuration indexer access such as `configuration["Key"]`. |
| FR-066 | The extractor shall detect `Bind(...)` usage. |
| FR-067 | The extractor shall detect `Get<T>()` usage on configuration sections where symbol resolution is available. |
| FR-068 | The extractor shall detect `Configure<TOptions>(...)`. |
| FR-069 | The extractor shall detect `IOptions<T>`, `IOptionsMonitor<T>`, and `IOptionsSnapshot<T>` injection and usage. |
| FR-070 | The extractor shall map options types to configuration sections where the binding source can be determined. |
| FR-071 | The extractor shall emit `USES_CONFIG` relationships from methods, types, options types, or projects to configuration keys according to the established graph model. |
| FR-072 | The extractor shall mark dynamically constructed configuration keys as lower confidence and preserve the available string-part evidence. |

### 4.10 Legacy Configuration Detection

| ID | Requirement |
| --- | --- |
| FR-073 | The extractor shall detect `app.config`. |
| FR-074 | The extractor shall detect `web.config`. |
| FR-075 | The extractor shall parse `appSettings` entries. |
| FR-076 | The extractor shall parse `connectionStrings` entries. |
| FR-077 | The extractor shall detect `ConfigurationManager.AppSettings` usage. |
| FR-078 | The extractor shall detect `ConfigurationManager.ConnectionStrings` usage. |
| FR-079 | The extractor shall detect custom XML configuration sections. |
| FR-080 | The extractor shall detect binding redirects. |
| FR-081 | The extractor shall detect machine-level configuration assumptions where code or config references them. |
| FR-082 | The extractor shall create configuration facts for legacy keys and connection-string names without storing secret values. |
| FR-083 | The extractor shall preserve XML file evidence with file path, element path, line span where available, snippet hash, and snippet preview. |

### 4.11 Configuration Key Normalization and Matching

| ID | Requirement |
| --- | --- |
| FR-084 | The extractor shall normalize hierarchical JSON keys using the repository-established configuration key format, such as colon-delimited paths where compatible with .NET configuration APIs. |
| FR-085 | The extractor shall normalize legacy `appSettings` keys without changing their semantic casing. |
| FR-086 | The extractor shall normalize connection-string names as configuration keys with metadata identifying them as connection strings. |
| FR-087 | The extractor shall match code-referenced keys to file-defined keys when exact path evidence exists. |
| FR-088 | The extractor shall represent code-referenced keys with no discovered file definition as explicit configuration-key facts with unknown source-provider metadata. |
| FR-089 | The extractor shall represent file-defined keys with no code reference only when prior graph contracts require full configuration inventory; otherwise it shall document and test the chosen behavior. |
| FR-090 | The extractor shall preserve enough metadata to distinguish key definition, key reference, section binding, and options binding facts. |

### 4.12 Graph Nodes and Relationships

| ID | Requirement |
| --- | --- |
| FR-091 | The extractor shall persist `ConfigurationKey` nodes through the snapshot contract. |
| FR-092 | The extractor shall reuse existing `Project`, `Type`, `Method`, `FilePath`, and related nodes rather than creating duplicate conceptual nodes. |
| FR-093 | The extractor shall persist `USES_CONFIG` relationships for configuration usage. |
| FR-094 | The extractor shall persist `REGISTERED_AS_SERVICE` relationships for service registrations. |
| FR-095 | The extractor shall persist `INJECTS` relationships for injection facts where appropriate. |
| FR-096 | The extractor shall persist `DEPENDS_ON` relationships for service and configuration dependencies where appropriate. |
| FR-097 | The extractor shall attach evidence to every non-derived DI and configuration fact. |
| FR-098 | The extractor shall store registration lifetime, registration source, container kind, configuration provider, configuration source file, section name, key category, and redaction metadata in metadata fields. |

### 4.13 Confidence, Unknowns, Warnings, and Errors

| ID | Requirement |
| --- | --- |
| FR-099 | The extractor shall assign high confidence to symbol-resolved registrations and exact configuration key references. |
| FR-100 | The extractor shall assign medium confidence to string-constant or wrapper-inferred facts that are strongly supported but not fully symbol-resolved. |
| FR-101 | The extractor shall assign low confidence to dynamic or heuristic detections such as constructed configuration keys or custom service-locator recognition. |
| FR-102 | The extractor shall represent unresolved implementation types as explicit unknowns with unknown reason. |
| FR-103 | The extractor shall represent unresolved configuration providers as explicit unknowns with unknown reason. |
| FR-104 | The extractor shall produce warnings for unsupported container APIs, unreadable configuration files, malformed configuration files, unresolvable wrapper methods, and redacted secret-like values where appropriate. |
| FR-105 | The extractor shall produce extraction errors only for failures that prevent the DI/configuration slice from completing for a project or solution. |
| FR-106 | The extractor shall not silently omit partially detectable DI or configuration facts when explicit unknown representation is possible. |

## 5. Non-Functional Requirements

### 5.1 Determinism and Evidence

| ID | Requirement |
| --- | --- |
| NFR-001 | Given the same repository content, solution paths, extraction settings, and dependency versions, WP007 shall produce deterministic DI and configuration facts. |
| NFR-002 | Stable keys and fingerprints for WP007 facts shall not depend on database IDs, absolute developer machine paths, or enumeration order. |
| NFR-003 | Every persisted architectural statement shall have evidence unless it is purely derived from persisted facts. |
| NFR-004 | Evidence shall preserve enough context for later API and MCP consumers to explain the fact without re-reading source files. |

### 5.2 Security and Secret Handling

| ID | Requirement |
| --- | --- |
| NFR-005 | Secret-like configuration values shall not be stored in metadata, evidence snippets, warnings, errors, logs, API-ready responses, or generated outputs. |
| NFR-006 | The extractor shall preserve key names and source locations while redacting values that look like passwords, connection-string secrets, tokens, API keys, certificates, private keys, or credentials. |
| NFR-007 | Redaction behavior shall be deterministic and tested. |
| NFR-008 | The extractor shall not execute analyzed repository code, run startup methods, invoke container configuration, or load untrusted application assemblies for execution. |

### 5.3 Performance and Scalability

| ID | Requirement |
| --- | --- |
| NFR-009 | The extractor shall avoid repeated semantic analysis of the same syntax tree or symbol where prior Roslyn context is available. |
| NFR-010 | The extractor shall use cancellation tokens from the extraction orchestration path. |
| NFR-011 | The extractor shall avoid unbounded recursion when following wrapper registration methods. |
| NFR-012 | The extractor shall define and test safeguards for large configuration files and deeply nested configuration structures. |
| NFR-013 | The extractor shall avoid holding full secret-bearing configuration documents in long-lived memory beyond the extraction scope. |

### 5.4 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-014 | C# code shall use block-scoped namespaces. |
| NFR-015 | C# code shall use Allman braces. |
| NFR-016 | C# files shall contain one public type per file. |
| NFR-017 | Private fields shall use underscore-prefixed naming. |
| NFR-018 | Executable entry points shall avoid top-level statements. |
| NFR-019 | `.csproj` files shall keep `PackageReference` entries in `ItemGroup` blocks that contain only package references. |
| NFR-020 | Internal and non-public types introduced for WP007 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand architecture or behavior. |

### 5.5 Testability

| ID | Requirement |
| --- | --- |
| NFR-021 | DI and configuration extraction logic shall be testable without starting the Aspire AppHost. |
| NFR-022 | DI and configuration extraction logic shall be testable using in-memory or fixture-based source repositories. |
| NFR-023 | Secret redaction, confidence assignment, stable-key behavior, evidence generation, and unknown handling shall be directly testable. |
| NFR-024 | Tests shall not require external service credentials. |

## 6. Technical Requirements

### 6.1 Target Runtime and Project Placement

WP007 implementation shall use the repository-approved .NET target and the project layout created by WP001. The expected primary production projects are:

| Project | Responsibility |
| --- | --- |
| `Archon.Extractors.DependencyInjection` | Dependency-injection registration, wrapper, legacy container, service locator, and service mapping extraction. |
| `Archon.Extractors.Configuration` | Modern and legacy configuration file/API, options binding, connection-string, and configuration-key extraction. |
| `Archon.Roslyn` and language-specific Roslyn projects | Shared semantic context, symbol resolution, invocation analysis, and evidence projection support. |
| `Archon.Application` | Shared extraction contracts, snapshot accumulation contracts, and orchestration interfaces. |
| `Archon.Api.Extraction` | Coordination of extractor execution through the established API-triggered extraction path. |

Expected corresponding test projects are:

| Test Project | Responsibility |
| --- | --- |
| `Archon.Extractors.DependencyInjection.Tests` | DI extraction behavior, legacy container detection, wrapper traversal, service mapping, lifetime metadata, and evidence. |
| `Archon.Extractors.Configuration.Tests` | Configuration file/API extraction, options binding, key normalization, connection strings, legacy config, redaction, and evidence. |
| `Archon.Api.Extraction.Tests` | Pipeline participation, orchestration integration, warning/error propagation, and snapshot accumulation behavior. |
| `Archon.Roslyn.Tests`, `Archon.Roslyn.CSharp.Tests`, `Archon.Roslyn.VisualBasic.Tests` | Any shared semantic helper behavior introduced specifically to support WP007. |

### 6.2 Dependency Direction

WP007 must preserve Onion Architecture dependency direction:

- Domain must not depend on application, extractors, infrastructure, or hosts.
- Application may define contracts and ports but must not depend on infrastructure or hosts.
- Extractors may depend on application and Roslyn abstractions according to existing solution direction.
- API extraction coordination may depend on extractor contracts but must not absorb extractor implementation details that belong in the extractor projects.
- Infrastructure and hosts must not become a dumping ground for DI/configuration domain logic.

### 6.3 Configuration Parsing

The implementation shall parse JSON and XML configuration artifacts as data files. It shall not instantiate target application configuration providers in a way that executes target application code or requires target runtime services.

Configuration parsing shall preserve:

- Source file path.
- Logical key or XML element path.
- Environment suffix when applicable.
- Key category such as app setting, connection string, options section, endpoint clue, queue clue, or unknown.
- Redaction status for values or snippets.
- Evidence line span where available.

### 6.4 Registration Analysis

Registration analysis shall use Roslyn semantic information where available. It shall recognize registration calls by symbol identity when possible and by syntax fallback only where symbol identity is not available. Fallback detections must carry lower confidence and explicit metadata identifying the detection mode.

Wrapper registration traversal shall have safeguards for:

- Recursion depth.
- Cycles between registration methods.
- Missing source.
- Partial compilation failures.
- Unsupported dynamic invocation.

### 6.5 Secret Redaction

Secret redaction shall apply before snippets, metadata, warnings, or errors are added to the snapshot output. The redaction policy shall cover, at minimum:

- Password-like key names.
- Connection strings with credentials.
- API keys.
- Tokens.
- Shared access signatures.
- Private keys.
- Certificates.
- Client secrets.

Redaction shall preserve diagnostic usefulness by keeping the key name, source file, line span, and value-shape metadata where safe.

### 6.6 Documentation Pass

WP007 shall include a documentation pass covering:

- Supported DI registration patterns.
- Supported legacy containers and service-locator patterns.
- Supported modern configuration patterns.
- Supported legacy configuration patterns.
- Secret redaction behavior.
- Confidence and unknown-state behavior.
- Testing and fixture guidance for DI/configuration extraction.
- Limitations and known unsupported patterns, expressed as current implementation constraints rather than deferred mandatory requirements.

Internal and non-public implementation types introduced for WP007 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand the architecture or behavior.

## 7. Exclusions

WP007 shall not implement:

- Archon Discovery UI host, pages, components, assets, or tests for UI behavior.
- API query endpoints for browsing DI or configuration facts; those belong to the query API work package.
- MCP tools, MCP resources, MCP prompts, or Copilot workflows.
- Rule catalog evaluation, hotlist generation, finding suppression, or rule management.
- Data-access extraction beyond connection-string and configuration-key facts.
- External integration extraction beyond `HttpClient` registration and configuration clues explicitly required by WP007.
- Runtime endpoint, controller, worker, queue consumer, or scheduled-job extraction beyond hosted-service registration facts explicitly required by WP007.
- Markdown export.
- Snapshot diff.
- Direct Neo4j writes from extractor projects.
- Execution of analyzed repository startup code or container-building code.

## 8. Data and Integration Requirements

### 8.1 Required Graph Facts

WP007 shall contribute graph facts that fit the existing Archon graph model:

| Fact Type | Required Treatment |
| --- | --- |
| Configuration key | Represent as `ConfigurationKey` nodes with stable keys, metadata, evidence, confidence, and redaction status. |
| Configuration usage | Represent as `USES_CONFIG` relationships from relevant project, type, method, options type, or service facts. |
| Service registration | Represent as `REGISTERED_AS_SERVICE` relationships with lifetime and registration-source metadata. |
| Constructor injection | Represent or reuse `INJECTS` relationships for constructor dependency facts. |
| Service dependency | Represent or reuse `DEPENDS_ON` relationships where registration and semantic evidence support dependency. |
| Evidence | Link file, symbol, call-site, config-file, line-span, snippet hash, snippet preview, confidence, and redaction metadata. |
| Unknown | Represent unresolved service implementation, dynamic configuration key, unknown provider, unsupported container pattern, or unresolved wrapper with explicit unknown reason. |

### 8.2 Metadata Requirements

WP007 metadata shall support later API and MCP consumption. Metadata shall include, where available:

- DI container kind.
- Registration lifetime.
- Registration source method.
- Registration source file.
- Wrapper method chain.
- Service type identity.
- Implementation type identity.
- Factory registration indicator.
- Hosted-service indicator.
- `HttpClient` client kind.
- Named `HttpClient` name.
- Configuration provider kind.
- Configuration source file.
- Environment-specific configuration suffix.
- Configuration key category.
- Options type identity.
- Connection-string indicator.
- Redaction indicator.
- Detection mode.
- Confidence reason.
- Unknown reason.

### 8.3 Evidence Requirements

Evidence shall include enough information for later API and MCP consumers to show why the fact exists:

- Repository-relative file path.
- Line and column span where available.
- Symbol name where available.
- Containing symbol where available.
- Configuration key path or XML element path where relevant.
- Snippet hash.
- Snippet preview with secrets redacted.
- Detection mode.
- Confidence.

### 8.4 Integration with Later Work Packages

WP007 output shall be shaped so later work packages can:

- Query configuration usage by project, type, method, key, section, options type, and source file.
- Query service registrations by service type, implementation type, lifetime, container, project, and registration source.
- Explain change impact from configuration keys and service registrations.
- Feed rule evaluation and hotlist findings for legacy containers, service locators, secret risks, missing configuration, and risky dependency patterns.
- Expose evidence-backed configuration and DI facts through MCP tools and resources.
- Include configuration and service-composition facts in generated markdown.

## 9. Test Requirements

### 9.1 Required Test Coverage

| Test Area | Required Verification |
| --- | --- |
| Pipeline integration | DI and configuration extractors run through the existing extraction orchestration path and emit snapshot facts. |
| Microsoft DI registrations | Singleton, scoped, transient, factory, `TryAdd`, hosted-service, and `HttpClient` patterns are detected. |
| Wrapper registrations | Extension-method wrappers and invocation chains are detected with evidence and recursion safeguards. |
| Legacy containers | Unity, Autofac, Castle Windsor, StructureMap, Ninject, SimpleInjector, CommonServiceLocator, custom locators, and manual factories are detected or explicitly represented as unknown where partial. |
| Service mapping | Service-to-implementation mappings, lifetimes, registration sources, and constructor dependencies are emitted correctly. |
| Modern config files | `appsettings.json` and `appsettings.*.json` keys are parsed, normalized, and linked to evidence. |
| Modern config APIs | `IConfiguration`, `GetSection`, indexer access, `Bind`, `Get<T>`, `Configure<TOptions>`, and options injection are detected. |
| Legacy config files | `app.config`, `web.config`, `appSettings`, `connectionStrings`, custom sections, binding redirects, and machine-level assumptions are detected. |
| Legacy config APIs | `ConfigurationManager.AppSettings` and `ConfigurationManager.ConnectionStrings` usage are detected. |
| Key normalization | JSON paths, app setting keys, connection-string names, definitions, and references normalize deterministically. |
| Secret redaction | Secret-like values are redacted from evidence, metadata, warnings, errors, and logs. |
| Confidence | High, medium, and low confidence cases are assigned consistently. |
| Unknowns | Dynamic keys, unresolved implementations, unsupported containers, unknown providers, and unresolvable wrappers produce explicit unknowns. |
| Deduplication | Duplicate facts from multiple detection paths do not create duplicate graph facts. |
| Evidence | Every non-derived fact has source evidence with file path, line span where available, snippet hash, and redacted preview. |
| C# support | C# DI and configuration usage patterns are covered. |
| VB.NET support | VB.NET configuration usage and DI/container patterns are covered where Roslyn supports semantic detection. |

### 9.2 Test Fixtures

Tests shall include fixture repositories or in-memory source sets for:

- Minimal ASP.NET Core host with direct service registrations.
- Startup class with registration methods.
- Module-style extension method wrappers.
- Worker service with hosted-service registration.
- Typed and named `HttpClient` registrations.
- Multiple legacy container examples.
- Custom service locator example.
- Modern JSON configuration with nested sections and environment variants.
- Options binding and options injection examples.
- Legacy `.config` app settings, connection strings, binding redirects, and custom sections.
- Dynamic configuration key examples.
- Secret-like value redaction examples.
- Mixed C# and VB.NET solution examples where feasible.

### 9.3 Test Constraints

Automated verification must not start the Aspire AppHost as a blocking process. Tests should use extractor-level fixtures, application-layer orchestration seams, and targeted integration tests. For this work package, the full test suite should not be run unless explicitly requested; run targeted WP007 tests and a solution build as final validation.

## 10. Acceptance Criteria

WP007 is accepted when all of the following are true:

1. Configuration and dependency-injection extractors are wired into the existing extraction orchestration path.
2. Microsoft DI registrations for singleton, scoped, transient, factory, hosted-service, and `HttpClient` patterns are extracted.
3. Wrapper extension methods that register services are detected with invocation-chain evidence where available.
4. Unity, Autofac, Castle Windsor, StructureMap, Ninject, SimpleInjector, CommonServiceLocator, custom service locators, and manual factories are detected or represented as explicit unknowns where partially understood.
5. Service-to-implementation mappings, registration lifetimes, constructor dependencies, and registration sources are represented with evidence.
6. `appsettings.json` and environment-specific appsettings files are detected and parsed.
7. `IConfiguration`, options binding, `GetSection`, indexer access, `Bind`, `Get<T>`, `Configure<TOptions>`, and options injection usage are detected.
8. `app.config`, `web.config`, `ConfigurationManager.AppSettings`, `ConfigurationManager.ConnectionStrings`, custom XML sections, binding redirects, and machine-level configuration assumptions are detected.
9. `ConfigurationKey` nodes, `USES_CONFIG`, `REGISTERED_AS_SERVICE`, `INJECTS`, and `DEPENDS_ON` relationships are emitted through the snapshot contract.
10. Registration lifetime and configuration-provider details are stored in metadata.
11. Secret-like values are redacted before being stored or exposed in evidence, metadata, warnings, errors, or logs.
12. Unknowns and confidence are explicit for unresolved or inferred facts.
13. Tests cover options binding, connection strings, wrapper registrations, hosted services, legacy containers, service locators, configuration evidence, secret redaction, and unknown handling.
14. Documentation is updated for supported DI/configuration extraction behavior and validation.
15. No Archon Discovery UI implementation is introduced.
16. The solution builds successfully.
17. Targeted WP007 tests pass.

## 11. Risks and Decisions

### 11.1 Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Registration wrapper methods may hide service composition. | Extracted service graph may miss important dependencies. | Analyze extension methods accepting `IServiceCollection`, preserve wrapper chains, and emit warnings for unresolvable wrappers. |
| Legacy containers have broad and version-specific APIs. | Detection may be incomplete or overconfident. | Use symbol-aware detection where possible, confidence levels, metadata, and explicit unknowns for partial understanding. |
| Configuration values may contain secrets. | Persisted evidence could expose credentials. | Redact secret-like values before evidence, metadata, warning, error, or log emission. |
| Dynamic configuration keys cannot always be resolved. | False precision could mislead API and MCP consumers. | Use low confidence and explicit unknown reasons for dynamic key cases. |
| Multiple extraction slices may emit overlapping dependency facts. | Duplicate graph relationships could reduce query quality. | Deduplicate by stable key and fingerprint through the snapshot accumulator. |
| VB.NET support may differ from C# for modern DI idioms. | Mixed-language estates may have uneven coverage. | Use Roslyn Visual Basic semantic support where available and document/test supported parity. |
| File parsing line spans may be hard for generated or malformed config files. | Evidence may lack exact location. | Preserve file-level evidence, warnings, and unknowns when precise line spans are unavailable. |

### 11.2 Decisions

| Decision | Rationale |
| --- | --- |
| Use a single WP007 specification document. | User requested a single markdown document spec for WP007. |
| Create the documentation under `docs/007-Configuration-and-Dependency-Injection-Extraction/`. | This is the next incremental documentation work-package folder after WP006. |
| Do not create separate overview and component spec documents. | The user explicitly requested a single markdown document, overriding the multi-document collaboration pattern for this output. |
| Treat DI and configuration as extraction slices, not persistence services. | The work-package sequence requires extractors to contribute through the snapshot contract and keep Neo4j as the system of record. |
| Redact secret-like values at extraction time. | Prevents secrets from entering evidence, metadata, logs, API outputs, MCP outputs, or markdown exports later. |
| Preserve explicit unknowns rather than suppressing partial facts. | The source brief requires unknowns to be represented instead of omitted or invented. |

## 12. Manual Verification Requirements

The implementation documentation for WP007 shall instruct a developer to verify the work package by:

1. Restoring and building the solution.
2. Running targeted tests for dependency-injection extraction.
3. Running targeted tests for configuration extraction.
4. Running targeted extraction integration tests through the API extraction module seam without launching the blocking Aspire AppHost process.
5. Inspecting representative snapshot output to confirm `ConfigurationKey`, `USES_CONFIG`, `REGISTERED_AS_SERVICE`, `INJECTS`, and `DEPENDS_ON` facts are emitted.
6. Confirming evidence includes redacted snippets and source locations.
7. Confirming secret-like configuration values are not present in test output, logs, warnings, errors, metadata, or evidence previews.
8. Confirming no Archon Discovery UI resource, page, component, or front-end asset was created.

Automated validation instructions shall explicitly state not to run the AppHost as a blocking process during agent-driven verification.

## 13. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| Detect `IServiceCollection` registrations | Sections 4.2, 9, 10 |
| Detect hosted-service registrations | Sections 4.3, 9, 10 |
| Detect `HttpClient` registrations | Sections 4.4, 9, 10 |
| Detect wrapper extension methods | Sections 4.5, 6.4, 9, 10 |
| Detect legacy containers and service locators | Sections 4.6, 9, 10 |
| Extract service-to-implementation mappings, lifetimes, constructor dependencies, and registration sources | Sections 4.7, 8.2, 9, 10 |
| Detect `appsettings.json`, environment-specific files, `IConfiguration`, options binding, `GetSection`, indexer access, `Bind`, and `Configure<TOptions>` | Sections 4.8, 4.9, 9, 10 |
| Detect `app.config`, `web.config`, `ConfigurationManager.AppSettings`, `ConfigurationManager.ConnectionStrings`, custom XML sections, binding redirects, and machine-level assumptions | Sections 4.10, 9, 10 |
| Persist `ConfigurationKey`, `USES_CONFIG`, `REGISTERED_AS_SERVICE`, `INJECTS`, and `DEPENDS_ON` facts | Sections 4.12, 8.1, 10 |
| Store lifetime and configuration-provider details in metadata | Sections 4.12, 8.2, 10 |
| Evidence-backed facts | Sections 4.13, 5.1, 8.3, 9, 10 |
| Confidence and unknown handling | Sections 4.13, 8.1, 9, 10 |
| Secret redaction | Sections 4.8, 5.2, 6.5, 9, 10 |
| Tests cover options binding, connection strings, wrapper registrations, hosted services, legacy containers, and configuration evidence | Sections 9, 10 |
| Repository documentation updated | Sections 6.6, 12, 10 |
| No Discovery UI implementation | Sections 1.3, 7, 10, 12 |

## 14. Open Questions

No blocking open questions are known for producing the WP007 specification. Implementation may still need to confirm exact stable-key formats, graph relationship direction, and metadata field names from the completed WP002 domain contracts before code work begins.

## 15. Change Log

| Date | Change |
| --- | --- |
| 2026-05-21 | Created initial single-document WP007 specification from `docs/foundation/work-packages.md` and the Archon source brief. |
