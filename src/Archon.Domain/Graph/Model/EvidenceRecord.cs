using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents a canonical explanation source for graph facts, findings, metrics, or generated summaries within one snapshot.
    /// </summary>
    public sealed class EvidenceRecord
    {
        /// <summary>
        /// Initializes a validated evidence record model.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the evidence.</param>
        /// <param name="stableKey">The deterministic stable key that identifies the evidence within the snapshot contract.</param>
        /// <param name="evidenceKind">The controlled evidence kind that classifies the explanation source.</param>
        /// <param name="filePath">The repository-relative path associated with the evidence.</param>
        /// <param name="startLine">The optional starting line number for source-backed evidence.</param>
        /// <param name="endLine">The optional ending line number for source-backed evidence.</param>
        /// <param name="symbolName">The optional symbol name associated with the evidence.</param>
        /// <param name="containingSymbol">The optional containing symbol associated with the evidence.</param>
        /// <param name="snippetHash">The optional hash of the evidence snippet.</param>
        /// <param name="snippetPreview">The optional human-readable snippet preview.</param>
        /// <param name="knowledgeKind">The knowledge classification that explains how Archon knows the evidence is valid.</param>
        /// <param name="confidence">The normalized confidence assigned to the evidence.</param>
        /// <param name="unknownState">The explicit unknown-state representation for the evidence.</param>
        /// <param name="metadata">Deterministic metadata for evidence details that are not normalized fields.</param>
        /// <param name="fingerprint">The deterministic fingerprint for diff-relevant evidence content.</param>
        public EvidenceRecord(
            StableKey snapshotStableKey,
            StableKey stableKey,
            EvidenceKind evidenceKind,
            RepositoryRelativePath filePath,
            int? startLine,
            int? endLine,
            string? symbolName,
            string? containingSymbol,
            string? snippetHash,
            string? snippetPreview,
            KnowledgeKind knowledgeKind,
            Confidence confidence,
            UnknownState unknownState,
            GraphMetadata metadata,
            Fingerprint fingerprint)
        {
            // Evidence construction enforces classification and unknown semantics because evidence is the explanation anchor for graph facts.
            ArgumentNullException.ThrowIfNull(evidenceKind);
            ArgumentNullException.ThrowIfNull(knowledgeKind);
            ArgumentNullException.ThrowIfNull(unknownState);
            ArgumentNullException.ThrowIfNull(metadata);
            GraphFactValidation.RequireLineRange(startLine, endLine);
            GraphFactValidation.RequireUnknownReasonWhenNeeded(knowledgeKind, unknownState, nameof(EvidenceRecord));

            SnapshotStableKey = snapshotStableKey;
            StableKey = stableKey;
            EvidenceKind = evidenceKind;
            FilePath = filePath;
            StartLine = startLine;
            EndLine = endLine;
            SymbolName = GraphFactValidation.OptionalString(symbolName);
            ContainingSymbol = GraphFactValidation.OptionalString(containingSymbol);
            SnippetHash = GraphFactValidation.OptionalString(snippetHash);
            SnippetPreview = GraphFactValidation.OptionalString(snippetPreview);
            KnowledgeKind = knowledgeKind;
            Confidence = confidence;
            UnknownState = unknownState;
            Metadata = metadata;
            Fingerprint = fingerprint;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that scopes the evidence.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the deterministic stable key that identifies the evidence within the snapshot contract.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the controlled evidence kind that classifies the explanation source.
        /// </summary>
        public EvidenceKind EvidenceKind { get; }

        /// <summary>
        /// Gets the repository-relative path associated with the evidence.
        /// </summary>
        public RepositoryRelativePath FilePath { get; }

        /// <summary>
        /// Gets the optional starting line number for source-backed evidence.
        /// </summary>
        public int? StartLine { get; }

        /// <summary>
        /// Gets the optional ending line number for source-backed evidence.
        /// </summary>
        public int? EndLine { get; }

        /// <summary>
        /// Gets the optional symbol name associated with the evidence.
        /// </summary>
        public string? SymbolName { get; }

        /// <summary>
        /// Gets the optional containing symbol associated with the evidence.
        /// </summary>
        public string? ContainingSymbol { get; }

        /// <summary>
        /// Gets the optional hash of the evidence snippet.
        /// </summary>
        public string? SnippetHash { get; }

        /// <summary>
        /// Gets the optional human-readable snippet preview.
        /// </summary>
        public string? SnippetPreview { get; }

        /// <summary>
        /// Gets the knowledge classification that explains how Archon knows the evidence is valid.
        /// </summary>
        public KnowledgeKind KnowledgeKind { get; }

        /// <summary>
        /// Gets the normalized confidence assigned to the evidence.
        /// </summary>
        public Confidence Confidence { get; }

        /// <summary>
        /// Gets the explicit unknown-state representation for the evidence.
        /// </summary>
        public UnknownState UnknownState { get; }

        /// <summary>
        /// Gets deterministic metadata for evidence details that are not normalized fields.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Gets the deterministic fingerprint for diff-relevant evidence content.
        /// </summary>
        public Fingerprint Fingerprint { get; }
    }
}
