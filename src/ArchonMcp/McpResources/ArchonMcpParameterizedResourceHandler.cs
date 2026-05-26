using ArchonMcp.McpEnvelope;
using ArchonMcp.McpProjects;
using ArchonMcp.McpSnapshotDiff;
using ArchonMcp.McpSymbols;

namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Handles parameterized MCP resources by delegating to the approved read-only tool abstractions.
    /// </summary>
    public sealed class ArchonMcpParameterizedResourceHandler : IArchonMcpParameterizedResourceHandler
    {
        /// <summary>
        /// Reads project context through the existing project description tool seam.
        /// </summary>
        private readonly IArchonMcpProjectTool _projectTool;

        /// <summary>
        /// Reads symbol context through the existing symbol description tool seam.
        /// </summary>
        private readonly IArchonMcpSymbolTool _symbolTool;

        /// <summary>
        /// Reads snapshot diff context through the existing snapshot diff tool seam.
        /// </summary>
        private readonly IArchonMcpSnapshotDiffTool _snapshotDiffTool;

        /// <summary>
        /// Creates a parameterized resource handler.
        /// </summary>
        /// <param name="projectTool">The project MCP tool used for project resource facts.</param>
        /// <param name="symbolTool">The symbol MCP tool used for symbol resource facts.</param>
        /// <param name="snapshotDiffTool">The snapshot diff MCP tool used for diff resource facts.</param>
        public ArchonMcpParameterizedResourceHandler(
            IArchonMcpProjectTool projectTool,
            IArchonMcpSymbolTool symbolTool,
            IArchonMcpSnapshotDiffTool snapshotDiffTool)
        {
            // Delegating to already-approved tools prevents a second mapping path from drifting from tool contracts or bypassing security semantics.
            _projectTool = projectTool ?? throw new ArgumentNullException(nameof(projectTool));
            _symbolTool = symbolTool ?? throw new ArgumentNullException(nameof(symbolTool));
            _snapshotDiffTool = snapshotDiffTool ?? throw new ArgumentNullException(nameof(snapshotDiffTool));
        }

        /// <inheritdoc />
        public async Task<object> ReadParameterizedResourceAsync(ArchonMcpResourceRequest request, CancellationToken cancellationToken)
        {
            // The parser already validated path shape and stable-key prefixes; this switch selects the matching read-only query workflow.
            ArgumentNullException.ThrowIfNull(request);
            object payload = request.Family switch
            {
                ArchonMcpResourceFamily.Project => await ReadProjectResourceAsync(request, cancellationToken).ConfigureAwait(false),
                ArchonMcpResourceFamily.Symbol => await ReadSymbolResourceAsync(request, cancellationToken).ConfigureAwait(false),
                ArchonMcpResourceFamily.Snapshot when string.Equals(request.Selector, "diff", StringComparison.OrdinalIgnoreCase) => await ReadSnapshotDiffResourceAsync(request, cancellationToken).ConfigureAwait(false),
                _ => ArchonMcpErrorResponse.Create(ArchonMcpResourceOperations.ReadResource, ArchonMcpErrorCategory.UnsupportedOperation, "The requested parameterized resource is not supported.", [new ArchonMcpSuggestedFollowUp("Use archon://project/{projectKey}, archon://symbol/{symbolKey}, or archon://snapshot/{snapshotId}/diff/{previousSnapshotId}.", "user.question", null)])
            };

            return payload;
        }

        /// <summary>
        /// Reads a project resource by invoking the project description tool with stable-key identity.
        /// </summary>
        /// <param name="request">The validated project resource request.</param>
        /// <param name="cancellationToken">The token that can cancel project query execution.</param>
        /// <returns>A project facts envelope or structured MCP error response.</returns>
        private async Task<object> ReadProjectResourceAsync(ArchonMcpResourceRequest request, CancellationToken cancellationToken)
        {
            // The project resource treats the path key as the exact project identity and uses an explicit latest selector for optional scope resolution.
            ArchonMcpDescribeProjectRequest toolRequest = new(
                request.ProjectStableKey,
                ProjectName: null,
                SnapshotSelector: "latest",
                request.RepositoryStableKey,
                request.SolutionStableKey);
            object payload = await _projectTool.DescribeProjectAsync(toolRequest, cancellationToken).ConfigureAwait(false);
            return RewriteEnvelopeOperation(payload, request.CanonicalUri, "Project resource context was read through archon.describe_project.");
        }

        /// <summary>
        /// Reads a symbol resource by invoking the symbol description tool with stable-key identity.
        /// </summary>
        /// <param name="request">The validated symbol resource request.</param>
        /// <param name="cancellationToken">The token that can cancel symbol query execution.</param>
        /// <returns>A symbol facts envelope or structured MCP error response.</returns>
        private async Task<object> ReadSymbolResourceAsync(ArchonMcpResourceRequest request, CancellationToken cancellationToken)
        {
            // Symbol resources deliberately avoid search-text lookup so the resource URI always identifies one stable symbol key.
            ArchonMcpDescribeSymbolRequest toolRequest = new(
                request.SymbolStableKey,
                SearchText: null,
                SnapshotSelector: "latest",
                request.RepositoryStableKey,
                request.SolutionStableKey);
            object payload = await _symbolTool.DescribeSymbolAsync(toolRequest, cancellationToken).ConfigureAwait(false);
            return RewriteEnvelopeOperation(payload, request.CanonicalUri, "Symbol resource context was read through archon.describe_symbol.");
        }

        /// <summary>
        /// Reads a snapshot diff resource by invoking the explicit snapshot diff tool workflow.
        /// </summary>
        /// <param name="request">The validated snapshot diff resource request.</param>
        /// <param name="cancellationToken">The token that can cancel diff query execution.</param>
        /// <returns>A snapshot diff facts envelope or structured MCP error response.</returns>
        private async Task<object> ReadSnapshotDiffResourceAsync(ArchonMcpResourceRequest request, CancellationToken cancellationToken)
        {
            // Snapshot diff resources are explicit comparisons; they never infer previous snapshots from the resource path.
            ArchonMcpSnapshotDiffRequest toolRequest = new(
                request.CurrentSnapshotStableKey,
                request.PreviousSnapshotStableKey,
                UseLatestComparableSnapshots: false,
                request.RepositoryStableKey,
                request.SolutionStableKey,
                Domains: null,
                ChangeKinds: null,
                ProjectStableKey: null,
                TargetStableKey: null,
                RecordKind: null,
                Severity: null,
                request.IncludeDetails,
                IncludeUnchangedDetails: false,
                request.Limit);
            object payload = await _snapshotDiffTool.GetSnapshotDiffAsync(toolRequest, cancellationToken).ConfigureAwait(false);
            return RewriteEnvelopeOperation(payload, request.CanonicalUri, "Snapshot diff resource context was read through archon.get_snapshot_diff.");
        }

        /// <summary>
        /// Rewrites successful delegated tool envelopes so resource reads share the common resource operation name and URI context.
        /// </summary>
        /// <param name="payload">The delegated tool payload.</param>
        /// <param name="resourceUri">The canonical resource URI being read.</param>
        /// <param name="warningMessage">The safe warning message that explains delegated resource mapping.</param>
        /// <returns>A resource-operation envelope when possible; otherwise the original structured payload.</returns>
        private static object RewriteEnvelopeOperation(object payload, string resourceUri, string warningMessage)
        {
            // Tool error responses are already structured and safe; only success envelopes need operation and URI context adjusted for resources.
            return payload switch
            {
                ArchonMcpEnvelope<ArchonMcpProjectFacts> projectEnvelope => RewriteEnvelope(projectEnvelope, resourceUri, warningMessage),
                ArchonMcpEnvelope<ArchonMcpSymbolFacts> symbolEnvelope => RewriteEnvelope(symbolEnvelope, resourceUri, warningMessage),
                ArchonMcpEnvelope<ArchonMcpSnapshotDiffFacts> diffEnvelope => RewriteEnvelope(diffEnvelope, resourceUri, warningMessage),
                _ => payload
            };
        }

        /// <summary>
        /// Creates a resource envelope from a delegated typed tool envelope.
        /// </summary>
        /// <typeparam name="TFacts">The facts type carried by the delegated envelope.</typeparam>
        /// <param name="envelope">The delegated tool envelope.</param>
        /// <param name="resourceUri">The canonical resource URI being read.</param>
        /// <param name="warningMessage">The safe warning message that explains delegated resource mapping.</param>
        /// <returns>A resource-operation envelope with unchanged facts, evidence, findings, unknowns, limits, and follow-ups.</returns>
        private static ArchonMcpEnvelope<TFacts> RewriteEnvelope<TFacts>(ArchonMcpEnvelope<TFacts> envelope, string resourceUri, string warningMessage)
        {
            // A small warning records that the resource surface reuses a tool-backed query abstraction without changing the underlying facts.
            List<ArchonMcpWarning> warnings = [.. envelope.Warnings];
            warnings.Add(new ArchonMcpWarning("resourceDelegatedToolMapping", warningMessage, resourceUri));
            return new ArchonMcpEnvelope<TFacts>(
                ArchonMcpResourceOperations.ReadResource,
                envelope.Snapshot,
                envelope.Summary,
                envelope.Confidence,
                envelope.Facts,
                envelope.Evidence,
                envelope.Findings,
                envelope.Unknowns,
                warnings,
                envelope.Limits,
                envelope.SuggestedFollowUps);
        }
    }
}