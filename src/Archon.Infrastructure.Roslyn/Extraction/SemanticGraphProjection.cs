using Archon.Application.Extraction.Pipeline;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Roslyn.SemanticModel;

namespace Archon.Infrastructure.Roslyn.Extraction
{
    /// <summary>
    /// Projects language-neutral Roslyn semantic facts into the application graph contracts that persistence adapters already understand.
    /// </summary>
    /// <remarks>
    /// This projector is infrastructure-owned because it bridges Roslyn extraction output and Archon's application/domain graph model. It deliberately emits only generic nodes, edges, evidence, and diagnostics so Neo4j-specific behavior remains in the Neo4j adapter.
    /// </remarks>
    public sealed class SemanticGraphProjection
    {
        /// <summary>
        /// Adds one semantic extraction result to the supplied snapshot accumulation using snapshot-scoped graph contracts.
        /// </summary>
        /// <param name="context">The extraction stage context that carries the accepted run, resolved input, and shared accumulator.</param>
        /// <param name="result">The Roslyn semantic result to project.</param>
        /// <param name="projectRelativePath">The repository-relative project path associated with the result.</param>
        public void Project(Archon.Application.Extraction.Pipeline.ExtractionStageContext context, SemanticExtractionResult result, string projectRelativePath)
        {
            // Projection is intentionally additive: each semantic fact becomes graph-ready state while non-fatal degraded data remains warnings or evidence.
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(result);
            string normalizedProjectRelativePath = NormalizeRelativePath(projectRelativePath);
            StableKey repositoryStableKey = CreateRepositoryStableKey(context.ResolvedInput.RepositoryRootDirectory);
            StableKey snapshotStableKey = CreateSnapshotStableKey(repositoryStableKey, context.Run.RunId.ToString());
            StableKey projectStableKey = StableKeyGenerator.ForProject(normalizedProjectRelativePath);

            foreach (SemanticDeclarationFact declaration in result.Declarations)
            {
                context.Accumulation.AddEvidence(CreateEvidence(snapshotStableKey, declaration.Evidence, declaration.StableKey, EvidenceKind.CompilerSymbol, declaration.Confidence, declaration.Metadata));
                context.Accumulation.AddNode(CreateDeclarationNode(snapshotStableKey, projectStableKey, declaration));
            }

            foreach (SemanticRelationshipFact relationship in result.Relationships)
            {
                context.Accumulation.AddEvidence(CreateEvidence(snapshotStableKey, relationship.Evidence, relationship.StableKey, EvidenceKind.CompilerSymbol, relationship.Confidence, relationship.Metadata));
                context.Accumulation.AddEdge(CreateRelationshipEdge(snapshotStableKey, relationship));
            }

            foreach (SemanticDiagnosticFact diagnostic in result.Diagnostics)
            {
                context.Accumulation.AddEvidence(CreateDiagnosticEvidence(snapshotStableKey, diagnostic));
                context.Accumulation.AddWarning($"Semantic diagnostic {diagnostic.DiagnosticId} ({diagnostic.Severity}) in {diagnostic.Evidence.RepositoryRelativeFilePath}: {diagnostic.Message}");
            }

            foreach (SemanticUnknownFact unknown in result.Unknowns)
            {
                context.Accumulation.AddEvidence(CreateUnknownEvidence(snapshotStableKey, unknown));
                context.Accumulation.AddWarning($"Semantic unknown {unknown.Reason} in {unknown.Evidence.RepositoryRelativeFilePath}: {unknown.Description}");
            }

            foreach (SemanticEvidenceContribution contribution in result.EvidenceContributions)
            {
                context.Accumulation.AddEvidence(CreateEvidenceContribution(snapshotStableKey, contribution));
            }

            foreach (string warning in result.Warnings)
            {
                context.Accumulation.AddWarning(warning);
            }

            foreach (string error in result.Errors)
            {
                context.Accumulation.AddError(error);
            }
        }

        /// <summary>
        /// Creates a graph node from one semantic declaration fact.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="projectStableKey">The stable key of the project that owns the declaration.</param>
        /// <param name="declaration">The semantic declaration fact being projected.</param>
        /// <returns>An architecture node ready for application accumulation and persistence.</returns>
        private static ArchitectureNode CreateDeclarationNode(StableKey snapshotStableKey, StableKey projectStableKey, SemanticDeclarationFact declaration)
        {
            // Semantic stable keys are already deterministic and repository-relative, so projection wraps them in the domain value object unchanged.
            StableKey nodeStableKey = new(declaration.StableKey);
            StableKey evidenceStableKey = CreateEvidenceStableKey(snapshotStableKey, declaration.StableKey, declaration.Evidence);
            NodeKind nodeKind = MapDeclarationKind(declaration.DeclarationKind);
            GraphMetadata metadata = CreateMetadata(
                declaration.Metadata,
                new Dictionary<string, object?>
                {
                    ["semantic.confidenceCategory"] = declaration.Confidence.ToString(),
                    ["semantic.declarationKind"] = declaration.DeclarationKind.ToString(),
                    ["semantic.metadataName"] = declaration.SymbolIdentity.MetadataName,
                    ["semantic.projectContext"] = declaration.ProjectContext,
                    ["semantic.sourceLanguage"] = declaration.SourceLanguage.ToString()
                });
            string searchName = string.Join(' ', declaration.SymbolIdentity.FullyQualifiedName.Split(['.', ':', '(', ')', ',', '<', '>', ' '], StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                nodeKind,
                declaration.SymbolIdentity.DisplayName,
                declaration.SymbolIdentity.FullyQualifiedName,
                searchName,
                MapLanguage(declaration.SourceLanguage),
                projectStableKey,
                string.IsNullOrWhiteSpace(declaration.ParentStableKey) ? null : new StableKey(declaration.ParentStableKey),
                KnowledgeKind.Fact,
                null,
                null,
                MapConfidence(declaration.Confidence),
                UnknownState.Known,
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForNode(nodeKind, declaration.SymbolIdentity.DisplayName, declaration.SymbolIdentity.FullyQualifiedName, searchName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a graph edge from one semantic relationship fact.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the edge.</param>
        /// <param name="relationship">The semantic relationship fact being projected.</param>
        /// <returns>An architecture edge ready for application accumulation and persistence.</returns>
        private static ArchitectureEdge CreateRelationshipEdge(StableKey snapshotStableKey, SemanticRelationshipFact relationship)
        {
            // Unknown relationship state remains first-class so degraded semantic extraction can be queried without parsing metadata.
            EdgeKind edgeKind = MapRelationshipKind(relationship.RelationshipKind);
            StableKey sourceStableKey = new(relationship.SourceStableKey);
            StableKey targetStableKey = new(relationship.TargetStableKey);
            StableKey evidenceStableKey = CreateEvidenceStableKey(snapshotStableKey, relationship.StableKey, relationship.Evidence);
            UnknownState unknownState = string.IsNullOrWhiteSpace(relationship.UnknownReason) ? UnknownState.Known : UnknownState.Unknown(relationship.UnknownReason);
            GraphMetadata metadata = CreateMetadata(
                relationship.Metadata,
                new Dictionary<string, object?>
                {
                    ["semantic.confidenceCategory"] = relationship.Confidence.ToString(),
                    ["semantic.relationshipKind"] = relationship.RelationshipKind.ToString(),
                    ["semantic.sourceSymbol"] = relationship.SourceSymbolIdentity?.FullyQualifiedName,
                    ["semantic.targetSymbol"] = relationship.TargetSymbolIdentity?.FullyQualifiedName
                });
            return new ArchitectureEdge(
                snapshotStableKey,
                new StableKey(relationship.StableKey),
                edgeKind,
                sourceStableKey,
                targetStableKey,
                true,
                KnowledgeKind.Fact,
                MapConfidence(relationship.Confidence),
                unknownState,
                evidenceStableKey,
                metadata,
                FingerprintGenerator.ForEdge(edgeKind, sourceStableKey, targetStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates generic source evidence from semantic fact evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidence">The semantic evidence supplied by Roslyn extraction.</param>
        /// <param name="factStableKey">The stable key of the semantic fact supported by the evidence.</param>
        /// <param name="evidenceKind">The graph evidence kind to assign.</param>
        /// <param name="confidence">The semantic confidence category to map to numeric confidence.</param>
        /// <param name="metadata">The semantic metadata to copy into graph metadata.</param>
        /// <returns>A snapshot-scoped evidence record.</returns>
        private static EvidenceRecord CreateEvidence(StableKey snapshotStableKey, SemanticEvidence evidence, string factStableKey, EvidenceKind evidenceKind, SemanticFactConfidence confidence, IReadOnlyDictionary<string, string> metadata)
        {
            // Column data is preserved in metadata because the current generic evidence contract exposes line ranges as first-class fields.
            GraphMetadata graphMetadata = CreateMetadata(
                metadata,
                new Dictionary<string, object?>
                {
                    ["semantic.endColumn"] = evidence.EndColumn,
                    ["semantic.factStableKey"] = factStableKey,
                    ["semantic.startColumn"] = evidence.StartColumn
                });
            RepositoryRelativePath filePath = RepositoryRelativePath.Parse(evidence.RepositoryRelativeFilePath);
            return new EvidenceRecord(
                snapshotStableKey,
                CreateEvidenceStableKey(snapshotStableKey, factStableKey, evidence),
                evidenceKind,
                filePath,
                evidence.StartLine,
                evidence.EndLine,
                evidence.SymbolName,
                evidence.ContainingSymbolName,
                evidence.SnippetHash,
                evidence.SnippetPreview,
                KnowledgeKind.Fact,
                MapConfidence(confidence),
                UnknownState.Known,
                graphMetadata,
                FingerprintGenerator.ForEvidence(evidenceKind, filePath.Value, evidence.StartLine, evidence.EndLine, evidence.SymbolName, KnowledgeKind.Fact, graphMetadata));
        }

        /// <summary>
        /// Creates compiler-diagnostic evidence from a semantic diagnostic fact.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="diagnostic">The semantic diagnostic fact being projected.</param>
        /// <returns>A snapshot-scoped compiler diagnostic evidence record.</returns>
        private static EvidenceRecord CreateDiagnosticEvidence(StableKey snapshotStableKey, SemanticDiagnosticFact diagnostic)
        {
            // Compiler diagnostics are evidence rather than fatal errors because they describe degraded semantic quality while extraction continues.
            GraphMetadata metadata = CreateMetadata(
                diagnostic.Metadata,
                new Dictionary<string, object?>
                {
                    ["semantic.compilerSource"] = diagnostic.CompilerSource,
                    ["semantic.diagnosticId"] = diagnostic.DiagnosticId,
                    ["semantic.diagnosticMessage"] = diagnostic.Message,
                    ["semantic.diagnosticSeverity"] = diagnostic.Severity.ToString()
                });
            return CreateEvidence(snapshotStableKey, diagnostic.Evidence, $"diagnostic:{diagnostic.DiagnosticId}:{diagnostic.Evidence.RepositoryRelativeFilePath}:{diagnostic.Evidence.StartLine}", EvidenceKind.CompilerDiagnostic, SemanticFactConfidence.PartiallyResolved, metadata.ToDictionary());
        }

        /// <summary>
        /// Creates source evidence for one explicit semantic unknown.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="unknown">The semantic unknown fact being projected.</param>
        /// <returns>A snapshot-scoped inference evidence record for the unknown.</returns>
        private static EvidenceRecord CreateUnknownEvidence(StableKey snapshotStableKey, SemanticUnknownFact unknown)
        {
            // Unknowns are represented as degraded evidence plus a warning until a future domain model adds a dedicated unknown fact section.
            GraphMetadata metadata = CreateMetadata(
                unknown.Metadata,
                new Dictionary<string, object?>
                {
                    ["semantic.confidenceCategory"] = unknown.Confidence.ToString(),
                    ["semantic.description"] = unknown.Description,
                    ["semantic.projectContext"] = unknown.ProjectContext,
                    ["semantic.reason"] = unknown.Reason.ToString(),
                    ["semantic.sourceLanguage"] = unknown.SourceLanguage.ToString(),
                    ["semantic.sourceSymbol"] = unknown.SourceSymbolIdentity?.FullyQualifiedName,
                    ["semantic.unknownStableKey"] = unknown.StableKey
                });
            return CreateEvidence(snapshotStableKey, unknown.Evidence, unknown.StableKey, EvidenceKind.Inference, unknown.Confidence, metadata.ToDictionary());
        }

        /// <summary>
        /// Creates additional source evidence for partial declarations and generated companions.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="contribution">The semantic evidence contribution to project.</param>
        /// <returns>A snapshot-scoped evidence record for the additional contribution.</returns>
        private static EvidenceRecord CreateEvidenceContribution(StableKey snapshotStableKey, SemanticEvidenceContribution contribution)
        {
            // Contribution evidence points to the same fact stable key but receives a distinct evidence key from its own source span.
            IReadOnlyDictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["semantic.contributionKind"] = contribution.ContributionKind,
                ["semantic.generated"] = contribution.Generated.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["semantic.ownerFactStableKey"] = contribution.FactStableKey
            };
            return CreateEvidence(snapshotStableKey, contribution.Evidence, $"contribution:{contribution.FactStableKey}:{contribution.ContributionKind}", contribution.Generated ? EvidenceKind.GeneratedArtifact : EvidenceKind.CompilerSymbol, SemanticFactConfidence.Inferred, metadata);
        }

        /// <summary>
        /// Creates a deterministic evidence stable key from snapshot, fact identity, and source coordinates.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes evidence identity.</param>
        /// <param name="factStableKey">The semantic fact stable key supported by the evidence.</param>
        /// <param name="evidence">The source evidence span.</param>
        /// <returns>A stable key for the evidence record.</returns>
        private static StableKey CreateEvidenceStableKey(StableKey snapshotStableKey, string factStableKey, SemanticEvidence evidence)
        {
            // The key includes source coordinates so partial declarations and repeated relationship evidence remain distinct within a snapshot.
            return new StableKey($"evidence://{snapshotStableKey.Value}/{factStableKey}/{evidence.RepositoryRelativeFilePath}:{evidence.StartLine}:{evidence.StartColumn}:{evidence.EndLine}:{evidence.EndColumn}");
        }

        /// <summary>
        /// Maps semantic declaration categories to the shared graph node vocabulary.
        /// </summary>
        /// <param name="declarationKind">The semantic declaration category.</param>
        /// <returns>The matching graph node kind.</returns>
        private static NodeKind MapDeclarationKind(SemanticDeclarationKind declarationKind)
        {
            // A switch keeps unsupported enum additions visible at compile time in future work packages.
            return declarationKind switch
            {
                SemanticDeclarationKind.Namespace => NodeKind.Namespace,
                SemanticDeclarationKind.Type => NodeKind.Type,
                SemanticDeclarationKind.Method => NodeKind.Method,
                SemanticDeclarationKind.Property => NodeKind.Property,
                SemanticDeclarationKind.Field => NodeKind.Field,
                _ => throw new ArgumentOutOfRangeException(nameof(declarationKind), declarationKind, "Unsupported semantic declaration kind.")
            };
        }

        /// <summary>
        /// Maps semantic relationship categories to the shared graph edge vocabulary.
        /// </summary>
        /// <param name="relationshipKind">The semantic relationship category.</param>
        /// <returns>The matching graph edge kind.</returns>
        private static EdgeKind MapRelationshipKind(SemanticRelationshipKind relationshipKind)
        {
            // Relationship vocabulary is normalized here so language-specific extractors do not reference domain graph classes.
            return relationshipKind switch
            {
                SemanticRelationshipKind.Contains => EdgeKind.Contains,
                SemanticRelationshipKind.Calls => EdgeKind.Calls,
                SemanticRelationshipKind.Implements => EdgeKind.Implements,
                SemanticRelationshipKind.Inherits => EdgeKind.Inherits,
                SemanticRelationshipKind.Injects => EdgeKind.Injects,
                SemanticRelationshipKind.DependsOn => EdgeKind.DependsOn,
                _ => throw new ArgumentOutOfRangeException(nameof(relationshipKind), relationshipKind, "Unsupported semantic relationship kind.")
            };
        }

        /// <summary>
        /// Maps semantic confidence categories to normalized graph confidence values.
        /// </summary>
        /// <param name="confidence">The semantic confidence category.</param>
        /// <returns>The normalized graph confidence value.</returns>
        private static Confidence MapConfidence(SemanticFactConfidence confidence)
        {
            // Confidence categories collapse to graph numbers while the category name is preserved in metadata for explanation.
            return confidence switch
            {
                SemanticFactConfidence.CompilerResolved => Confidence.High,
                SemanticFactConfidence.Inferred => Confidence.Medium,
                SemanticFactConfidence.Generated => Confidence.Medium,
                SemanticFactConfidence.MetadataOnly => Confidence.Medium,
                SemanticFactConfidence.PartiallyResolved => Confidence.Medium,
                SemanticFactConfidence.Unresolved => Confidence.Low,
                _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Unsupported semantic confidence category.")
            };
        }

        /// <summary>
        /// Maps semantic source language values to graph language labels.
        /// </summary>
        /// <param name="sourceLanguage">The semantic source language.</param>
        /// <returns>The graph language label.</returns>
        private static string MapLanguage(SourceLanguage sourceLanguage)
        {
            // Labels remain user-facing and match existing project extraction language display values.
            return sourceLanguage switch
            {
                SourceLanguage.CSharp => "C#",
                SourceLanguage.VisualBasic => "VB.NET",
                _ => sourceLanguage.ToString()
            };
        }

        /// <summary>
        /// Creates graph metadata from string metadata plus supplemental semantic projection fields.
        /// </summary>
        /// <param name="metadata">The semantic metadata supplied by extraction.</param>
        /// <param name="supplementalValues">The supplemental graph metadata values added by projection.</param>
        /// <returns>Canonical graph metadata.</returns>
        private static GraphMetadata CreateMetadata(IReadOnlyDictionary<string, string> metadata, IReadOnlyDictionary<string, object?> supplementalValues)
        {
            // Semantic metadata keys are prefixed when needed so they do not collide with reserved first-class graph property names.
            Dictionary<string, object?> values = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in metadata)
            {
                values[PrefixMetadataKey(pair.Key)] = pair.Value;
            }

            foreach (KeyValuePair<string, object?> pair in supplementalValues)
            {
                if (pair.Value is not null && !string.IsNullOrWhiteSpace(pair.Value.ToString()))
                {
                    values[PrefixMetadataKey(pair.Key)] = pair.Value;
                }
            }

            return values.Count == 0 ? GraphMetadata.Empty : GraphMetadata.From(values);
        }

        /// <summary>
        /// Prefixes semantic metadata keys that are not already namespaced.
        /// </summary>
        /// <param name="key">The metadata key supplied by extraction or projection.</param>
        /// <returns>A graph-safe metadata key.</returns>
        private static string PrefixMetadataKey(string key)
        {
            // Reserved graph names such as confidence and stableKey cannot be stored directly in GraphMetadata.
            string trimmed = key.Trim();
            return trimmed.StartsWith("semantic.", StringComparison.Ordinal) ? trimmed : $"semantic.{trimmed}";
        }

        /// <summary>
        /// Normalizes repository-relative project paths for project stable keys.
        /// </summary>
        /// <param name="relativePath">The project path to normalize.</param>
        /// <returns>A repository-relative path using forward slashes.</returns>
        private static string NormalizeRelativePath(string relativePath)
        {
            // The domain path value object performs final validation after separators are normalized.
            return RepositoryRelativePath.Parse(relativePath.Replace('\\', '/')).Value;
        }

        /// <summary>
        /// Creates the repository stable key using the same rule as the application snapshot assembler and project stage.
        /// </summary>
        /// <param name="repositoryRootDirectory">The accepted repository root directory.</param>
        /// <returns>The repository stable key.</returns>
        private static StableKey CreateRepositoryStableKey(string repositoryRootDirectory)
        {
            // The current application contract derives repository identity from normalized submitted root path.
            string normalized = Path.TrimEndingDirectorySeparator(repositoryRootDirectory).Replace('\\', '/').Trim().ToLowerInvariant();
            return StableKeyGenerator.ForRepository(normalized);
        }

        /// <summary>
        /// Creates the snapshot stable key used by the current application assembler for the accepted run.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key associated with the run.</param>
        /// <param name="runId">The accepted extraction run identifier text.</param>
        /// <returns>The snapshot stable key.</returns>
        private static StableKey CreateSnapshotStableKey(StableKey repositoryStableKey, string runId)
        {
            // Matching the assembler key keeps semantic facts scoped to the exact snapshot persisted after pipeline completion.
            return StableKeyGenerator.ForSummary(repositoryStableKey.Value, "ExtractionRun", runId);
        }
    }

    /// <summary>
    /// Provides local conversion helpers for semantic projection metadata.
    /// </summary>
    internal static class SemanticGraphProjectionMetadataExtensions
    {
        /// <summary>
        /// Converts graph metadata back into a string dictionary for nested projection helper reuse.
        /// </summary>
        /// <param name="metadata">The graph metadata to convert.</param>
        /// <returns>A string dictionary containing the metadata canonical JSON under one payload key.</returns>
        public static IReadOnlyDictionary<string, string> ToDictionary(this GraphMetadata metadata)
        {
            // GraphMetadata does not expose raw pairs, so nested helper calls preserve the already canonical payload as one semantic field.
            ArgumentNullException.ThrowIfNull(metadata);
            if (metadata.IsEmpty)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["metadataJson"] = metadata.ToCanonicalJson()
            };
        }
    }
}
