using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;

namespace Archon.Extractors.Integrations.Foundation
{
    /// <summary>
    /// Describes one evidence-backed external integration observation ready for graph projection by the external-integration foundation extractor.
    /// </summary>
    /// <remarks>
    /// Later detector work items can create observations from HTTP, REST, messaging, storage, email, payment, and generated-client evidence while this foundation type keeps graph projection consistent.
    /// </remarks>
    /// <param name="TargetKind">The integration graph target category to emit.</param>
    /// <param name="TargetName">The known target name, or <see langword="null" /> when the target must be represented as an explicit unknown.</param>
    /// <param name="IntegrationCategory">The high-level integration category, such as Http, Messaging, Storage, Email, or Payment.</param>
    /// <param name="Provider">The provider, transport, library, or framework that supplied detection evidence.</param>
    /// <param name="Role">The integration role, such as OutboundClient, Producer, Consumer, Handler, or ConfigurationDependency.</param>
    /// <param name="SourceNodeStableKey">The stable key of the project, method, endpoint, handler, or other source node that owns the relationship.</param>
    /// <param name="RelationshipKind">The graph relationship kind to create from the source node to the integration target.</param>
    /// <param name="EvidenceFilePath">The source evidence file path, either repository-relative or absolute under the analyzed repository root.</param>
    /// <param name="EvidenceStartLine">The one-based source evidence start line.</param>
    /// <param name="EvidenceEndLine">The one-based source evidence end line.</param>
    /// <param name="SymbolName">The optional source symbol name that anchors evidence.</param>
    /// <param name="ContainingSymbol">The optional containing symbol that anchors evidence.</param>
    /// <param name="SnippetPreview">The optional redacted source snippet preview for explanation.</param>
    /// <param name="DetectionMode">The deterministic detector mode that produced the observation.</param>
    /// <param name="UnknownReason">The explicit reason the target remains unknown, when applicable.</param>
    /// <param name="ConfigurationKeyStableKey">The optional configuration-key node stable key that the integration target uses.</param>
    public sealed record ExternalIntegrationObservation(
        ExternalIntegrationTargetKind TargetKind,
        string? TargetName,
        string IntegrationCategory,
        string Provider,
        string Role,
        string SourceNodeStableKey,
        EdgeKind RelationshipKind,
        string EvidenceFilePath,
        int? EvidenceStartLine,
        int? EvidenceEndLine,
        string? SymbolName,
        string? ContainingSymbol,
        string? SnippetPreview,
        string DetectionMode,
        string? UnknownReason,
        StableKey? ConfigurationKeyStableKey);
}
