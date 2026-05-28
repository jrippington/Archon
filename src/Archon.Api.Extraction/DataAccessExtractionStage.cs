using Archon.Application.Extraction.Pipeline;
using Archon.Domain.Graph.Identity;
using Archon.Extractors.DataAccess.LinqToSql;
using Archon.Infrastructure.Roslyn.Extraction;
using Archon.Roslyn.SemanticModel;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Extraction
{
    /// <summary>
    /// Runs the data-access DBML data-access extraction vertical slice as part of the API-triggered extraction pipeline.
    /// </summary>
    /// <remarks>
    /// The stage is an orchestration adapter. It delegates static LINQ to SQL DBML model extraction to the data-access extractor project and merges graph-ready facts into the shared accumulator without connecting to target databases, executing analyzed code, or writing directly to Neo4j.
    /// </remarks>
    public sealed class DataAccessExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the extractor that recognizes LINQ to SQL DBML model artifacts.
        /// </summary>
        private readonly LinqToSqlDbmlModelExtractor _dbmlExtractor;

        /// <summary>
        /// Stores the logger used for credential-safe data-access DBML orchestration events.
        /// </summary>
        private readonly ILogger<DataAccessExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataAccessExtractionStage" /> class.
        /// </summary>
        /// <param name="logger">The logger used for start, completion, and degraded extraction messages.</param>
        public DataAccessExtractionStage(ILogger<DataAccessExtractionStage> logger)
            : this(new LinqToSqlDbmlModelExtractor(), logger)
        {
            // The default constructor path keeps API module registration simple while preserving extractor ownership in the data-access project.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataAccessExtractionStage" /> class with an explicit extractor dependency.
        /// </summary>
        /// <param name="dbmlExtractor">The extractor responsible for LINQ to SQL DBML graph facts.</param>
        /// <param name="logger">The logger used for credential-safe stage diagnostics.</param>
        public DataAccessExtractionStage(LinqToSqlDbmlModelExtractor dbmlExtractor, ILogger<DataAccessExtractionStage> logger)
        {
            // Explicit dependencies make the stage independently testable and keep host registration free of extraction logic.
            _dbmlExtractor = dbmlExtractor ?? throw new ArgumentNullException(nameof(dbmlExtractor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the stable stage identifier used by pipeline ordering, progress reporting, and diagnostics.
        /// </summary>
        public string StageId => "wp009-data-access-dbml";

        /// <summary>
        /// Runs the DBML data-access extractor and merges its graph facts into the shared accumulator.
        /// </summary>
        /// <param name="context">The pipeline context containing resolved repository input, accepted run state, and shared accumulation.</param>
        /// <param name="cancellationToken">The cancellation token that stops repository discovery and DBML extraction.</param>
        /// <returns>A successful stage result when DBML extraction completes or degrades non-fatally.</returns>
        public async Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // data-access DBML DBML extraction is non-blocking because malformed files and unreadable roots are represented as diagnostics or empty contributions.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting data-access DBML data-access DBML extraction for run {RunId}.",
                context.Run.RunId.ToString());

            IReadOnlyList<SemanticExtractionRequest> semanticDocuments = await LoadSemanticDocumentsAsync(context, cancellationToken).ConfigureAwait(false);
            StableKey snapshotStableKey = CreateSnapshotStableKey(context.ResolvedInput.RepositoryRootDirectory, context.Run.RunId.ToString());
            LinqToSqlDbmlExtractionRequest extractionRequest = new(snapshotStableKey, context.ResolvedInput.RepositoryRootDirectory, semanticDocuments);
            LinqToSqlDbmlExtractionResult result = _dbmlExtractor.Extract(extractionRequest, cancellationToken);
            context.Accumulation.Merge(result.Snapshot);
            _dbmlExtractor.AccumulateCrossSliceCorrelations(extractionRequest, context.Accumulation, cancellationToken);

            _logger.LogInformation(
                "Completed data-access DBML data-access DBML extraction for run {RunId}; contributed {NodeCount} node(s), {EdgeCount} edge(s), and {EvidenceCount} evidence record(s).",
                context.Run.RunId.ToString(),
                result.Snapshot.Nodes.Count,
                result.Snapshot.Edges.Count,
                result.Snapshot.Evidence.Count);

            return ExtractionStageResult.Success();
        }

        /// <summary>
        /// Loads Roslyn semantic documents for data-access DBML generated designer and source-usage extraction.
        /// </summary>
        /// <param name="context">The pipeline context containing submitted solution and repository input.</param>
        /// <param name="cancellationToken">The cancellation token that stops solution and source loading.</param>
        /// <returns>Semantic extraction requests for supported C# documents in submitted solutions.</returns>
        private async Task<IReadOnlyList<SemanticExtractionRequest>> LoadSemanticDocumentsAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // data-access DBML reuses the lightweight Roslyn loader from the semantic stage so it can inspect generated designer and source usage without adding MSBuildWorkspace coupling.
            List<SemanticExtractionRequest> semanticDocuments = [];
            RoslynSemanticDocumentLoader loader = new();
            foreach (string solutionPath in context.ResolvedInput.SolutionPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    semanticDocuments.AddRange(await loader.LoadCSharpDocumentsAsync(context.ResolvedInput.RepositoryRootDirectory, solutionPath, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException or ArgumentException)
                {
                    context.Accumulation.AddWarning($"data-access DBML data-access source usage extraction skipped solution '{Path.GetFileName(solutionPath)}' because semantic documents could not be loaded.");
                    _logger.LogWarning(exception, "data-access DBML data-access source usage extraction skipped semantic loading for solution {SolutionPath} during run {RunId}.", solutionPath, context.Run.RunId.ToString());
                }
            }

            return semanticDocuments;
        }

        /// <summary>
        /// Creates the snapshot stable key used by extraction stages that merge graph facts into the current run.
        /// </summary>
        /// <param name="repositoryRootDirectory">The repository root directory for the analyzed repository.</param>
        /// <param name="runId">The accepted extraction run identifier.</param>
        /// <returns>A deterministic snapshot stable key scoped to the run.</returns>
        private static StableKey CreateSnapshotStableKey(string repositoryRootDirectory, string runId)
        {
            // The snapshot key mirrors the assembler so contributed facts are scoped to the same final snapshot identity.
            StableKey repositoryStableKey = StableKeyGenerator.ForRepository(NormalizeIdentitySegment(repositoryRootDirectory));
            return StableKeyGenerator.ForSummary(repositoryStableKey.Value, "ExtractionRun", runId);
        }

        /// <summary>
        /// Normalizes a filesystem path into the repository identity segment used by final snapshot assembly.
        /// </summary>
        /// <param name="value">The absolute path value to normalize.</param>
        /// <returns>A deterministic lowercase segment suitable for stable-key generation.</returns>
        private static string NormalizeIdentitySegment(string value)
        {
            // Stable keys must match the final snapshot assembler so stage contributions pass persistence scope validation.
            string trimmed = Path.TrimEndingDirectorySeparator(value).Replace('\\', '/').Trim();
            return trimmed.ToLowerInvariant();
        }
    }
}
