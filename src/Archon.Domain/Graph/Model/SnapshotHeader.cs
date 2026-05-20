using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents one architecture extraction run and scopes all facts emitted by that run.
    /// </summary>
    /// <param name="StableKey">The deterministic stable key that identifies the snapshot.</param>
    /// <param name="RepositoryStableKey">The stable key of the repository extracted by the snapshot.</param>
    /// <param name="BranchName">The optional branch name extracted for the snapshot.</param>
    /// <param name="CommitSha">The optional source-control commit SHA extracted for the snapshot.</param>
    /// <param name="StartedUtc">The UTC time at which extraction started.</param>
    /// <param name="CompletedUtc">The optional UTC time at which extraction completed.</param>
    /// <param name="ExtractionVersion">The extraction contract or tool version that produced the snapshot.</param>
    /// <param name="Status">The snapshot status, such as Completed or Failed.</param>
    /// <param name="Warnings">Warnings produced during extraction without requiring persistence-specific records.</param>
    /// <param name="Errors">Errors produced during extraction without requiring persistence-specific records.</param>
    /// <param name="Metadata">Deterministic metadata for snapshot details that are not normalized fields.</param>
    public sealed class SnapshotHeader
    {
        /// <summary>
        /// Initializes a validated snapshot header model.
        /// </summary>
        /// <param name="stableKey">The deterministic stable key that identifies the snapshot.</param>
        /// <param name="repositoryStableKey">The stable key of the repository extracted by the snapshot.</param>
        /// <param name="branchName">The optional branch name extracted for the snapshot.</param>
        /// <param name="commitSha">The optional source-control commit SHA extracted for the snapshot.</param>
        /// <param name="startedUtc">The UTC time at which extraction started.</param>
        /// <param name="completedUtc">The optional UTC time at which extraction completed.</param>
        /// <param name="extractionVersion">The extraction contract or tool version that produced the snapshot.</param>
        /// <param name="status">The snapshot status, such as Completed or Failed.</param>
        /// <param name="warnings">Warnings produced during extraction without requiring persistence-specific records.</param>
        /// <param name="errors">Errors produced during extraction without requiring persistence-specific records.</param>
        /// <param name="metadata">Deterministic metadata for snapshot details that are not normalized fields.</param>
        public SnapshotHeader(
            StableKey stableKey,
            StableKey repositoryStableKey,
            string? branchName,
            string? commitSha,
            DateTimeOffset startedUtc,
            DateTimeOffset? completedUtc,
            string? extractionVersion,
            string? status,
            IEnumerable<string>? warnings,
            IEnumerable<string>? errors,
            GraphMetadata metadata)
        {
            // Snapshot warnings and errors are immutable lists so accumulation can preserve extraction diagnostics safely.
            ArgumentNullException.ThrowIfNull(metadata);
            StableKey = stableKey;
            RepositoryStableKey = repositoryStableKey;
            BranchName = GraphFactValidation.OptionalString(branchName);
            CommitSha = GraphFactValidation.OptionalString(commitSha);
            StartedUtc = startedUtc;
            CompletedUtc = completedUtc;
            ExtractionVersion = GraphFactValidation.RequiredString(extractionVersion, nameof(extractionVersion));
            Status = GraphFactValidation.RequiredString(status, nameof(status));
            Warnings = NormalizeMessages(warnings);
            Errors = NormalizeMessages(errors);
            Metadata = metadata;
        }

        /// <summary>
        /// Gets the deterministic stable key that identifies the snapshot.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the stable key of the repository extracted by the snapshot.
        /// </summary>
        public StableKey RepositoryStableKey { get; }

        /// <summary>
        /// Gets the optional branch name extracted for the snapshot.
        /// </summary>
        public string? BranchName { get; }

        /// <summary>
        /// Gets the optional source-control commit SHA extracted for the snapshot.
        /// </summary>
        public string? CommitSha { get; }

        /// <summary>
        /// Gets the UTC time at which extraction started.
        /// </summary>
        public DateTimeOffset StartedUtc { get; }

        /// <summary>
        /// Gets the optional UTC time at which extraction completed.
        /// </summary>
        public DateTimeOffset? CompletedUtc { get; }

        /// <summary>
        /// Gets the extraction contract or tool version that produced the snapshot.
        /// </summary>
        public string ExtractionVersion { get; }

        /// <summary>
        /// Gets the snapshot status, such as Completed or Failed.
        /// </summary>
        public string Status { get; }

        /// <summary>
        /// Gets warnings produced during extraction without requiring persistence-specific records.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Gets errors produced during extraction without requiring persistence-specific records.
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Gets deterministic metadata for snapshot details that are not normalized fields.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Normalizes optional diagnostic messages into an immutable read-only list.
        /// </summary>
        /// <param name="messages">The diagnostic messages to normalize.</param>
        /// <returns>A read-only list of trimmed non-empty diagnostic messages.</returns>
        private static IReadOnlyList<string> NormalizeMessages(IEnumerable<string>? messages)
        {
            // Blank diagnostics are discarded because they cannot help explain snapshot status.
            return messages is null
                ? []
                : messages.Where(message => !string.IsNullOrWhiteSpace(message)).Select(message => message.Trim()).ToArray();
        }
    }
}
