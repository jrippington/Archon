using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;

namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Resolves current snapshot context for MCP resources from the approved architecture snapshot writer abstraction.
    /// </summary>
    public sealed class ArchonMcpCurrentSnapshotProvider : IArchonMcpCurrentSnapshotProvider
    {
        /// <summary>
        /// Reads architecture snapshots through the application-layer persistence abstraction.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Creates a current snapshot provider.
        /// </summary>
        /// <param name="snapshotWriter">The application snapshot writer that may expose process-local diagnostic snapshots.</param>
        public ArchonMcpCurrentSnapshotProvider(IArchitectureSnapshotWriter snapshotWriter)
        {
            // The MCP host remains outside persistence internals by depending on the same application abstraction as query services.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <inheritdoc />
        public Task<ArchonMcpCurrentSnapshotResolution> ResolveCurrentSnapshotAsync(ArchonMcpCurrentSnapshotRequest request, CancellationToken cancellationToken)
        {
            // Current selection is explicit to one repository, optionally narrowed to one solution, and ties are reported as ambiguity.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            ExtractedArchitectureSnapshot[] scopedSnapshots = GetSnapshots()
                .Where(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.RepositoryStableKey.Value, request.RepositoryStableKey))
                .Where(snapshot => request.SolutionStableKey is null || snapshot.Solutions.Any(solution => StringComparer.Ordinal.Equals(solution.StableKey.Value, request.SolutionStableKey)))
                .Where(static snapshot => snapshot.SnapshotHeader is not null)
                .ToArray();
            if (scopedSnapshots.Length == 0)
            {
                return Task.FromResult(ArchonMcpCurrentSnapshotResolution.NotFound("No current snapshot matched the requested repository or solution scope."));
            }

            DateTimeOffset latestCompleted = scopedSnapshots.Max(static snapshot => snapshot.SnapshotHeader!.CompletedUtc ?? snapshot.SnapshotHeader.StartedUtc);
            DateTimeOffset latestStarted = scopedSnapshots
                .Where(snapshot => (snapshot.SnapshotHeader!.CompletedUtc ?? snapshot.SnapshotHeader.StartedUtc) == latestCompleted)
                .Max(static snapshot => snapshot.SnapshotHeader!.StartedUtc);
            ExtractedArchitectureSnapshot[] tied = scopedSnapshots
                .Where(snapshot => (snapshot.SnapshotHeader!.CompletedUtc ?? snapshot.SnapshotHeader.StartedUtc) == latestCompleted)
                .Where(snapshot => snapshot.SnapshotHeader!.StartedUtc == latestStarted)
                .ToArray();
            if (tied.Length > 1)
            {
                string[] candidates = tied.Select(static snapshot => snapshot.SnapshotHeader!.StableKey.Value).OrderBy(key => key, StringComparer.Ordinal).ToArray();
                return Task.FromResult(ArchonMcpCurrentSnapshotResolution.Ambiguous(candidates, "Current snapshot selection matched multiple snapshots with the same completion and start timestamp."));
            }

            return Task.FromResult(ArchonMcpCurrentSnapshotResolution.Success(MapSnapshot(tied[0])));
        }

        /// <summary>
        /// Reads snapshots from the in-memory fallback writer when available.
        /// </summary>
        /// <returns>A read-only list of snapshots available to MCP resource resolution.</returns>
        private IReadOnlyList<ExtractedArchitectureSnapshot> GetSnapshots()
        {
            // Infrastructure-backed stores can replace query services later; the current resource slice uses the repository-standard in-memory seam.
            return _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics()
                : [];
        }

        /// <summary>
        /// Maps one extracted architecture snapshot into safe current snapshot context.
        /// </summary>
        /// <param name="snapshot">The selected extracted architecture snapshot.</param>
        /// <returns>Safe MCP current snapshot context.</returns>
        private static ArchonMcpCurrentSnapshotContext MapSnapshot(ExtractedArchitectureSnapshot snapshot)
        {
            // The mapped context contains only stable keys, timestamps, status, and section counts; repository paths and remotes are omitted.
            return new ArchonMcpCurrentSnapshotContext(
                snapshot.SnapshotHeader!.StableKey.Value,
                snapshot.SnapshotHeader.RepositoryStableKey.Value,
                snapshot.Solutions.Select(static solution => solution.StableKey.Value).OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                snapshot.SnapshotHeader.BranchName,
                snapshot.SnapshotHeader.CommitSha,
                snapshot.SnapshotHeader.StartedUtc,
                snapshot.SnapshotHeader.CompletedUtc,
                snapshot.SnapshotHeader.Status,
                snapshot.Nodes.Count,
                snapshot.Edges.Count,
                snapshot.Rules.Count,
                snapshot.Findings.Count,
                snapshot.Metrics.Count,
                snapshot.Evidence.Count,
                snapshot.Warnings.Count + snapshot.SnapshotHeader.Warnings.Count,
                snapshot.Errors.Count + snapshot.SnapshotHeader.Errors.Count);
        }
    }
}
