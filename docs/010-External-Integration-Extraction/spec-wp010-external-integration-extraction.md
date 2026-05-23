# WP010 Specification - External Integration Extraction

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP010 - External Integration Extraction |
| Output Path | `docs/010-External-Integration-Extraction/spec-wp010-external-integration-extraction.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP010 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP010, the Archon work package that extracts external integration architecture facts from analyzed .NET repositories. The package identifies outbound service calls, messaging integrations, storage integrations, email integrations, payment-provider usage, and internal service API dependencies.

WP010 builds on the prior repository, project, package, semantic symbol, configuration, dependency-injection, runtime, data-access, snapshot orchestration, and Neo4j persistence foundations. It must contribute evidence-backed integration nodes, relationships, metadata, confidence, warnings, and unknowns through the established extraction pipeline rather than introducing a separate persistence or query path.

### 1.2 Background

Archon provides deterministic, evidence-backed architecture intelligence for modern and legacy .NET estates. External integrations are central to impact analysis because service endpoints, queues, topics, storage accounts, email channels, payment gateways, and internal API calls often define hidden runtime coupling across systems.

The controlling work-package sequence is API-first and MCP-first. WP010 therefore focuses on backend extraction and graph population only. Human-facing integration explorers, service maps, queue dashboards, storage browsers, payment-provider dashboards, and other Archon Discovery UI surfaces remain excluded.

### 1.3 High-Level Scope

WP010 covers these external integration extraction areas:

- `HttpClient`, `IHttpClientFactory`, typed HTTP clients, and named HTTP clients.
- RestSharp and common REST client usage.
- WCF clients and service references.
- SOAP clients, ASMX references, and generated proxy usage.
- gRPC clients and channel/client construction.
- Message queues and topics, including Azure Service Bus, NServiceBus, RabbitMQ, MSMQ, and framework-neutral queue abstractions.
- Storage clients, including blob/file storage and SDK-based storage dependencies.
- SMTP/email client usage.
- Payment-provider SDK and API client usage.
- Internal service API clients and service-to-service call patterns.
- Base URL, endpoint, queue/topic, storage, credential, and authentication-hint configuration correlation.
- Integration node, relationship, metadata, evidence, confidence, warning, and unknown emission.
- Tests for all production behavior introduced by this work package.
- Documentation updates explaining supported external integration extraction behavior and validation.

WP010 excludes Archon Discovery UI, API query product surface expansion, MCP tools, rule-engine evaluation, hotlist generation, markdown export, snapshot diff, executing external calls, validating external credentials, connecting to brokers or storage accounts, and directly persisting extractor results outside the established snapshot contract.

## 2. System Context

### 2.1 Product Context

Archon accepts API-triggered extraction requests, loads submitted repositories and explicit solution paths, extracts deterministic architecture facts, persists them in Neo4j, and later exposes them through API and MCP surfaces. WP010 contributes the external integration slice of the architecture graph.

The package must use the single extraction orchestration path created earlier in the sequence. It must not scan arbitrary directories independently of the submitted extraction request, bypass the snapshot contract, execute analyzed repository code, call external services, connect to queues or storage services, or persist data directly outside the established graph persistence adapter.

### 2.2 Source References

WP010 must align with these source materials:

- `docs/foundation/work-packages.md` WP010 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 17.3 for method-level external-service dependencies.
- `docs/foundation/archon_full_concept_brief.md` section 20 for workers, queue consumers, topics, and external integrations.
- `docs/foundation/archon_full_concept_brief.md` section 21 for `HttpClient` dependency-injection registrations.
- `docs/foundation/archon_full_concept_brief.md` section 24 for external integration extraction.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.6.7 for external integration nodes, edges, and metadata.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.7.4 for data access and integration slice enablement.
- `docs/foundation/work-packages.md` completion rules for evidence-backed facts, explicit unknowns, deterministic stable keys, Neo4j as the system of record, tests, documentation, and no Discovery UI.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms external integration extraction satisfies WP010 scope without UI delivery or deferred mandatory behavior. |
| Architect | Confirms service calls, clients, queues, topics, storage integrations, email flows, and provider dependencies are represented consistently in the graph. |
| Developer | Uses extracted facts to understand service dependencies, integration risk, queue consumers/producers, configuration keys, and change impact. |
| Test engineer | Verifies detection coverage, evidence quality, confidence, unknown handling, redaction, and extraction-pipeline integration. |
| Future API consumer | Depends on persisted integration facts being complete enough for query APIs in later work packages. |
| Future MCP consumer | Depends on evidence-backed integration facts for impact analysis and Copilot workflows in later work packages. |

## 3. Component Summary

### 3.1 HTTP Client Integration Extractor

The HTTP client integration extractor detects direct `HttpClient` construction, injected `HttpClient`, `IHttpClientFactory`, named clients, typed clients, generated API clients built on HTTP, base-address assignments, request-message construction, common HTTP method invocations, and endpoint configuration key usage. It contributes `ExternalService` facts and `CALLS_EXTERNAL_SERVICE`, `USES_CONFIG`, and `DEPENDS_ON` relationships where evidence exists.

### 3.2 REST Client Extractor

The REST client extractor detects RestSharp and similar REST client abstractions where deterministic package, namespace, type, or invocation evidence exists. It captures client type, base URL source, request resource/path hints, authentication hints, usage sites, and explicit unknowns for computed endpoints.

### 3.3 WCF, SOAP, and Generated Proxy Extractor

The WCF and SOAP extractor detects service references, connected-service artifacts, generated proxy classes, `ClientBase<T>`, channel factories, binding configuration, endpoint addresses, ASMX-style service references, SOAP client usage, and related configuration. It captures legacy integration facts without executing generated proxies or contacting remote services.

### 3.4 gRPC Client Extractor

The gRPC extractor detects gRPC client package references, generated client types, `GrpcChannel`, channel creation, typed clients, service contract usage, endpoint configuration, and call sites. It contributes service dependency facts with transport metadata and confidence based on symbol or generated artifact evidence.

### 3.5 Messaging Integration Extractor

The messaging extractor detects queue and topic producers and consumers across Azure Service Bus, NServiceBus, RabbitMQ, MSMQ, and common abstraction libraries. It uses worker/runtime facts from earlier work packages where available to correlate message handlers, sagas, hosted services, background services, endpoint names, and queue/topic names.

### 3.6 Storage Integration Extractor

The storage extractor detects SDK and framework usage for blob storage, file storage, and similar external storage clients. It captures client type, storage service category, configuration keys, container/share/blob/file name hints where deterministic, usage sites, and explicit unknowns for runtime-computed names.

### 3.7 Email and Payment Provider Extractor

The email and payment provider extractor detects SMTP clients, mail message construction, common email sender abstractions, and known payment-provider SDK or HTTP client usage. It captures provider hints, configuration keys, authentication hints, usage sites, and redacted evidence without validating provider credentials.

### 3.8 Integration Evidence and Graph Integration

WP010 uses Roslyn semantic outputs, repository file artifacts, project/package facts, configuration facts, dependency-injection facts, runtime facts, and data-access facts from earlier work packages. It must emit facts through the established extraction snapshot contract. The Neo4j persistence adapter remains the only persistence path, and extractors must not invent facts that cannot be tied to evidence or represented as explicit unknowns.

## 4. Functional Requirements

### 4.1 Extraction Pipeline Participation

| ID | Requirement |
| --- | --- |
| FR-001 | WP010 shall register external integration extractors with the existing extraction orchestration path. |
| FR-002 | WP010 extractors shall run only as part of an API-triggered extraction using a repository root directory and explicit solution path list. |
| FR-003 | WP010 extractors shall consume repository, solution, project, package, semantic symbol, configuration, dependency-injection, runtime, and file artifact context produced by earlier extraction stages. |
| FR-004 | WP010 extractors shall contribute nodes, relationships, evidence, metadata, warnings, and errors to the shared snapshot accumulator. |
| FR-005 | WP010 extractors shall not persist directly to Neo4j, write sidecar extraction files, execute analyzed code, connect to external systems, send messages, read remote storage, send email, or call payment providers. |
| FR-006 | WP010 output shall be snapshot-scoped and compatible with deterministic stable keys and fingerprints established by prior work packages. |

### 4.2 HTTP Client Extraction

| ID | Requirement |
| --- | --- |
| FR-007 | The extractor shall detect direct `System.Net.Http.HttpClient` construction. |
| FR-008 | The extractor shall detect injected `HttpClient` dependencies. |
| FR-009 | The extractor shall detect `IHttpClientFactory` usage. |
| FR-010 | The extractor shall detect named HTTP clients registered through `AddHttpClient` overloads. |
| FR-011 | The extractor shall detect typed HTTP clients registered through `AddHttpClient<TClient>` and related overloads. |
| FR-012 | The extractor shall detect base-address assignments and initialization where statically available. |
| FR-013 | The extractor shall detect HTTP method invocations including `GetAsync`, `PostAsync`, `PutAsync`, `PatchAsync`, `DeleteAsync`, `SendAsync`, `GetFromJsonAsync`, `PostAsJsonAsync`, and related variants where symbol evidence exists. |
| FR-014 | The extractor shall detect `HttpRequestMessage` construction and usage. |
| FR-015 | The extractor shall extract base URL configuration keys where present. |
| FR-016 | The extractor shall extract relative path, route, or request resource hints where statically available. |
| FR-017 | The extractor shall link HTTP usage sites to owning projects, types, methods, typed clients, named clients, configuration keys, and external service nodes where evidence supports the link. |
| FR-018 | The extractor shall represent computed URLs, unresolved client names, environment-supplied endpoints, and ambiguous services as explicit unknowns rather than inventing service names. |

### 4.3 RestSharp and REST Client Extraction

| ID | Requirement |
| --- | --- |
| FR-019 | The extractor shall detect RestSharp package references, namespaces, client types, and request types. |
| FR-020 | The extractor shall detect `RestClient` construction and configuration where symbol or syntax evidence exists. |
| FR-021 | The extractor shall detect RestSharp request creation, resource/path assignment, method selection, and execute calls where available. |
| FR-022 | The extractor shall detect base URL and API endpoint configuration keys used by REST client setup. |
| FR-023 | The extractor shall capture authentication hints such as bearer token, basic authentication, API key, certificate, OAuth, or custom header usage without storing secret values. |
| FR-024 | The extractor shall detect other common REST client abstractions where package, namespace, type, or invocation evidence can be identified deterministically. |
| FR-025 | The extractor shall represent unresolved REST resources, dynamic paths, dynamic headers, and unknown authentication details as explicit unknowns. |

### 4.4 WCF and SOAP Extraction

| ID | Requirement |
| --- | --- |
| FR-026 | The extractor shall detect WCF client usage from `System.ServiceModel` packages, namespaces, base types, and generated service-reference artifacts. |
| FR-027 | The extractor shall detect generated WCF proxy classes. |
| FR-028 | The extractor shall detect `ClientBase<T>` derived clients. |
| FR-029 | The extractor shall detect `ChannelFactory<T>` usage. |
| FR-030 | The extractor shall detect WCF endpoint configuration names, binding names, and endpoint addresses where present in source or configuration artifacts. |
| FR-031 | The extractor shall detect SOAP client usage from generated proxies, service references, connected-service artifacts, ASMX references, or package evidence where deterministic. |
| FR-032 | The extractor shall extract service contract names, operation call sites, and owning project/method context where available. |
| FR-033 | The extractor shall capture transport and binding metadata such as HTTP, HTTPS, TCP, named pipes, basicHttpBinding, wsHttpBinding, netTcpBinding, or custom binding where available. |
| FR-034 | The extractor shall represent unresolved endpoints, generated proxy ambiguity, configuration-driven addresses, and dynamic channel creation as explicit unknowns. |

### 4.5 gRPC Extraction

| ID | Requirement |
| --- | --- |
| FR-035 | The extractor shall detect gRPC package references and namespaces. |
| FR-036 | The extractor shall detect generated gRPC client types where generated source artifacts or symbols are available. |
| FR-037 | The extractor shall detect `GrpcChannel` creation and configuration. |
| FR-038 | The extractor shall detect typed gRPC clients registered in dependency injection where available. |
| FR-039 | The extractor shall detect gRPC method call sites where generated client method symbols or invocation patterns are available. |
| FR-040 | The extractor shall extract gRPC endpoint configuration keys and address hints where present. |
| FR-041 | The extractor shall represent unresolved generated clients, runtime-computed channels, and ambiguous gRPC endpoints as explicit unknowns. |

### 4.6 Messaging Integration Extraction

| ID | Requirement |
| --- | --- |
| FR-042 | The extractor shall detect message queue and topic integration usage from package references, namespaces, type symbols, configuration keys, and invocation patterns. |
| FR-043 | The extractor shall detect Azure Service Bus clients, senders, receivers, processors, queue names, topic names, subscription names, and message handlers where evidence exists. |
| FR-044 | The extractor shall detect NServiceBus endpoint configuration, message handlers, sagas, send operations, publish operations, subscribe operations, routing configuration, transport configuration, endpoint names, queue names, error queue hints, and recoverability configuration where evidence exists. |
| FR-045 | The extractor shall detect RabbitMQ connections, channels, exchanges, queues, routing keys, publishers, consumers, and handlers where evidence exists. |
| FR-046 | The extractor shall detect MSMQ usage including queue paths, senders, receivers, and handlers where evidence exists. |
| FR-047 | The extractor shall detect common queue abstraction libraries and framework wrappers when deterministic package, namespace, type, or invocation evidence exists. |
| FR-048 | The extractor shall distinguish producer, consumer, handler, saga, sender, publisher, subscriber, and receiver roles where evidence supports classification. |
| FR-049 | The extractor shall correlate queue consumers, message handlers, sagas, and endpoint configuration with hosted services, background services, worker projects, and runtime facts produced by earlier work packages where available. |
| FR-050 | The extractor shall emit `Queue` and `Topic` nodes for queue/topic facts where names are deterministically available. |
| FR-051 | The extractor shall represent unresolved endpoint names, message type names, queue/topic names, subscription names, routing keys, and computed broker addresses as explicit unknowns. |

### 4.7 Storage Integration Extraction

| ID | Requirement |
| --- | --- |
| FR-052 | The extractor shall detect storage client package references, namespaces, types, and invocation patterns. |
| FR-053 | The extractor shall detect Azure Blob Storage client usage where package, namespace, type, or invocation evidence exists. |
| FR-054 | The extractor shall detect Azure File Storage client usage where package, namespace, type, or invocation evidence exists. |
| FR-055 | The extractor shall detect generic external storage abstractions where deterministic package, namespace, type, or invocation evidence exists. |
| FR-056 | The extractor shall capture storage account, container, share, blob, file, bucket, or path configuration keys where available without storing secret values. |
| FR-057 | The extractor shall classify read/write/delete hints for storage usage where API calls provide deterministic evidence. |
| FR-058 | The extractor shall represent unresolved storage targets, runtime-computed container names, and ambiguous storage providers as explicit unknowns. |

### 4.8 SMTP and Email Extraction

| ID | Requirement |
| --- | --- |
| FR-059 | The extractor shall detect `SmtpClient` usage. |
| FR-060 | The extractor shall detect mail message construction and send operations where symbol or syntax evidence exists. |
| FR-061 | The extractor shall detect common email sender abstractions and framework integrations where deterministic package, namespace, type, or invocation evidence exists. |
| FR-062 | The extractor shall capture SMTP host, port, sender, credential, API provider, and template configuration keys where available without storing secret values. |
| FR-063 | The extractor shall classify email integrations as external service dependencies with client type and transport metadata. |
| FR-064 | The extractor shall represent unresolved SMTP hosts, runtime-computed recipients, template names, and provider details as explicit unknowns. |

### 4.9 Payment Provider Extraction

| ID | Requirement |
| --- | --- |
| FR-065 | The extractor shall detect known payment-provider SDK usage where package, namespace, type, or invocation evidence exists. |
| FR-066 | The extractor shall detect payment-provider HTTP client wrappers where deterministic naming, package, configuration, or call-site evidence exists. |
| FR-067 | The extractor shall capture provider name hints, API endpoint configuration keys, authentication configuration keys, and usage sites where available. |
| FR-068 | The extractor shall not store card data, payment tokens, API secrets, customer payment identifiers, or secret-bearing request payload values in evidence, metadata, warnings, errors, or logs. |
| FR-069 | The extractor shall represent ambiguous provider names, dynamically loaded payment integrations, and unresolved payment endpoints as explicit unknowns. |

### 4.10 Internal Service API Extraction

| ID | Requirement |
| --- | --- |
| FR-070 | The extractor shall detect internal API client patterns where service boundaries can be inferred from typed clients, generated clients, configuration keys, base URLs, package names, project names, namespaces, or endpoint references. |
| FR-071 | The extractor shall detect calls from one analyzed project to an endpoint exposed by another analyzed project where prior runtime facts and deterministic URL/route evidence support correlation. |
| FR-072 | The extractor shall link internal service calls to `Endpoint`, `Controller`, `Method`, `Project`, and `ExternalService` facts where deterministic evidence supports the link. |
| FR-073 | The extractor shall preserve internal/external classification metadata and confidence. |
| FR-074 | The extractor shall represent unknown service ownership or unresolved internal route targets as explicit unknowns rather than forcing an internal-service match. |

### 4.11 Graph Nodes and Relationships

| ID | Requirement |
| --- | --- |
| FR-075 | The extractor shall emit `ExternalService` nodes through the snapshot contract for external service and service API facts. |
| FR-076 | The extractor shall emit `Queue` nodes through the snapshot contract for queue facts. |
| FR-077 | The extractor shall emit `Topic` nodes through the snapshot contract for topic facts. |
| FR-078 | The extractor shall reuse existing `Project`, `Type`, `Method`, `Endpoint`, `Controller`, `HostedService`, `ConfigurationKey`, `FilePath`, and related nodes rather than creating duplicate conceptual nodes. |
| FR-079 | The extractor shall emit `CALLS_EXTERNAL_SERVICE` relationships from projects, types, methods, typed clients, endpoints, workers, handlers, or services to external service nodes where evidence exists. |
| FR-080 | The extractor shall emit `HANDLES` relationships for queue/topic consumers, processors, subscriptions, message handlers, sagas, hosted services, background services, and worker methods where evidence exists. |
| FR-081 | The extractor shall emit `USES_CONFIG` relationships for integration facts that depend on configuration keys where evidence exists. |
| FR-082 | The extractor shall emit `DEPENDS_ON` relationships for integration-related dependencies that are not better represented by a more specific relationship where evidence exists. |
| FR-083 | The extractor shall attach evidence to every non-derived integration fact. |
| FR-084 | The extractor shall store transport, provider, client type, base URL key, authentication hints, endpoint names, message type names, queue/topic names, storage target hints, email hints, payment hints, direction, role, and unknown metadata in metadata fields where available. |

### 4.12 Confidence, Unknowns, Warnings, and Errors

| ID | Requirement |
| --- | --- |
| FR-085 | The extractor shall assign high confidence to symbol-resolved integration facts, exact configuration-key references, generated proxy symbols, and explicit package/type matches. |
| FR-086 | The extractor shall assign medium confidence to strongly supported syntax, naming, configuration, or file-pattern detections that are not fully symbol-resolved. |
| FR-087 | The extractor shall assign low confidence to heuristic provider classification, inferred internal service ownership, naming-only payment-provider detection, and partially dynamic endpoint detection. |
| FR-088 | The extractor shall represent unresolved service names, computed URLs, computed endpoint names, computed message type names, computed queue/topic names, computed storage targets, ambiguous authentication mechanisms, unresolved generated clients, and unknown provider ownership as explicit unknowns with unknown reason. |
| FR-089 | The extractor shall produce warnings for unreadable service-reference artifacts, malformed configuration artifacts, unsupported integration frameworks, unresolvable generated proxies, unsupported messaging abstractions, unsupported storage SDKs, and partial compilation failures that affect integration extraction. |
| FR-090 | The extractor shall produce extraction errors only for failures that prevent the integration slice from completing for a project or solution. |
| FR-091 | The extractor shall not silently omit partially detectable integration facts when explicit unknown representation is possible. |

## 5. Non-Functional Requirements

### 5.1 Determinism and Evidence

| ID | Requirement |
| --- | --- |
| NFR-001 | Given the same repository content, solution paths, extraction settings, and dependency versions, WP010 shall produce deterministic integration facts. |
| NFR-002 | Stable keys and fingerprints for WP010 facts shall not depend on database IDs, absolute developer machine paths, enumeration order, generated temporary paths, runtime environment variables, live network state, broker state, storage account state, or external service availability. |
| NFR-003 | Every persisted integration architectural statement shall have evidence unless it is purely derived from persisted facts. |
| NFR-004 | Evidence shall preserve enough context for later API and MCP consumers to explain the fact without re-reading source files. |

### 5.2 Security and Safe Analysis

| ID | Requirement |
| --- | --- |
| NFR-005 | The extractor shall not execute analyzed repository code, instantiate target integration clients, open network sockets, call external APIs, connect to brokers, connect to storage services, send messages, receive messages, send email, validate credentials, or call payment providers. |
| NFR-006 | Secret-like integration values shall not be stored in metadata, evidence snippets, warnings, errors, logs, API-ready responses, or generated outputs. |
| NFR-007 | The extractor shall preserve configuration key names and source locations while redacting values that look like passwords, tokens, API keys, connection strings, SAS tokens, certificates, private keys, client secrets, bearer tokens, authorization headers, or credentials. |
| NFR-008 | Integration extraction shall be static and deterministic, based on source files, project artifacts, configuration artifacts, generated client artifacts, service references, package metadata, and Roslyn semantic information. |
| NFR-009 | Authentication details shall be stored only as high-level hints such as bearer token, basic authentication, API key, OAuth, certificate, managed identity, connection string, or unknown. Secret values shall never be stored. |
| NFR-010 | Payment-related evidence shall be aggressively redacted to avoid storing card data, customer payment identifiers, tokens, or payment payload secrets. |

### 5.3 Performance and Scalability

| ID | Requirement |
| --- | --- |
| NFR-011 | The extractor shall avoid repeated semantic analysis of the same syntax tree, symbol, generated proxy, or configuration artifact where prior Roslyn context is available. |
| NFR-012 | The extractor shall use cancellation tokens from the extraction orchestration path. |
| NFR-013 | The extractor shall avoid unbounded recursion when following client wrappers, generated proxies, service gateway methods, message handler chains, storage helper methods, or configuration indirection. |
| NFR-014 | The extractor shall define and test safeguards for large generated service references, large OpenAPI-generated clients, large WCF proxy files, large messaging configuration files, and source files with many integration calls. |
| NFR-015 | The extractor shall avoid holding full secret-bearing configuration documents, authorization headers, or large request payloads in long-lived memory beyond the extraction scope. |

### 5.4 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-016 | C# code shall use block-scoped namespaces. |
| NFR-017 | C# code shall use Allman braces. |
| NFR-018 | C# files shall contain one public type per file. |
| NFR-019 | Private fields shall use underscore-prefixed naming. |
| NFR-020 | Executable entry points shall avoid top-level statements. |
| NFR-021 | `.csproj` files shall keep `PackageReference` entries in `ItemGroup` blocks that contain only package references. |
| NFR-022 | Internal and non-public types introduced for WP010 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand architecture or behavior. |

### 5.5 Testability

| ID | Requirement |
| --- | --- |
| NFR-023 | External integration extraction logic shall be testable without starting the Aspire AppHost. |
| NFR-024 | External integration extraction logic shall be testable using in-memory or fixture-based source repositories. |
| NFR-025 | Integration classification, confidence assignment, stable-key behavior, evidence generation, redaction, deduplication, and unknown handling shall be directly testable. |
| NFR-026 | Tests shall not require external service credentials, running web servers, queue brokers, storage emulators, SMTP servers, payment-provider sandboxes, network access, or live service endpoints. |

## 6. Technical Requirements

### 6.1 Target Runtime and Project Placement

WP010 implementation shall use the repository-approved .NET target and the project layout created by WP001. The expected primary production projects are:

| Project | Responsibility |
| --- | --- |
| `Archon.Extractors.Integrations` or the existing integration-capable extractor slice established by WP001 | HTTP, REST, WCF, SOAP, gRPC, messaging, storage, SMTP/email, payment-provider, internal service API, graph fact, confidence, and unknown extraction behavior. |
| `Archon.Extractors.Projects` | Project metadata, package, framework, and application-type context consumed by integration extractors. |
| `Archon.Extractors.Configuration` | Endpoint, base URL, queue/topic, storage, SMTP, payment, authentication, and provider configuration-key context consumed by integration extractors. |
| `Archon.Extractors.DependencyInjection` | `HttpClient`, typed client, named client, generated client, hosted service, and service-registration context consumed by WP010 where available. |
| `Archon.Extractors.AspNet` and runtime extractors | Endpoint, controller, worker, queue consumer, message handler, and hosted-service facts consumed by WP010 for correlation. |
| `Archon.Roslyn` and language-specific Roslyn projects | Shared semantic context, symbol resolution, invocation analysis, attribute analysis, generated-client support where applicable, and evidence projection support. |
| `Archon.Application` | Shared extraction contracts, snapshot accumulation contracts, graph fact contracts, and orchestration interfaces. |
| `Archon.Api.Extraction` | Coordination of extractor execution through the established API-triggered extraction path. |

Expected corresponding test projects are:

| Test Project | Responsibility |
| --- | --- |
| `Archon.Extractors.Integrations.Tests` or the corresponding WP001-created integration test project | HTTP, REST, WCF, SOAP, gRPC, messaging, storage, SMTP/email, payment-provider, internal service API, evidence, confidence, redaction, unknown, and deduplication behavior. |
| `Archon.Extractors.Projects.Tests` | Any package or project metadata detection behavior introduced specifically to support WP010. |
| `Archon.Extractors.Configuration.Tests` | Endpoint, base URL, queue/topic, storage, SMTP, payment, authentication, and provider configuration correlation behavior introduced or adjusted for WP010. |
| `Archon.Extractors.DependencyInjection.Tests` | `HttpClient`, typed client, named client, generated client, and integration-service registration behavior introduced or adjusted for WP010. |
| `Archon.Api.Extraction.Tests` | Pipeline participation, orchestration integration, warning/error propagation, and snapshot accumulation behavior. |
| `Archon.Roslyn.Tests`, `Archon.Roslyn.CSharp.Tests`, `Archon.Roslyn.VisualBasic.Tests` | Any shared semantic helper behavior introduced specifically to support WP010. |

If WP001 uses a different concrete project name for external integration extraction, WP010 shall use that existing project rather than creating a duplicate responsibility. The implementation shall preserve the intended extractor-slice separation from host, infrastructure, and persistence projects.

### 6.2 Dependency Direction

WP010 must preserve Onion Architecture dependency direction:

- Domain must not depend on application, extractors, infrastructure, or hosts.
- Application may define contracts and ports but must not depend on infrastructure or hosts.
- Extractors may depend on application and Roslyn abstractions according to existing solution direction.
- API extraction coordination may depend on extractor contracts but must not absorb extractor implementation details that belong in extractor projects.
- Infrastructure and hosts must not become a dumping ground for external integration extraction logic.

### 6.3 Integration Artifact Analysis

The implementation shall analyze integration artifacts as source data. It shall not execute generated clients, instantiate target clients, resolve real DNS names, open network connections, connect to brokers, connect to storage, send email, or call external APIs.

Integration artifact analysis shall preserve:

- Repository-relative source or artifact file path.
- Integration artifact kind.
- Project and target framework context.
- Client type, transport, provider, and role where available.
- Base URL, endpoint, queue/topic, storage, SMTP, payment, or authentication configuration key where available.
- Source line span or artifact location where available.
- Confidence and detection mode.
- Unknown reason where an integration fact is partial.

### 6.4 Client and Provider Analysis

Client and provider analysis shall use explicit package references, source artifacts, generated client artifacts, configuration keys, and Roslyn semantic information where available. It shall recognize integration clients by symbol identity when possible and by syntax, package, configuration, or artifact fallback only where symbol identity is not available. Fallback detections must carry lower confidence and explicit metadata identifying the detection mode.

Client and provider analysis shall preserve:

- Integration category.
- Client type.
- Provider or framework.
- Transport.
- Direction or role.
- Service, queue, topic, storage target, SMTP, payment provider, or internal service identity where deterministic.
- Configuration key references.
- Authentication hints without secret values.
- Dynamic or configuration-based unknowns where exact values cannot be determined.

### 6.5 Service Target and Endpoint Analysis

Service target and endpoint analysis shall use conservative static analysis. The implementation shall not attempt to resolve arbitrary runtime URL construction, evaluate environment variables, or infer service ownership without sufficient evidence.

Service target and endpoint analysis shall preserve:

- Static base URL or endpoint text only after redaction and only when safe to persist.
- Configuration key references for base URLs and endpoints.
- Relative path hints where deterministic.
- HTTP method or operation name where available.
- Queue/topic names and subscription/routing hints where deterministic.
- Storage target hints where deterministic.
- Internal/external classification confidence.
- Unknown reason metadata for dynamic or ambiguous targets.

### 6.6 Documentation Pass

WP010 shall include a documentation pass covering:

- Supported HTTP client extraction patterns.
- Supported RestSharp and REST client extraction patterns.
- Supported WCF, SOAP, and generated proxy extraction patterns.
- Supported gRPC extraction patterns.
- Supported messaging extraction patterns for Azure Service Bus, NServiceBus, RabbitMQ, MSMQ, and abstractions.
- Supported storage integration extraction patterns.
- Supported SMTP/email extraction patterns.
- Supported payment-provider extraction patterns.
- Supported internal service API correlation patterns.
- Confidence and unknown-state behavior.
- Secret redaction behavior for endpoints, authentication hints, connection strings, request payloads, and payment-related evidence.
- Testing and fixture guidance for external integration extraction.
- Limitations and known unsupported patterns, expressed as current implementation constraints rather than deferred mandatory requirements.

Internal and non-public implementation types introduced for WP010 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand the architecture or behavior.

## 7. Exclusions

WP010 shall not implement:

- Archon Discovery UI host, pages, components, assets, integration explorer, service map, queue dashboard, storage browser, payment dashboard, graph view, or tests for UI behavior.
- API query endpoints for browsing integration facts; those belong to the query API work package.
- MCP tools, MCP resources, MCP prompts, or Copilot workflows.
- Rule catalog evaluation, hotlist generation, finding suppression, or rule management.
- Data-access extraction except where previously emitted data-access facts are consumed for correlation.
- Runtime endpoint, worker, queue consumer, or message handler extraction except where WP010 consumes existing runtime facts for correlation.
- .NET UI technology extraction.
- Markdown export.
- Snapshot diff.
- Direct Neo4j writes from extractor projects.
- Live network calls, external API calls, broker inspection, queue/topic enumeration from running services, storage account inspection, SMTP sending, payment-provider calls, credential validation, or service availability checks.
- Automatic remediation, service client rewriting, integration migration, broker migration, storage migration, or generated replacement integration code.

## 8. Data and Integration Requirements

### 8.1 Required Graph Facts

WP010 shall contribute graph facts that fit the existing Archon graph model:

| Fact Type | Required Treatment |
| --- | --- |
| External service | Represent as `ExternalService` nodes with service name or unknown identity, category, transport, client type, provider, project, metadata, evidence, confidence, and unknowns where applicable. |
| Queue | Represent as `Queue` nodes with queue name, provider, broker hint, owning project, producer/consumer role, metadata, evidence, confidence, and unknowns where applicable. |
| Topic | Represent as `Topic` nodes with topic name, subscription hint, provider, broker hint, owning project, producer/consumer role, metadata, evidence, confidence, and unknowns where applicable. |
| HTTP or REST call | Represent through `CALLS_EXTERNAL_SERVICE` relationships and metadata attached to projects, types, methods, clients, endpoints, workers, or handlers. |
| WCF, SOAP, or gRPC call | Represent through `ExternalService` nodes and `CALLS_EXTERNAL_SERVICE` relationships with transport, contract, operation, endpoint, and binding metadata where available. |
| Message handler | Represent through `HANDLES` relationships linking handlers, hosted services, background services, worker methods, queues, or topics where evidence exists. |
| Configuration dependency | Represent through `USES_CONFIG` relationships linking integration facts to configuration keys where evidence exists. |
| Evidence | Link file, symbol, call site, configuration key, generated artifact, line span, snippet hash, snippet preview, confidence, and redaction metadata. |
| Unknown | Represent unresolved service names, URLs, queue/topic names, storage targets, authentication details, and provider ownership with explicit unknown reason. |

### 8.2 Metadata Requirements

WP010 metadata shall support later API and MCP consumption. Metadata shall include, where available:

- Integration category.
- Provider.
- Transport.
- Client type.
- Project key.
- Target framework.
- Type name.
- Method name.
- External service name.
- Base URL key.
- Endpoint key.
- Endpoint preview.
- Relative path hint.
- HTTP method.
- Operation name.
- Service contract name.
- Binding name.
- Endpoint name.
- Message type name.
- Queue name.
- Topic name.
- Subscription name.
- Routing key.
- Exchange name.
- Storage account key.
- Container name.
- Share name.
- Blob or file path hint.
- SMTP host key.
- Payment provider.
- Authentication hint.
- Integration role.
- Direction.
- Transport provider.
- Internal or external classification.
- Detection mode.
- Confidence reason.
- Unknown reason.

### 8.3 Evidence Requirements

Evidence shall include enough information for later API and MCP consumers to show why the fact exists:

- Repository-relative file path.
- Line and column span where available.
- Artifact path or generated file location where available.
- Symbol name where available.
- Containing symbol where available.
- Integration artifact type.
- Client type, provider, transport, operation, queue/topic, storage target, or service identifier where relevant.
- Configuration key path where relevant.
- Snippet hash.
- Snippet preview with secrets redacted.
- Detection mode.
- Confidence.

### 8.4 Integration with Earlier Work Packages

WP010 shall integrate with earlier outputs as follows:

- Use project and package facts from WP005 to identify candidate integration technologies and target frameworks.
- Use semantic symbol facts from WP006 to identify types, methods, attributes, invocations, inheritance, generics, and interface/base-class relationships.
- Use configuration facts from WP007 to resolve base URL keys, endpoint keys, queue/topic keys, storage keys, SMTP keys, payment-provider keys, and authentication configuration references where available.
- Use dependency-injection facts from WP007 to correlate `HttpClient`, typed clients, named clients, generated clients, hosted services, and wrapper registrations where available.
- Use runtime facts from WP008 to correlate endpoints, workers, handlers, hosted services, queue consumers, and entry points with downstream integration usage where method-level call or dependency evidence exists.
- Use data-access facts from WP009 only where integration facts need to be correlated with broader change impact, not to re-extract data-access behavior.
- Reuse existing nodes and relationships when earlier work packages already emitted equivalent facts.

### 8.5 Integration with Later Work Packages

WP010 output shall be shaped so later work packages can:

- Query integration facts by project, method, client, service, transport, provider, queue, topic, storage target, configuration key, and snapshot.
- Query which projects, methods, endpoints, workers, or handlers call specific external services.
- Query which projects publish to or consume from specific queues or topics.
- Query which projects use storage, email, or payment-provider integrations.
- Explain change impact from external services, endpoints, queues, topics, storage targets, configuration keys, endpoints, workers, and service dependencies.
- Feed rule evaluation and hotlist findings for legacy integration technologies, unsupported client libraries, hard-coded endpoints, secret exposure risks, payment-risk indicators, and high-coupling service dependencies.
- Expose evidence-backed integration facts through MCP tools and resources.
- Include integration maps in generated markdown.

## 9. Test Requirements

### 9.1 Required Test Coverage

| Test Area | Required Verification |
| --- | --- |
| Pipeline integration | External integration extractors run through the existing extraction orchestration path and emit snapshot facts. |
| HTTP clients | Direct `HttpClient`, injected `HttpClient`, `IHttpClientFactory`, typed clients, named clients, base addresses, request messages, HTTP methods, and configuration keys are detected. |
| REST clients | RestSharp and equivalent REST client usage, request resources, execute calls, authentication hints, and configuration keys are detected. |
| WCF and SOAP | Service references, generated proxies, `ClientBase<T>`, `ChannelFactory<T>`, endpoint configuration, bindings, contracts, operations, and SOAP/ASMX patterns are detected. |
| gRPC | gRPC package usage, generated clients, `GrpcChannel`, typed clients, endpoint configuration, and call sites are detected. |
| Messaging | Azure Service Bus, NServiceBus, RabbitMQ, MSMQ, queue/topic names, endpoint names, message type names, subscriptions, routing keys, producers, consumers, handlers, and sagas are detected. |
| Storage | Blob/file storage clients, storage target hints, configuration keys, and read/write/delete hints are detected. |
| SMTP/email | `SmtpClient`, mail message construction, send operations, email sender abstractions, and SMTP/provider configuration keys are detected. |
| Payment providers | Known payment-provider SDKs, HTTP wrappers, provider hints, endpoint keys, authentication keys, and redaction behavior are detected. |
| Internal services | Internal service API calls are correlated to analyzed endpoints only when deterministic evidence supports correlation. |
| Graph facts | `ExternalService`, `Queue`, `Topic`, `CALLS_EXTERNAL_SERVICE`, `HANDLES`, `USES_CONFIG`, and `DEPENDS_ON` facts are emitted as applicable. |
| Evidence | Every non-derived fact has source evidence with file path, artifact location or line span where available, snippet hash, and redacted preview. |
| Confidence | High, medium, and low confidence cases are assigned consistently. |
| Unknowns | Dynamic URLs, unresolved service targets, computed queue/topic names, unresolved storage targets, unknown authentication, and ambiguous provider ownership produce explicit unknowns. |
| Redaction | Tokens, passwords, API keys, SAS tokens, connection strings, authorization headers, payment tokens, card-like values, and credential-like configuration values are not present in metadata, evidence previews, warnings, errors, logs, or test output. |
| Deduplication | Duplicate facts from package references, DI registrations, generated clients, configuration artifacts, and call sites do not create duplicate graph facts. |
| C# support | C# integration extraction patterns are covered. |
| VB.NET support | VB.NET integration extraction patterns are covered where Roslyn supports semantic detection. |

### 9.2 Test Fixtures

Tests shall include fixture repositories or in-memory source sets for:

- HTTP client project with direct, injected, factory-created, named, and typed client patterns.
- RestSharp project with base URL, resource paths, execute calls, and authentication configuration.
- WCF project with service reference artifacts, generated proxy usage, `ClientBase<T>`, `ChannelFactory<T>`, and endpoint configuration.
- SOAP or ASMX service-reference project with generated proxy usage.
- gRPC project with generated clients, channel configuration, and service calls.
- Azure Service Bus producer and consumer patterns.
- NServiceBus endpoint configuration, handlers, sagas, send, publish, subscribe, routing, transport, error queue, and recoverability patterns.
- RabbitMQ publisher and consumer patterns.
- MSMQ sender and receiver patterns.
- Storage client examples covering blob/file usage and read/write/delete hints.
- SMTP/email examples covering `SmtpClient`, mail messages, and sender abstractions.
- Payment-provider examples with SDK and HTTP wrapper patterns.
- Internal service examples where one analyzed project calls another analyzed project endpoint.
- Configuration examples containing endpoint keys, authentication keys, queue/topic names, storage names, SMTP values, payment values, and secret-like values for redaction verification.
- Mixed C# and VB.NET examples where feasible.
- Duplicate fact examples where package references, DI registration, generated clients, configuration, and usage facts describe the same integration.

### 9.3 Test Constraints

Automated verification must not start the Aspire AppHost as a blocking process. Tests should use extractor-level fixtures, application-layer orchestration seams, and targeted integration tests. For this work package, the full test suite should not be run unless explicitly requested; run targeted WP010 tests and a solution build as final validation.

## 10. Acceptance Criteria

WP010 is accepted when all of the following are true:

1. External integration extractors are wired into the existing extraction orchestration path.
2. `HttpClient`, `IHttpClientFactory`, typed clients, named clients, base URL configuration keys, usage sites, and evidence are detected.
3. RestSharp and REST client usage, resources, authentication hints, configuration keys, usage sites, and evidence are detected.
4. WCF clients, SOAP clients, service references, generated proxies, endpoint configuration, bindings, contracts, operations, usage sites, and evidence are detected.
5. gRPC clients, generated clients, channel configuration, endpoint configuration, usage sites, and evidence are detected.
6. Message queues and topics, Azure Service Bus, NServiceBus, RabbitMQ, MSMQ, producers, consumers, handlers, sagas, senders, publishers, subscribers, queue/topic names, endpoint names, message type names, subscriptions, routing keys, and evidence are detected where deterministically possible.
7. Storage clients, blob/file storage, storage target hints, configuration keys, read/write/delete hints, usage sites, and evidence are detected.
8. SMTP/email client usage, mail send operations, sender abstractions, configuration keys, usage sites, and evidence are detected.
9. Payment-provider SDK and API client usage, provider hints, configuration keys, usage sites, and evidence are detected without exposing payment-sensitive values.
10. Internal service API client usage is correlated to analyzed endpoints only when deterministic evidence supports correlation.
11. `ExternalService`, `Queue`, and `Topic` nodes are emitted through the snapshot contract.
12. `CALLS_EXTERNAL_SERVICE`, `HANDLES`, `USES_CONFIG`, and `DEPENDS_ON` relationships are emitted through the snapshot contract where applicable.
13. External integrations can be queried later by project, service, queue/topic, method, client type, transport, provider, and configuration key.
14. Authentication hints and base URL configuration keys are captured where detectable without storing secret values.
15. Unknowns and confidence are explicit for unresolved or inferred integration facts.
16. Secret-like endpoint, token, credential, connection, request, payment, and authentication values are redacted before being stored or exposed in evidence, metadata, warnings, errors, or logs.
17. Tests cover HTTP clients, named/typed clients, WCF/SOAP/gRPC clients, queues, storage clients, SMTP, payment providers, unknown service targets, evidence handling, confidence, unknowns, deduplication, and redaction.
18. Documentation is updated for supported external integration extraction behavior and validation.
19. No Archon Discovery UI implementation is introduced.
20. The solution builds successfully.
21. Targeted WP010 tests pass.

## 11. Risks and Decisions

### 11.1 Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| URLs and service names may be computed at runtime. | Integration targets may be incomplete or misleading if inferred too aggressively. | Preserve configuration keys, dynamic indicators, confidence metadata, and explicit unknowns rather than inventing service names. |
| Generated clients can be large and noisy. | Extraction could be slow or produce duplicate facts. | Prefer symbol and artifact identity, use recursion safeguards, and deduplicate through stable keys and fingerprints. |
| WCF and SOAP configuration can be split across generated code and XML configuration. | Endpoint and binding facts may be partial. | Correlate generated proxy symbols with configuration artifacts where possible and warn when configuration cannot be resolved. |
| Queue/topic names can be environment-driven or computed. | Messaging graph may have unknown targets. | Link to configuration keys where available and represent unresolved queue/topic names with explicit unknown reasons. |
| Authentication and payment evidence can contain sensitive values. | Persisted evidence could expose credentials or payment data. | Apply aggressive redaction before evidence, metadata, warning, error, or log emission. |
| Internal service ownership can be hard to prove. | False internal-service links could reduce graph trust. | Require deterministic route/base URL/project evidence for internal correlation and otherwise emit unknown ownership. |
| Integration wrappers may hide actual clients. | Some service calls may be missed. | Follow wrapper methods conservatively with bounded traversal and capture wrapper-level facts when concrete targets remain unknown. |
| Multiple extraction slices may emit overlapping integration dependency facts. | Duplicate graph relationships could reduce query quality. | Deduplicate by stable key and fingerprint through the snapshot accumulator. |
| VB.NET support may differ from C# for legacy integration idioms. | Mixed-language estates may have uneven coverage. | Use Roslyn Visual Basic semantic support where available and document/test supported parity. |

### 11.2 Decisions

| Decision | Rationale |
| --- | --- |
| Use a single WP010 specification document. | User requested a single markdown document spec for WP010. |
| Create the documentation under `docs/010-External-Integration-Extraction/`. | This is the next incremental documentation work-package folder after WP009. |
| Do not create separate overview and component spec documents. | The user explicitly requested a single markdown document, overriding the multi-document collaboration pattern for this output. |
| Treat external integration extraction as extraction slices, not persistence services. | The work-package sequence requires extractors to contribute through the snapshot contract and keep Neo4j as the system of record. |
| Keep API query and MCP exposure out of WP010. | Later work packages implement query API and MCP product surfaces over persisted facts. |
| Preserve explicit unknowns rather than suppressing partial integration facts. | The source brief requires unknowns to be represented instead of omitted or invented. |
| Use deterministic integration stable-key inputs. | WP010 stable keys shall be deterministic and shall not depend on database IDs, absolute developer machine paths, enumeration order, generated temporary paths, runtime environment variables, network state, broker state, storage state, or external service availability. `ExternalService` keys shall use snapshot/repository scope plus normalized service identity, provider, transport, and configuration key when available. Unknown external service keys shall use project key plus client type plus normalized call-site or registration location. `Queue` keys shall use provider plus normalized queue name or configuration key where available. `Topic` keys shall use provider plus normalized topic name, subscription key where applicable, or configuration key where available. Call relationship keys shall use source node key plus target node key plus integration role plus normalized call-site location. |
| Use ownership and dependency direction for integration relationships. | `Project`, `Type`, `Method`, `Endpoint`, `HostedService`, `Worker`, or client nodes shall point to `ExternalService` nodes through `CALLS_EXTERNAL_SERVICE`. Handlers or hosted services shall point to `Queue` or `Topic` nodes through `HANDLES`. Integration facts shall point to configuration keys through `USES_CONFIG`. General integration dependencies may use `DEPENDS_ON` when no more specific relationship applies. |
| Use lower camel case integration metadata field names. | Integration metadata fields shall use stable API-friendly lower camel case names, including `integrationCategory`, `provider`, `transport`, `clientType`, `targetFramework`, `externalServiceName`, `baseUrlKey`, `endpointKey`, `endpointPreview`, `relativePathHint`, `httpMethod`, `operationName`, `serviceContractName`, `bindingName`, `endpointName`, `messageTypeName`, `queueName`, `topicName`, `subscriptionName`, `routingKey`, `exchangeName`, `transportProvider`, `storageAccountKey`, `containerName`, `shareName`, `blobPathHint`, `filePathHint`, `smtpHostKey`, `paymentProvider`, `authenticationHint`, `integrationRole`, `direction`, `isInternalService`, `detectionMode`, `confidenceReason`, and `unknownReason`. |
| Represent integration subtypes as metadata, not new graph node kinds. | WP010 shall keep the core graph node kinds aligned with WP002 and Appendix E: `ExternalService`, `Queue`, and `Topic`, plus existing `Project`, `Type`, `Method`, `Endpoint`, `Controller`, `HostedService`, `ConfigurationKey`, and `FilePath`. Finer distinctions shall be represented as metadata, such as `integrationCategory` values `Http`, `Rest`, `Wcf`, `Soap`, `Grpc`, `Messaging`, `Storage`, `Email`, `Payment`, and `InternalService`; `provider` values including `AzureServiceBus`, `NServiceBus`, `RabbitMQ`, `MSMQ`, and `Unknown`; `transport` values `Http`, `Https`, `Grpc`, `Soap`, `Tcp`, `Amqp`, `Msmq`, `Smtp`, and `Unknown`; and `integrationRole` values `Client`, `Producer`, `Consumer`, `Handler`, `Saga`, `Sender`, `Publisher`, `Subscriber`, `Receiver`, and `Unknown`. |

## 12. Manual Verification Requirements

The implementation documentation for WP010 shall instruct a developer to verify the work package by:

1. Restoring and building the solution.
2. Running targeted tests for HTTP client extraction.
3. Running targeted tests for RestSharp and REST client extraction.
4. Running targeted tests for WCF, SOAP, and generated proxy extraction.
5. Running targeted tests for gRPC extraction.
6. Running targeted tests for Azure Service Bus, NServiceBus, RabbitMQ, MSMQ, and generic messaging extraction.
7. Running targeted tests for storage client extraction.
8. Running targeted tests for SMTP/email extraction.
9. Running targeted tests for payment-provider extraction and redaction.
10. Running targeted tests for internal service API correlation.
11. Running targeted extraction integration tests through the API extraction module seam without launching the blocking Aspire AppHost process.
12. Inspecting representative snapshot output to confirm `ExternalService`, `Queue`, `Topic`, `CALLS_EXTERNAL_SERVICE`, `HANDLES`, `USES_CONFIG`, and `DEPENDS_ON` facts are emitted where applicable.
13. Confirming evidence includes redacted snippets and source locations.
14. Confirming secret-like endpoint, token, credential, connection, payment, request, and authentication values are not present in test output, logs, warnings, errors, metadata, or evidence previews.
15. Confirming no Archon Discovery UI resource, page, component, or front-end asset was created.

Automated validation instructions shall explicitly state not to run the AppHost as a blocking process during agent-driven verification.

## 13. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| Detect `HttpClient`, `IHttpClientFactory`, typed clients, and named clients | Sections 3.1, 4.2, 8, 9, 10 |
| Detect RestSharp and REST client usage | Sections 3.2, 4.3, 8, 9, 10 |
| Detect WCF clients and SOAP clients | Sections 3.3, 4.4, 8, 9, 10 |
| Detect gRPC clients | Sections 3.4, 4.5, 8, 9, 10 |
| Detect message queues, Azure Service Bus, NServiceBus, RabbitMQ, and MSMQ | Sections 3.5, 4.6, 8, 9, 10 |
| Detect storage clients and blob/file storage | Sections 3.6, 4.7, 8, 9, 10 |
| Detect SMTP/email integrations | Sections 3.7, 4.8, 8, 9, 10 |
| Detect payment providers | Sections 3.7, 4.9, 8, 9, 10 |
| Detect internal service APIs | Sections 4.10, 8, 9, 10 |
| Extract integration name, owning project, client type, base URL configuration key, authentication hints, usage sites, and evidence | Sections 4.2 through 4.12, 8.2, 8.3, 10 |
| Persist `ExternalService`, `Queue`, and `Topic` nodes | Sections 4.11, 8.1, 10 |
| Persist `CALLS_EXTERNAL_SERVICE`, `HANDLES`, `USES_CONFIG`, and `DEPENDS_ON` relationships | Sections 4.11, 8.1, 10 |
| Represent uncertain external targets explicitly as unknowns rather than inventing service names | Sections 4.12, 5.1, 8.1, 10 |
| External integrations can be queried by project, service, queue/topic, method, and configuration key | Sections 8.5, 10 |
| Authentication hints and base URL configuration keys are captured where detectable | Sections 4.2 through 4.12, 8.2, 10 |
| Tests cover HTTP clients, named/typed clients, WCF/SOAP/gRPC clients, queues, storage clients, SMTP, unknown service targets, and evidence handling | Sections 9, 10 |
| Repository documentation updated | Sections 6.6, 12, 10 |
| No Discovery UI implementation | Sections 1.3, 7, 10, 12 |

## 14. Open Questions

No open questions remain for WP010. Stable-key inputs, graph relationship direction, metadata field names, integration subtype representation, safe static-analysis constraints, and redaction expectations are recorded as definitive decisions in section 11.2.

## 15. Change Log

| Date | Change |
| --- | --- |
| 2026-05-22 | Created initial single-document WP010 specification from `docs/foundation/work-packages.md` and the Archon source brief. |
