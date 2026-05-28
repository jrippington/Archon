using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Executes static Neo4j persistence statements over bounded list-parameter batches.
    /// </summary>
    /// <remarks>
    /// This helper is intentionally scoped to the Neo4j persistence adapter. It avoids a repository-wide abstraction while giving the
    /// snapshot writer one tested path for empty inputs, exact-size batches, partial final batches, static Cypher, and operation counting.
    /// </remarks>
    internal static class Neo4jPersistenceBatchExecutor
    {
        /// <summary>
        /// Executes one static Cypher statement for each bounded batch of mapped persistence records.
        /// </summary>
        /// <param name="transaction">The active Neo4j query runner that receives each statement.</param>
        /// <param name="cypher">The static parameterized Cypher statement to execute for every non-empty batch.</param>
        /// <param name="parameterName">The Cypher parameter name that receives the current batch list.</param>
        /// <param name="records">The already mapped persistence records to partition into bounded batches.</param>
        /// <param name="batchSize">The maximum number of records to include in one Cypher execution.</param>
        /// <returns>The number of Cypher executions performed for the supplied records.</returns>
        public static async Task<int> ExecuteBatchesAsync(
            IAsyncQueryRunner transaction,
            string cypher,
            string parameterName,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
            int batchSize)
        {
            // The writer validates options before calling this helper, but defensive argument checks keep the internal seam safe for tests
            // and future writer stages that may reuse it.
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentException.ThrowIfNullOrWhiteSpace(cypher);
            ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
            ArgumentNullException.ThrowIfNull(records);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

            if (records.Count == 0)
            {
                // Empty persistence sections should not execute no-op Cypher because operation count means actual Neo4j statements.
                return 0;
            }

            int operationCount = 0;
            for (int offset = 0; offset < records.Count; offset += batchSize)
            {
                // The final batch may be smaller than the configured size; Math.Min preserves that tail instead of dropping it.
                int currentBatchSize = Math.Min(batchSize, records.Count - offset);
                List<IReadOnlyDictionary<string, object?>> batch = new(capacity: currentBatchSize);
                for (int index = 0; index < currentBatchSize; index++)
                {
                    batch.Add(records[offset + index]);
                }

                Dictionary<string, object> parameters = new(StringComparer.Ordinal)
                {
                    [parameterName] = batch
                };

                IResultCursor cursor = await transaction.RunAsync(cypher, parameters).ConfigureAwait(false);
                await cursor.ConsumeAsync().ConfigureAwait(false);
                operationCount++;
            }

            return operationCount;
        }
    }
}
