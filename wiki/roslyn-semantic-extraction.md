# Roslyn Semantic Extraction

Roslyn semantic extraction is the part of Archon that reads source code through the .NET compiler platform, commonly called Roslyn, and turns compiler-resolved symbols into graph-ready architecture facts. A compiler-resolved symbol is different from a string found in a file. It is the compiler's representation of a declaration such as a namespace, type, constructor, method, property, or field after parsing and binding the source code. Archon uses those symbols because symbol identity is more reliable than text matching when the same word appears in comments, string literals, aliases, overloads, or nested declarations.

Read this page with [solution architecture](solution-architecture.md) for layering rules, [graph domain model](graph-domain-model.md) for graph vocabulary, [API extraction workflow](api-extraction-workflow.md) for the broader extraction pipeline, and [validation and test workflows](validation-and-test-workflows.md) for focused commands. Glossary entries for Roslyn semantic model, semantic declaration fact, semantic evidence, and semantic stable key appear in the [glossary](glossary.md).

Reader path: [Home](home.md) -> [Solution architecture](solution-architecture.md) -> Roslyn semantic extraction -> [Graph domain model](graph-domain-model.md).

## Current C# and VB.NET semantic slice

The current WP006 slice implements C# and VB.NET semantic extraction paths for declarations and compiler-resolved relationships. A caller supplies a Roslyn syntax tree, the matching semantic model, a repository root, a source document path, and a logical project context. The language extractor walks declaration syntax and asks the semantic model for declared Roslyn symbols before emitting facts. If Roslyn cannot resolve a declaration, the extractor records a warning instead of inventing a graph fact from text alone.

The output is language-neutral. Declarations are represented as semantic declaration facts with a source language, declaration kind, stable key, display name, fully qualified name, project context, parent declaration key, and source evidence. Direct declaration nesting is represented as `CONTAINS` relationship facts. For example, a namespace contains a type, and a type contains its constructor, methods, properties, and fields. C# and VB.NET both emit `CALLS`, `IMPLEMENTS`, `INHERITS`, `INJECTS`, and `DEPENDS_ON` relationship facts when Roslyn resolves the participating symbols. These relationship facts include source and target symbol identity, source evidence, confidence, and deterministic metadata that describes how the relationship was discovered.

VB.NET support is first-class rather than a text-only compatibility layer. The Visual Basic extractor handles namespaces, modules, classes, structures, interfaces, enums, delegates, constructors, shared members, methods, properties, default properties, fields, events, constants, inheritance, interface implementation, constructor injection, attributes, method calls, object creation, extension methods, and generic constraints where Roslyn can resolve the symbols. A Visual Basic module is a compiler-backed type that contains shared members, so it projects into the shared `Type` declaration kind. A default property is a property that can be invoked through an indexed object expression, so it projects into the shared `Property` declaration kind while property access contributes `DEPENDS_ON` facts. A Visual Basic root namespace is project-level namespace text that Roslyn composes with source namespaces; when the semantic model exposes that composed namespace, semantic facts use the composed fully qualified name.

## Relationship and dependency semantics

A semantic relationship fact is the Roslyn layer's graph-ready representation of one compiler-backed edge before that edge is projected into the domain graph. The C# and VB.NET extractors emit `CALLS` when an invocation or object creation binds to a method or constructor symbol. They emit `IMPLEMENTS` when a type implements an interface or a member explicitly implements an interface member. They emit `INHERITS` when a type has a non-`object` base type or a member overrides a base member. They emit `INJECTS` when a constructor parameter has a compiler-resolved dependency type, because constructor parameters are the deterministic source-level signal for constructor injection in this slice. They emit `DEPENDS_ON` for broader symbol dependencies such as property access, object-created types, invocation target types, attributes, parameter types, return types, field types, event types, implemented interfaces, base types, and generic constraints.

Relationship confidence is currently categorical. `CompilerResolved` means Roslyn bound the relationship to a concrete symbol. The extractor does not create text-only call targets, guessed service types, or string-derived dependencies. This conservative rule matters because later API, MCP, and graph traversal behavior should be able to trust that a high-confidence relationship came from compiler binding rather than from a search term that merely looked like a symbol. Future degraded-compilation slices will add partially resolved and unresolved relationship records where the compiler cannot prove a target.

Duplicate relationship discoveries collapse deterministically by stable key. A relationship key is derived from the relationship kind, source endpoint, target endpoint, and a deterministic relationship-source qualifier such as `Invocation`, `ObjectCreation`, `PropertyAccess`, `ConstructorParameter`, `Attribute`, `ParameterType`, `ReturnType`, or `GenericConstraint`. The qualifier prevents unrelated dependency meanings from being merged accidentally. For example, a method can depend on the same type because it returns that type and because it constructs that type; those facts have the same endpoints but different architectural meanings, so they remain separate. Two identical calls from the same source method to the same target method, however, produce the same `CALLS` key and collapse to one fact.

Attributes are modeled as dependencies on attribute types. Assembly attributes use a deterministic project-level source endpoint because they are not owned by a source declaration node. Type, member, parameter, return-value, and type-parameter attributes are attached to the nearest declaration fact that owns the syntax being analyzed. This keeps attributes visible to later architecture rules without pretending that an attribute is a normal call or inheritance relationship.

## Stable identity and repository-relative evidence

A semantic stable key is the durable identity for a compiler-backed declaration, relationship, or symbol reference. It does not use a Neo4j node ID, an in-memory Roslyn object reference, or an absolute machine path. The current key builder scopes declaration keys by source language, project context, fully qualified symbol name, and compiler-facing metadata name or signature. Relationship keys are derived from endpoint keys, relationship kind, and relationship-source qualifier. Symbol-reference keys provide deterministic endpoints for metadata or external symbols that do not have declaration facts in the analyzed document. That scope is important because two projects can legally contain a `Sample.Widget` type, and those declarations must remain distinct in the graph.

Evidence follows the same determinism rule. Source paths are normalized to repository-relative paths with forward slashes, such as `src/Sample.App/Widget.cs`. The evidence also records one-based line and column spans, the symbol name, the containing symbol when available, a bounded snippet preview, and a `sha256:` snippet hash. The preview helps contributors recognize the declaration quickly, while the hash supports deterministic comparison without storing an entire source file in graph evidence. Missing or blank snippet text is represented as unavailable snippet details rather than a fabricated preview or hash.

## How the slice fits the architecture

The shared contracts live in `src/Archon.Roslyn`, which is the language-neutral Roslyn project. That project defines semantic requests, results, declaration facts, relationship facts, source language values, symbol identity, evidence, and deterministic helpers. The C# implementation lives in `src/Archon.Roslyn.CSharp`, and the VB.NET implementation lives in `src/Archon.Roslyn.VisualBasic`; both depend inward on the shared Roslyn abstractions. The infrastructure Roslyn adapter remains the future home for workspace loading, compilations, documents, metadata references, and diagnostics that come from real repositories. The current tests create in-memory syntax trees and compilations so the semantic behavior is validated without starting the Aspire AppHost, Neo4j, API endpoints, MCP tools, or UI behavior.

This boundary preserves the Onion Architecture rule. Roslyn language projects perform compiler-backed extraction and produce graph-ready facts, but they do not write Neo4j data and do not own API route behavior. Application and persistence layers can later decide how to accumulate, transform, persist, expose, or query those facts. Keeping the first slice graph-ready rather than persistence-bound lets the same semantic facts support API workflows, MCP workflows, markdown export, and future analysis without duplicating extraction logic.

## Walkthrough example

Consider this source file:

```csharp
namespace Sample.App
{
	public sealed class Widget
	{
		private readonly string _name;

		public Widget(string name)
		{
			_name = name;
		}

		public string Name { get; }

		public void Run()
		{
		}
	}
}
```

When the C# semantic extractor receives this file with a matching semantic model, it emits a namespace declaration for `Sample.App`, a type declaration for `Sample.App.Widget`, method declarations for `Widget.Widget(string)` and `Widget.Run()`, a property declaration for `Sample.App.Widget.Name`, and a field declaration for `Sample.App.Widget._name`. The namespace declaration is the parent of the type declaration. The type declaration is the parent of the constructor, method, property, and field declarations. Each declaration carries source evidence pointing back to the repository-relative file path and declaration span. The VB.NET extractor follows the same graph vocabulary for analogous source, while also mapping VB.NET-specific forms such as modules, default properties, shared members, and root namespace composition into the shared declaration and relationship model.

If `Run` called another method, read a property, created another type, or used an extension method, the relationship slice would add relationship facts sourced from the `Run` method declaration. If `Widget` inherited a base class, implemented an interface, carried an attribute, or accepted a constructor-injected service parameter, those dependencies would also become relationship facts with compiler-resolved source and target symbol identity. A contributor debugging relationship output should inspect the relationship kind, `dependencySource` metadata value, source and target symbol identities, confidence, and evidence span. If a relationship is missing, first check whether Roslyn could bind the symbol in the test compilation or workspace. The current slice prefers omission over invention when a target cannot be compiler-resolved.

A contributor debugging this output should first inspect the declaration fact's fully qualified name and evidence span. If the declaration is missing, confirm that the source file was parsed into the syntax tree used by the semantic model and that the declaration has a resolvable Roslyn symbol. The current slice intentionally avoids text-only fallback facts, so a missing compiler symbol produces a warning rather than a low-confidence declaration node.

## Validation commands

The focused validation path for the current semantic extraction slice is:

```powershell
dotnet test .\test\Archon.Roslyn.Tests\Archon.Roslyn.Tests.csproj --no-restore
dotnet test .\test\Archon.Roslyn.CSharp.Tests\Archon.Roslyn.CSharp.Tests.csproj --no-restore
dotnet test .\test\Archon.Roslyn.VisualBasic.Tests\Archon.Roslyn.VisualBasic.Tests.csproj --no-restore
dotnet build .\Archon.slnx --no-restore
```

These commands validate shared helper behavior, C# declaration and relationship extraction behavior, VB.NET declaration and relationship extraction behavior, and integrated solution compilation. They intentionally do not start the Aspire AppHost. They also do not require Neo4j credentials, API HTTP calls, MCP tools, repository scanning, or Visual Studio automation.
