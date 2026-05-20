using Archon.Application.Graph.Persistence;
using Microsoft.Extensions.Logging;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Provides credential-safe stage-level logging helpers for Neo4j snapshot persistence.
    /// </summary>
    /// <remarks>
    /// The helper centralizes logging wording so the writer can report progress and failures consistently without logging large payloads,
    /// secrets, or Neo4j connection details.
    /// </remarks>
    public sealed class Neo4jPersistenceStageLogger
    {
        private readonly ILogger<Neo4jPersistenceStageLogger> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jPersistenceStageLogger"/> class.
        /// </summary>
        /// <param name="logger">The logger that receives credential-safe persistence stage messages.</param>
        public Neo4jPersistenceStageLogger(ILogger<Neo4jPersistenceStageLogger> logger)
        {
            // Store only the logger dependency; no persistence behavior happens during construction.
            _logger = logger;
        }

        /// <summary>
        /// Logs the beginning of a snapshot persistence stage.
        /// </summary>
        /// <param name="stage">The high-level persistence stage being executed.</param>
        /// <param name="snapshotStableKey">The stable key of the snapshot being persisted.</param>
        public void LogStageStarting(PersistenceStage stage, string? snapshotStableKey)
        {
            // Stable keys are non-secret logical identifiers and are safe to include for troubleshooting.
            _logger.LogInformation("Starting Neo4j {PersistenceStage} for snapshot {SnapshotStableKey}.", stage, snapshotStableKey ?? "unknown");
        }

        /// <summary>
        /// Logs successful completion of a snapshot persistence stage.
        /// </summary>
        /// <param name="stage">The high-level persistence stage that completed.</param>
        /// <param name="snapshotStableKey">The stable key of the snapshot being persisted.</param>
        public void LogStageCompleted(PersistenceStage stage, string? snapshotStableKey)
        {
            // The completion message intentionally avoids logging record payloads or connection configuration.
            _logger.LogInformation("Completed Neo4j {PersistenceStage} for snapshot {SnapshotStableKey}.", stage, snapshotStableKey ?? "unknown");
        }

        /// <summary>
        /// Logs a credential-safe snapshot persistence failure.
        /// </summary>
        /// <param name="exception">The exception that caused the stage to fail.</param>
        /// <param name="stage">The high-level persistence stage that failed.</param>
        /// <param name="snapshotStableKey">The stable key of the snapshot being persisted.</param>
        public void LogStageFailed(Exception exception, PersistenceStage stage, string? snapshotStableKey)
        {
            // Exception details are useful for local diagnostics; the message avoids secrets and large graph payloads.
            _logger.LogError(exception, "Neo4j {PersistenceStage} failed for snapshot {SnapshotStableKey}.", stage, snapshotStableKey ?? "unknown");
        }
    }
}
