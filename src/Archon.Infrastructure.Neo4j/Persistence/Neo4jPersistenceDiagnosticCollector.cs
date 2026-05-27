using System.Diagnostics;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Runs;
using Archon.Application.Graph.Persistence;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Collects low-overhead persistence diagnostics for one Neo4j snapshot write attempt.
    /// </summary>
    /// <remarks>
    /// The collector deliberately stores only stable stage names, aggregate counts, and elapsed durations. It never records Cypher text,
    /// driver exception messages, connection strings, or parameter payloads, which keeps diagnostics safe for extraction status responses.
    /// </remarks>
    internal sealed class Neo4jPersistenceDiagnosticCollector
    {
        /// <summary>
        /// Names the total diagnostic timing that spans the whole persistence attempt.
        /// </summary>
        public const string TotalStageName = "Persistence.Total";

        /// <summary>
        /// Stores completed sub-stage timings in the exact order they finished.
        /// </summary>
        private readonly List<ExtractionRunTiming> _timings = [];

        /// <summary>
        /// Measures total persistence duration with a monotonic clock that is not affected by system clock changes.
        /// </summary>
        private readonly Stopwatch _totalStopwatch = Stopwatch.StartNew();

        /// <summary>
        /// Holds the most recent count snapshot built from already-available input data and completed write counters.
        /// </summary>
        private ExtractionRunPersistenceCounts _counts;

        /// <summary>
        /// Tracks whether the total timing has already been appended to avoid duplicate total entries on nested failure paths.
        /// </summary>
        private bool _totalCompleted;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jPersistenceDiagnosticCollector"/> class.
        /// </summary>
        /// <param name="snapshot">The snapshot whose already-materialized sections should seed count diagnostics.</param>
        public Neo4jPersistenceDiagnosticCollector(ExtractedArchitectureSnapshot snapshot)
        {
            // Constructor count capture is intentionally cheap because all section collections are already in memory at persistence handoff.
            ArgumentNullException.ThrowIfNull(snapshot);
            _counts = CreateInputCounts(snapshot, operationCount: null, batchCount: null);
        }

        /// <summary>
        /// Measures an asynchronous persistence sub-stage and appends its timing when the stage exits successfully or with an exception.
        /// </summary>
        /// <typeparam name="TResult">The result produced by the measured stage.</typeparam>
        /// <param name="stageName">The stable display-style diagnostic stage name.</param>
        /// <param name="action">The asynchronous work to measure.</param>
        /// <returns>The result produced by <paramref name="action"/>.</returns>
        public async Task<TResult> MeasureAsync<TResult>(string stageName, Func<Task<TResult>> action)
        {
            // Timing is appended in a finally block so partial diagnostics survive controlled persistence failures.
            ArgumentNullException.ThrowIfNull(action);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                return await action().ConfigureAwait(false);
            }
            finally
            {
                AddTiming(stageName, stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Measures an asynchronous persistence sub-stage that does not produce a result.
        /// </summary>
        /// <param name="stageName">The stable display-style diagnostic stage name.</param>
        /// <param name="action">The asynchronous work to measure.</param>
        /// <returns>A task that completes after <paramref name="action"/> completes and timing has been appended.</returns>
        public async Task MeasureAsync(string stageName, Func<Task> action)
        {
            // The void-returning overload keeps writer instrumentation readable while preserving the same finally-based behavior.
            ArgumentNullException.ThrowIfNull(action);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                await action().ConfigureAwait(false);
            }
            finally
            {
                AddTiming(stageName, stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Measures a synchronous persistence preparation sub-stage and appends its timing when the stage exits.
        /// </summary>
        /// <typeparam name="TResult">The result produced by the measured stage.</typeparam>
        /// <param name="stageName">The stable display-style diagnostic stage name.</param>
        /// <param name="action">The synchronous work to measure.</param>
        /// <returns>The result produced by <paramref name="action"/>.</returns>
        public TResult Measure<TResult>(string stageName, Func<TResult> action)
        {
            // Synchronous measurement covers validation and canonicalization without wrapping them in artificial tasks.
            ArgumentNullException.ThrowIfNull(action);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                return action();
            }
            finally
            {
                AddTiming(stageName, stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Updates diagnostic count details after the write transaction has determined operation and relationship counts.
        /// </summary>
        /// <param name="snapshot">The snapshot whose in-memory section counts should remain the count source of truth.</param>
        /// <param name="counts">The completed persistence counts produced by the writer.</param>
        /// <param name="operationCount">The number of Neo4j statements executed during the write attempt when known.</param>
        /// <param name="batchCount">The number of write batches or transactions executed during the write attempt when known.</param>
        public void UpdateCompletedCounts(ExtractedArchitectureSnapshot snapshot, SnapshotPersistenceCounts counts, int operationCount, int batchCount)
        {
            // Completed counts combine cheap snapshot section counts with writer counters already calculated for the public result.
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(counts);
            _counts = CreateCompletedCounts(snapshot, counts, operationCount, batchCount);
        }

        /// <summary>
        /// Builds the immutable diagnostics object for a completed or failed persistence attempt.
        /// </summary>
        /// <param name="completed">A value indicating whether the persistence attempt reached successful durable finalization.</param>
        /// <returns>The diagnostics collected so far, including a total timing appended exactly once.</returns>
        public ExtractionRunPersistenceDiagnostics Complete(bool completed)
        {
            // Total timing is appended last so consumers can read the ordered sub-stage sequence before the all-up duration.
            if (!completed)
            {
                _counts = AddPersistenceErrorCount(_counts);
            }

            CompleteTotalTiming();
            return new ExtractionRunPersistenceDiagnostics(_timings, _counts, completed);
        }

        /// <summary>
        /// Records an applicable sub-stage whose work was already represented by the in-memory handoff and has no separate operation.
        /// </summary>
        /// <param name="stageName">The stable display-style diagnostic stage name.</param>
        public void RecordAlreadyMaterializedStage(string stageName)
        {
            // Zero-duration timings are valid when the adapter can identify a stage but no additional work is performed for it.
            AddTiming(stageName, elapsedMilliseconds: 0);
        }

        /// <summary>
        /// Appends one validated timing record using a UTC completion timestamp.
        /// </summary>
        /// <param name="stageName">The stable display-style diagnostic stage name.</param>
        /// <param name="elapsedMilliseconds">The monotonic elapsed duration for the measured stage.</param>
        private void AddTiming(string stageName, long elapsedMilliseconds)
        {
            // ExtractionRunTiming performs stage-name and duration normalization shared with the application status contract.
            _timings.Add(new ExtractionRunTiming(stageName, elapsedMilliseconds, DateTimeOffset.UtcNow));
        }

        /// <summary>
        /// Appends the total persistence timing if it has not already been recorded.
        /// </summary>
        private void CompleteTotalTiming()
        {
            // The guard allows success and failure paths to call Complete independently without producing duplicate total timings.
            if (_totalCompleted)
            {
                return;
            }

            _totalCompleted = true;
            _totalStopwatch.Stop();
            AddTiming(TotalStageName, _totalStopwatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// Creates initial diagnostic counts from snapshot sections without performing extra graph reads or payload serialization.
        /// </summary>
        /// <param name="snapshot">The already-materialized snapshot supplied to the writer.</param>
        /// <param name="operationCount">The optional known operation count.</param>
        /// <param name="batchCount">The optional known batch count.</param>
        /// <returns>Application-level persistence counts safe for status output.</returns>
        private static ExtractionRunPersistenceCounts CreateInputCounts(ExtractedArchitectureSnapshot snapshot, int? operationCount, int? batchCount)
        {
            // Input counts intentionally avoid serializing payloads only to measure bytes, so SerializedPayloadBytes remains unknown.
            int projectCount = snapshot.Nodes.Count(static node => StringComparer.Ordinal.Equals(node.NodeKind.Value, NodeKind.Project.Value));
            int fileCount = snapshot.Nodes.Count(static node => IsFileLikeNode(node)) + snapshot.Evidence.Count;
            int metadataEntryCount = CountMetadataEntries(snapshot);
            return new ExtractionRunPersistenceCounts(
                snapshot.Repositories.Count,
                snapshot.Solutions.Count,
                projectCount,
                fileCount,
                snapshot.Nodes.Count,
                snapshot.Edges.Count,
                snapshot.Evidence.Count,
                snapshot.Findings.Count,
                snapshot.Warnings.Count + (snapshot.SnapshotHeader?.Warnings.Count ?? 0),
                snapshot.Errors.Count + (snapshot.SnapshotHeader?.Errors.Count ?? 0),
                snapshot.Metrics.Count,
                snapshot.GeneratedSummaries.Count,
                metadataEntryCount,
                operationCount,
                batchCount,
                SerializedPayloadBytes: null);
        }

        /// <summary>
        /// Creates final diagnostic counts using the writer's completed persistence counters where they are more accurate than input sections.
        /// </summary>
        /// <param name="snapshot">The already-materialized snapshot supplied to the writer.</param>
        /// <param name="counts">The completed persistence counts produced by the writer.</param>
        /// <param name="operationCount">The number of Neo4j statements executed during the write attempt.</param>
        /// <param name="batchCount">The number of write transactions executed during the write attempt.</param>
        /// <returns>Application-level persistence counts safe for status output.</returns>
        private static ExtractionRunPersistenceCounts CreateCompletedCounts(ExtractedArchitectureSnapshot snapshot, SnapshotPersistenceCounts counts, int operationCount, int batchCount)
        {
            // Relationship count uses completed writer counters because deduplication can change relationship totals from raw snapshot edges.
            int relationshipCount = counts.ArchitectureRelationships
                + counts.SnapshotSolutionRelationships
                + counts.NodeEvidenceRelationships
                + counts.RelationshipEndpointRelationships
                + counts.RelationshipEvidenceRelationships
                + counts.FindingRuleRelationships
                + counts.FindingNodeRelationships
                + counts.FindingEvidenceRelationships
                + counts.MetricEvidenceRelationships
                + counts.MetricTargetRelationships
                + counts.SummarySnapshotRelationships
                + counts.SummaryTargetRelationships;
            int projectCount = snapshot.Nodes.Count(static node => StringComparer.Ordinal.Equals(node.NodeKind.Value, NodeKind.Project.Value));
            int fileCount = snapshot.Nodes.Count(static node => IsFileLikeNode(node)) + snapshot.Evidence.Count;
            int metadataEntryCount = CountMetadataEntries(snapshot);
            return new ExtractionRunPersistenceCounts(
                counts.Repositories,
                counts.Solutions,
                projectCount,
                fileCount,
                counts.Nodes,
                relationshipCount,
                counts.Evidence,
                counts.Findings,
                snapshot.Warnings.Count + (snapshot.SnapshotHeader?.Warnings.Count ?? 0),
                snapshot.Errors.Count + (snapshot.SnapshotHeader?.Errors.Count ?? 0),
                counts.Metrics,
                counts.GeneratedSummaries,
                metadataEntryCount,
                operationCount,
                batchCount,
                SerializedPayloadBytes: null);
        }

        /// <summary>
        /// Adds one persistence-error count while preserving all previously captured count values.
        /// </summary>
        /// <param name="counts">The count set captured before the persistence failure was translated.</param>
        /// <returns>A count set with the safe persistence error included.</returns>
        private static ExtractionRunPersistenceCounts AddPersistenceErrorCount(ExtractionRunPersistenceCounts counts)
        {
            // Failure result creation happens outside snapshot input counting, so the translated persistence error is added here.
            return new ExtractionRunPersistenceCounts(
                counts.RepositoryCount,
                counts.SolutionCount,
                counts.ProjectCount,
                counts.FileCount,
                counts.NodeCount,
                counts.RelationshipCount,
                counts.EvidenceCount,
                counts.FindingCount,
                counts.WarningCount,
                counts.ErrorCount + 1,
                counts.MetricCount,
                counts.GeneratedSummaryCount,
                counts.MetadataEntryCount,
                counts.PersistenceOperationCount,
                counts.PersistenceBatchCount,
                counts.SerializedPayloadBytes);
        }

        /// <summary>
        /// Determines whether an architecture node represents a file-like graph concept for persistence diagnostics.
        /// </summary>
        /// <param name="node">The architecture node to classify.</param>
        /// <returns><see langword="true"/> when the node kind is a file or generated artifact concept; otherwise, <see langword="false"/>.</returns>
        private static bool IsFileLikeNode(ArchitectureNode node)
        {
            // FileCount includes explicit file/document nodes; evidence count is added separately because current evidence records are source-file scoped.
            return StringComparer.Ordinal.Equals(node.NodeKind.Value, NodeKind.FilePath.Value)
                || StringComparer.Ordinal.Equals(node.NodeKind.Value, NodeKind.Dockerfile.Value)
                || StringComparer.Ordinal.Equals(node.NodeKind.Value, NodeKind.SqlScript.Value)
                || StringComparer.Ordinal.Equals(node.NodeKind.Value, NodeKind.GeneratedArtifact.Value)
                || StringComparer.Ordinal.Equals(node.NodeKind.Value, NodeKind.OpenApiDocument.Value);
        }

        /// <summary>
        /// Counts metadata entries already present in the snapshot without parsing raw payloads outside the domain metadata contract.
        /// </summary>
        /// <param name="snapshot">The snapshot whose metadata values should be counted.</param>
        /// <returns>The number of non-empty metadata objects available across known snapshot sections.</returns>
        private static int CountMetadataEntries(ExtractedArchitectureSnapshot snapshot)
        {
            // The count is intentionally object-level rather than property-level because GraphMetadata exposes emptiness without a parser.
            int count = CountMetadata(snapshot.SnapshotHeader?.Metadata);
            count += snapshot.Repositories.Count(static item => !item.Metadata.IsEmpty);
            count += snapshot.Solutions.Count(static item => !item.Metadata.IsEmpty);
            count += snapshot.Nodes.Count(static item => !item.Metadata.IsEmpty);
            count += snapshot.Edges.Count(static item => !item.Metadata.IsEmpty);
            count += snapshot.Evidence.Count(static item => !item.Metadata.IsEmpty);
            count += snapshot.Rules.Count(static item => !item.Metadata.IsEmpty);
            count += snapshot.Findings.Count(static item => !item.Metadata.IsEmpty);
            count += snapshot.Metrics.Count(static item => !item.Metadata.IsEmpty);
            count += snapshot.GeneratedSummaries.Count(static item => !item.Metadata.IsEmpty);
            return count;
        }

        /// <summary>
        /// Converts one nullable metadata object into a non-empty metadata-object count.
        /// </summary>
        /// <param name="metadata">The metadata value to inspect.</param>
        /// <returns>One when <paramref name="metadata"/> exists and contains data; otherwise, zero.</returns>
        private static int CountMetadata(GraphMetadata? metadata)
        {
            // Null and empty metadata both represent known absence of metadata detail for diagnostic count purposes.
            return metadata is not null && !metadata.IsEmpty ? 1 : 0;
        }
    }
}
