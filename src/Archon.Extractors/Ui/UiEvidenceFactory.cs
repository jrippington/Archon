using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Extractors.Ui
{
    /// <summary>
    /// Creates source-backed evidence records for UI markup, code, and project artifact extraction.
    /// </summary>
    public static class UiEvidenceFactory
    {
        /// <summary>
        /// Creates an evidence record for a UI markup or code observation.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the evidence.</param>
        /// <param name="location">The repository-relative source location and snippet that support the evidence.</param>
        /// <param name="uiFramework">The UI framework name, such as <c>Blazor</c>.</param>
        /// <param name="uiArtifactKind">The UI artifact kind represented by the evidence.</param>
        /// <param name="detectionMode">The deterministic detection mode used by the extractor.</param>
        /// <param name="confidence">The confidence assigned to the evidence.</param>
        /// <param name="unknownState">The unknown-state assigned to the evidence.</param>
        /// <returns>An evidence record with redacted preview, stable snippet hash, and UI metadata.</returns>
        public static EvidenceRecord CreateMarkupEvidence(StableKey snapshotStableKey, UiSourceLocation location, string uiFramework, string uiArtifactKind, string detectionMode, Confidence confidence, UnknownState unknownState)
        {
            // Markup evidence stores only repository-relative paths and redacted snippets so graph output stays deterministic and credential-safe.
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(unknownState);

            string redactedSnippet = UiSecretRedactor.Redact(location.Snippet);
            string snippetHash = UiStableKeyBuilder.Hash(redactedSnippet);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["confidenceReason"] = unknownState.HasUnknownData ? "Partial static UI markup evidence." : "Static UI markup evidence.",
                ["detectionMode"] = RequireText(detectionMode, nameof(detectionMode)),
                ["extractorFamily"] = "Ui",
                ["uiArtifactKind"] = RequireText(uiArtifactKind, nameof(uiArtifactKind)),
                ["uiFramework"] = RequireText(uiFramework, nameof(uiFramework))
            });
            StableKey stableKey = UiStableKeyBuilder.Create("ui-evidence://", uiFramework, uiArtifactKind, detectionMode, location.RelativePath, location.StartLine?.ToString(System.Globalization.CultureInfo.InvariantCulture), location.EndLine?.ToString(System.Globalization.CultureInfo.InvariantCulture), snippetHash);

            return new EvidenceRecord(
                snapshotStableKey,
                stableKey,
                EvidenceKind.SourceCode,
                RepositoryRelativePath.Parse(location.RelativePath),
                location.StartLine,
                location.EndLine,
                uiArtifactKind,
                containingSymbol: null,
                snippetHash,
                redactedSnippet,
                KnowledgeKind.Fact,
                confidence,
                unknownState,
                metadata,
                FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, location.RelativePath, location.StartLine, location.EndLine, uiArtifactKind, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Requires meaningful text for evidence metadata fields.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name to report when validation fails.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Metadata fields are part of canonical graph facts, so missing values must be rejected before evidence is created.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("UI evidence text values cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}