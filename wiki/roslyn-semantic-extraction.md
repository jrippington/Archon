# Roslyn Semantic Extraction

Roslyn semantic extraction is the part of Archon that reads source code through the .NET compiler platform, commonly called Roslyn, and turns compiler-resolved symbols into graph-ready architecture facts. A compiler-resolved symbol is different from a string found in a file. It is the compiler's representation of a declaration such as a namespace, type, constructor, method, property, or field after parsing and binding the source code. Archon uses those symbols because symbol identity is more reliable than text matching when the same word appears in comments, string literals, aliases, overloads, or nested declarations.

Read this page with [solution architecture](solution-architecture.md) for layering rules, [graph domain model](graph-domain-model.md) for graph vocabulary, [API extraction workflow](api-extraction-workflow.md) for the broader extraction pipeline, and [validation and test workflows](validation-and-test-workflows.md) for focused commands. Glossary entries for Roslyn semantic model, semantic declaration fact, semantic evidence, and semantic stable key appear in the [glossary](glossary.md).

Reader path: [Home](home.md) -> [Solution architecture](solution-architecture.md) -> Roslyn semantic extraction -> [Graph domain model](graph-domain-model.md).

## Current C# declaration slice

The current WP006 slice implements the smallest useful C# semantic extraction path. A caller supplies a Roslyn syntax tree, the matching semantic model, a repository root, a source document path, and a logical project context. The C# extractor walks declaration syntax for namespaces, types, constructors, methods, properties, and fields. For each supported declaration, it asks the semantic model for the declared Roslyn symbol before emitting a fact. If Roslyn cannot resolve a declaration, the extractor records a warning instead of inventing a graph fact from text alone.

The output is language-neutral. Declarations are represented as semantic declaration facts with a source language, declaration kind, stable key, display name, fully qualified name, project context, parent declaration key, and source evidence. Direct declaration nesting is represented as `CONTAINS` relationship facts. For example, a namespace contains a type, and a type contains its constructor, methods, properties, and fields. This is not yet the full source-code dependency model. Later slices will add deeper relationships such as calls, inheritance, implementation, injection, and unresolved-symbol unknowns. The current slice deliberately proves the shared semantic path first so later extraction can build on a deterministic foundation.

## Stable identity and repository-relative evidence

A semantic stable key is the durable identity for a compiler-backed declaration or relationship. It does not use a Neo4j node ID, an in-memory Roslyn object reference, or an absolute machine path. The current key builder scopes declaration keys by source language, project context, fully qualified symbol name, and compiler-facing metadata name or signature. That scope is important because two projects can legally contain a `Sample.Widget` type, and those declarations must remain distinct in the graph.

Evidence follows the same determinism rule. Source paths are normalized to repository-relative paths with forward slashes, such as `src/Sample.App/Widget.cs`. The evidence also records one-based line and column spans, the symbol name, the containing symbol when available, a bounded snippet preview, and a `sha256:` snippet hash. The preview helps contributors recognize the declaration quickly, while the hash supports deterministic comparison without storing an entire source file in graph evidence. Missing or blank snippet text is represented as unavailable snippet details rather than a fabricated preview or hash.

## How the slice fits the architecture

The shared contracts live in `src/Archon.Roslyn`, which is the language-neutral Roslyn project. That project defines semantic requests, results, declaration facts, relationship facts, source language values, symbol identity, evidence, and deterministic helpers. The C# implementation lives in `src/Archon.Roslyn.CSharp`, which depends inward on the shared Roslyn abstractions. The infrastructure Roslyn adapter remains the future home for workspace loading, compilations, documents, metadata references, and diagnostics that come from real repositories. The current tests create in-memory syntax trees and compilations so the semantic behavior is validated without starting the Aspire AppHost, Neo4j, API endpoints, MCP tools, or UI behavior.

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

When the C# semantic extractor receives this file with a matching semantic model, it emits a namespace declaration for `Sample.App`, a type declaration for `Sample.App.Widget`, method declarations for `Widget.Widget(string)` and `Widget.Run()`, a property declaration for `Sample.App.Widget.Name`, and a field declaration for `Sample.App.Widget._name`. The namespace declaration is the parent of the type declaration. The type declaration is the parent of the constructor, method, property, and field declarations. Each declaration carries source evidence pointing back to the repository-relative file path and declaration span.

A contributor debugging this output should first inspect the declaration fact's fully qualified name and evidence span. If the declaration is missing, confirm that the source file was parsed into the syntax tree used by the semantic model and that the declaration has a resolvable Roslyn symbol. The current slice intentionally avoids text-only fallback facts, so a missing compiler symbol produces a warning rather than a low-confidence declaration node.

## Validation commands

The focused validation path for the current C# semantic extraction slice is:

```powershell
dotnet test .\test\Archon.Roslyn.Tests\Archon.Roslyn.Tests.csproj --no-restore
dotnet test .\test\Archon.Roslyn.CSharp.Tests\Archon.Roslyn.CSharp.Tests.csproj --no-restore
dotnet build .\Archon.slnx --no-restore
```

These commands validate shared helper behavior, C# declaration extraction behavior, and integrated solution compilation. They intentionally do not start the Aspire AppHost. They also do not require Neo4j credentials, API HTTP calls, MCP tools, repository scanning, or Visual Studio automation.
