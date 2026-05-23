using System.Security.Cryptography;
using System.Text;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Extractors.Integrations.Foundation
{
    /// <summary>
    /// Projects evidence-backed external integration observations into the shared Archon graph contract.
    /// </summary>
    /// <remarks>
    /// This foundation extractor performs no live network, broker, storage, SMTP, payment-provider, or credential validation work. It only converts deterministic observations supplied by static analyzers into snapshot facts.
    /// </remarks>
    public sealed class ExternalIntegrationFoundationExtractor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalIntegrationFoundationExtractor" /> class.
        /// </summary>
        public ExternalIntegrationFoundationExtractor()
        {
            // The foundation extractor is stateless so it can be reused safely across API-triggered extraction runs.
        }

        /// <summary>
        /// Projects the supplied integration observations into graph facts and diagnostics.
        /// </summary>
        /// <param name="request">The extraction request containing snapshot identity, repository root, and deterministic observations.</param>
        /// <param name="cancellationToken">The cancellation token that stops projection between observations.</param>
        /// <returns>The graph projection result containing a partial integration snapshot.</returns>
        public ExternalIntegrationExtractionResult Extract(ExternalIntegrationExtractionRequest request, CancellationToken cancellationToken)
        {
            // Projection is deliberately deterministic: each observation is handled in input order, while the accumulator sorts graph facts by stable key.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();
            foreach (ExternalIntegrationObservation observation in request.Observations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccumulateObservation(request, observation, accumulator);
            }

            return new ExternalIntegrationExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Adds graph facts for one integration observation to the accumulator.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity and repository-relative paths.</param>
        /// <param name="observation">The deterministic integration observation to project.</param>
        /// <param name="accumulator">The accumulator receiving graph facts and diagnostics.</param>
        private static void AccumulateObservation(ExternalIntegrationExtractionRequest request, ExternalIntegrationObservation observation, ArchitectureSnapshotAccumulator accumulator)
        {
            // A single observation contributes evidence, the integration target node, a primary relationship, and optionally a USES_CONFIG relationship.
            ArgumentNullException.ThrowIfNull(observation);
            KnowledgeKind knowledgeKind = string.IsNullOrWhiteSpace(observation.UnknownReason) ? KnowledgeKind.Fact : KnowledgeKind.Unknown;
            Confidence confidence = knowledgeKind == KnowledgeKind.Fact ? Confidence.High : Confidence.Medium;
            UnknownState unknownState = knowledgeKind == KnowledgeKind.Fact ? UnknownState.Known : UnknownState.Unknown(observation.UnknownReason);
            StableKey targetStableKey = CreateTargetStableKey(request, observation);
            string displayName = string.IsNullOrWhiteSpace(observation.TargetName) ? CreateUnknownDisplayName(observation.TargetKind) : observation.TargetName.Trim();
            GraphMetadata metadata = CreateMetadata(observation);
            StableKey evidenceStableKey = ExternalIntegrationStableKey.ForEvidence(targetStableKey, request.RepositoryRootDirectory, observation.EvidenceFilePath, observation.EvidenceStartLine, observation.EvidenceEndLine, observation.DetectionMode);
            EvidenceRecord evidence = CreateEvidence(request, observation, evidenceStableKey, knowledgeKind, confidence, unknownState, metadata);
            ArchitectureNode targetNode = CreateTargetNode(request.SnapshotStableKey, observation, targetStableKey, displayName, evidence.StableKey, knowledgeKind, confidence, unknownState, metadata);
            ArchitectureEdge relationship = CreateRelationship(request.SnapshotStableKey, observation.RelationshipKind, observation.SourceNodeStableKey, targetStableKey, evidence.StableKey, knowledgeKind, confidence, unknownState, metadata);

            accumulator.AddEvidence(evidence).AddNode(targetNode).AddEdge(relationship);
            if (observation.ConfigurationKeyStableKey is StableKey configurationKeyStableKey)
            {
                ArchitectureEdge configurationRelationship = CreateRelationship(request.SnapshotStableKey, EdgeKind.UsesConfig, targetStableKey.Value, configurationKeyStableKey, evidence.StableKey, KnowledgeKind.Fact, Confidence.High, UnknownState.Known, CreateConfigurationMetadata(observation));
                accumulator.AddEdge(configurationRelationship);
            }

            if (!string.IsNullOrWhiteSpace(observation.UnknownReason))
            {
                accumulator.AddWarning($"WP010 external integration extraction recorded an unknown {observation.TargetKind} target because {observation.UnknownReason.Trim()}");
            }
        }

        /// <summary>
        /// Creates the stable key for the observed integration target.
        /// </summary>
        /// <param name="request">The extraction request supplying repository root information.</param>
        /// <param name="observation">The observation whose target key is being created.</param>
        /// <returns>A deterministic target stable key.</returns>
        private static StableKey CreateTargetStableKey(ExternalIntegrationExtractionRequest request, ExternalIntegrationObservation observation)
        {
            // Known targets use logical provider/name identity; unknown service targets use evidence location so placeholder identities remain stable.
            bool known = !string.IsNullOrWhiteSpace(observation.TargetName);
            return observation.TargetKind switch
            {
                ExternalIntegrationTargetKind.ExternalService when known => ExternalIntegrationStableKey.ForExternalService(request.RepositoryRootDirectory, observation.TargetName),
                ExternalIntegrationTargetKind.ExternalService => ExternalIntegrationStableKey.ForUnknownExternalService(request.RepositoryRootDirectory, observation.EvidenceFilePath, observation.EvidenceStartLine ?? 0, observation.DetectionMode),
                ExternalIntegrationTargetKind.Queue when known => ExternalIntegrationStableKey.ForQueue(observation.Provider, observation.TargetName),
                ExternalIntegrationTargetKind.Queue => new StableKey($"queue://unknown/{CreateHash(observation.SourceNodeStableKey + observation.EvidenceFilePath + observation.EvidenceStartLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) + observation.DetectionMode)}"),
                ExternalIntegrationTargetKind.Topic when known => ExternalIntegrationStableKey.ForTopic(observation.Provider, observation.TargetName),
                ExternalIntegrationTargetKind.Topic => new StableKey($"topic://unknown/{CreateHash(observation.SourceNodeStableKey + observation.EvidenceFilePath + observation.EvidenceStartLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) + observation.DetectionMode)}"),
                _ => throw new ArgumentOutOfRangeException(nameof(observation), observation.TargetKind, "Unsupported external integration target kind.")
            };
        }

        /// <summary>
        /// Creates a target node for an integration observation.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="observation">The integration observation being projected.</param>
        /// <param name="targetStableKey">The stable key assigned to the target node.</param>
        /// <param name="displayName">The developer-facing target display name.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key for the node.</param>
        /// <param name="knowledgeKind">The knowledge classification for the node.</param>
        /// <param name="confidence">The confidence assigned to the node.</param>
        /// <param name="unknownState">The unknown-state representation for the node.</param>
        /// <param name="metadata">The metadata attached to the node.</param>
        /// <returns>An architecture node representing the integration target.</returns>
        private static ArchitectureNode CreateTargetNode(StableKey snapshotStableKey, ExternalIntegrationObservation observation, StableKey targetStableKey, string displayName, StableKey evidenceStableKey, KnowledgeKind knowledgeKind, Confidence confidence, UnknownState unknownState, GraphMetadata metadata)
        {
            // Node kind is intentionally constrained to the WP010 foundation target categories.
            NodeKind nodeKind = observation.TargetKind switch
            {
                ExternalIntegrationTargetKind.ExternalService => NodeKind.ExternalService,
                ExternalIntegrationTargetKind.Queue => NodeKind.Queue,
                ExternalIntegrationTargetKind.Topic => NodeKind.Topic,
                _ => throw new ArgumentOutOfRangeException(nameof(observation), observation.TargetKind, "Unsupported external integration target kind.")
            };
            string searchName = displayName.Equals("<unknown external service>", StringComparison.Ordinal) || displayName.Equals("<unknown queue>", StringComparison.Ordinal) || displayName.Equals("<unknown topic>", StringComparison.Ordinal)
                ? targetStableKey.Value
                : displayName;
            return new ArchitectureNode(snapshotStableKey, targetStableKey, nodeKind, displayName, observation.TargetName, searchName, null, null, null, knowledgeKind, ownership: null, externalCategory: observation.IntegrationCategory, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, observation.TargetName, searchName, knowledgeKind, metadata));
        }

        /// <summary>
        /// Creates the primary source-to-target relationship for an integration observation.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the edge.</param>
        /// <param name="edgeKind">The relationship kind to emit.</param>
        /// <param name="sourceStableKey">The source node stable key.</param>
        /// <param name="targetStableKey">The target node stable key.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key for the edge.</param>
        /// <param name="knowledgeKind">The knowledge classification for the edge.</param>
        /// <param name="confidence">The confidence assigned to the edge.</param>
        /// <param name="unknownState">The unknown-state representation for the edge.</param>
        /// <param name="metadata">The metadata attached to the edge.</param>
        /// <returns>An architecture edge representing the integration relationship.</returns>
        private static ArchitectureEdge CreateRelationship(StableKey snapshotStableKey, EdgeKind edgeKind, string sourceStableKey, StableKey targetStableKey, StableKey evidenceStableKey, KnowledgeKind knowledgeKind, Confidence confidence, UnknownState unknownState, GraphMetadata metadata)
        {
            // The relationship key mirrors graph semantics and endpoints rather than source order.
            StableKey sourceKey = new(sourceStableKey);
            StableKey edgeStableKey = ExternalIntegrationStableKey.ForRelationship(edgeKind.Value, sourceKey.Value, targetStableKey.Value);
            return new ArchitectureEdge(snapshotStableKey, edgeStableKey, edgeKind, sourceKey, targetStableKey, isDirect: true, knowledgeKind, confidence, unknownState, evidenceStableKey, metadata, FingerprintGenerator.ForEdge(edgeKind, sourceKey, targetStableKey, isDirect: true, knowledgeKind, metadata));
        }

        /// <summary>
        /// Creates the evidence record for an integration observation.
        /// </summary>
        /// <param name="request">The extraction request supplying snapshot and repository-root values.</param>
        /// <param name="observation">The observation whose evidence is being projected.</param>
        /// <param name="evidenceStableKey">The stable key assigned to the evidence record.</param>
        /// <param name="knowledgeKind">The knowledge classification for the evidence.</param>
        /// <param name="confidence">The confidence assigned to the evidence.</param>
        /// <param name="unknownState">The unknown-state representation for the evidence.</param>
        /// <param name="metadata">The metadata attached to the evidence.</param>
        /// <returns>An evidence record explaining the integration graph facts.</returns>
        private static EvidenceRecord CreateEvidence(ExternalIntegrationExtractionRequest request, ExternalIntegrationObservation observation, StableKey evidenceStableKey, KnowledgeKind knowledgeKind, Confidence confidence, UnknownState unknownState, GraphMetadata metadata)
        {
            // Evidence paths are normalized into repository-relative form before entering graph contracts.
            string relativePath = NormalizeRepositoryRelativePath(request.RepositoryRootDirectory, observation.EvidenceFilePath);
            string? snippetPreview = RedactSnippet(observation.SnippetPreview);
            string? snippetHash = string.IsNullOrWhiteSpace(snippetPreview) ? null : CreateHash(snippetPreview);
            return new EvidenceRecord(request.SnapshotStableKey, evidenceStableKey, EvidenceKind.SourceCode, RepositoryRelativePath.Parse(relativePath), observation.EvidenceStartLine, observation.EvidenceEndLine, observation.SymbolName, observation.ContainingSymbol, snippetHash, snippetPreview, knowledgeKind, confidence, unknownState, metadata, FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, relativePath, observation.EvidenceStartLine, observation.EvidenceEndLine, observation.SymbolName, knowledgeKind, metadata));
        }

        /// <summary>
        /// Creates deterministic metadata for integration nodes, relationships, and evidence records.
        /// </summary>
        /// <param name="observation">The observation supplying metadata values.</param>
        /// <returns>A metadata object containing non-normalized integration details.</returns>
        private static GraphMetadata CreateMetadata(ExternalIntegrationObservation observation)
        {
            // Metadata is intentionally descriptive but avoids reserved first-class graph fields and secret-bearing source values.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["confidenceReason"] = string.IsNullOrWhiteSpace(observation.UnknownReason) ? "Integration target is directly supported by static source evidence." : observation.UnknownReason.Trim(),
                ["detectionMode"] = observation.DetectionMode,
                ["integrationCategory"] = observation.IntegrationCategory,
                ["integrationRole"] = observation.Role,
                ["provider"] = observation.Provider,
                ["configurationKey"] = observation.ConfigurationKeyStableKey?.Value.Replace("config://", string.Empty, StringComparison.Ordinal),
                ["targetKind"] = observation.TargetKind.ToString()
            };
            AddRoleMetadata(values, observation.Role);
            AddOptional(values, "targetName", observation.TargetName);
            AddOptional(values, "configurationKeyStableKey", observation.ConfigurationKeyStableKey?.Value);
            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Adds structured metadata carried inside the integration role field by richer detector slices.
        /// </summary>
        /// <param name="values">The metadata dictionary receiving structured role properties.</param>
        /// <param name="role">The detector role value, optionally containing semicolon-delimited key-value tokens.</param>
        private static void AddRoleMetadata(Dictionary<string, object?> values, string role)
        {
            // Work Item 1 kept the foundation observation compact; later detectors can safely add structured hints by using role=value;key=value tokens.
            foreach (string part in role.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
                if (separatorIndex <= 0 || separatorIndex == part.Length - 1)
                {
                    continue;
                }

                string key = part[..separatorIndex];
                string value = part[(separatorIndex + 1)..];
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    values[key.Trim()] = value.Trim();
                }
            }
        }

        /// <summary>
        /// Creates deterministic metadata for a configuration dependency relationship.
        /// </summary>
        /// <param name="observation">The observation supplying integration context.</param>
        /// <returns>A metadata object describing configuration usage.</returns>
        private static GraphMetadata CreateConfigurationMetadata(ExternalIntegrationObservation observation)
        {
            // Configuration dependency metadata is narrower so fingerprints reflect that this edge only models USES_CONFIG.
            return GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = "Integration target is associated with an observed configuration-key dependency.",
                ["detectionMode"] = observation.DetectionMode,
                ["integrationCategory"] = observation.IntegrationCategory,
                ["integrationRole"] = "ConfigurationDependency",
                ["provider"] = observation.Provider,
                ["targetKind"] = observation.TargetKind.ToString()
            });
        }

        /// <summary>
        /// Creates a display name for an unknown integration target.
        /// </summary>
        /// <param name="targetKind">The target kind requiring an unknown display name.</param>
        /// <returns>A human-readable unknown display name.</returns>
        private static string CreateUnknownDisplayName(ExternalIntegrationTargetKind targetKind)
        {
            // Unknown labels are intentionally explicit so UI and API consumers do not mistake them for real service names.
            return targetKind switch
            {
                ExternalIntegrationTargetKind.ExternalService => "<unknown external service>",
                ExternalIntegrationTargetKind.Queue => "<unknown queue>",
                ExternalIntegrationTargetKind.Topic => "<unknown topic>",
                _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Unsupported external integration target kind.")
            };
        }

        /// <summary>
        /// Normalizes an evidence path into repository-relative form.
        /// </summary>
        /// <param name="repositoryRootDirectory">The analyzed repository root.</param>
        /// <param name="path">The candidate absolute or repository-relative evidence path.</param>
        /// <returns>A repository-relative slash-separated path.</returns>
        private static string NormalizeRepositoryRelativePath(string repositoryRootDirectory, string path)
        {
            // Absolute paths are reduced under the submitted repository root before entering evidence contracts.
            string normalized = path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            if (Path.IsPathRooted(path))
            {
                normalized = Path.GetRelativePath(repositoryRootDirectory, path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            }

            return RepositoryRelativePath.Parse(normalized).Value;
        }

        /// <summary>
        /// Redacts obvious secret-bearing assignment snippets before evidence preview storage.
        /// </summary>
        /// <param name="snippetPreview">The optional source snippet preview.</param>
        /// <returns>A redacted snippet preview or <see langword="null" /> when none was supplied.</returns>
        private static string? RedactSnippet(string? snippetPreview)
        {
            // Work Item 1 uses a conservative line-level redaction guard; later detectors can add richer token-aware redaction.
            if (string.IsNullOrWhiteSpace(snippetPreview))
            {
                return null;
            }

            string trimmed = snippetPreview.Trim();
            return trimmed.Contains("secret", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("password", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("token", StringComparison.OrdinalIgnoreCase)
                ? "<redacted integration evidence snippet>"
                : trimmed;
        }

        /// <summary>
        /// Adds an optional metadata property when a value is present.
        /// </summary>
        /// <param name="values">The metadata dictionary receiving the value.</param>
        /// <param name="key">The metadata property key.</param>
        /// <param name="value">The optional metadata property value.</param>
        private static void AddOptional(Dictionary<string, object?> values, string key, string? value)
        {
            // Omitting absent values keeps metadata from implying facts that were not observed.
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value.Trim();
            }
        }

        /// <summary>
        /// Creates a lowercase SHA-256 hash for evidence and placeholder discriminators.
        /// </summary>
        /// <param name="value">The canonical value to hash.</param>
        /// <returns>A lowercase hexadecimal SHA-256 hash.</returns>
        private static string CreateHash(string value)
        {
            // Hashing keeps keys deterministic while avoiding long source snippets in identity strings.
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
