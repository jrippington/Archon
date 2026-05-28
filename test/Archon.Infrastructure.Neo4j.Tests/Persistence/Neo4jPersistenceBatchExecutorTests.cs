using Archon.Infrastructure.Neo4j.Persistence;
using Neo4j.Driver;
using System.Reflection;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Persistence
{
    /// <summary>
    /// Verifies the internal Neo4j batch executor that bounds list parameters for static persistence statements.
    /// </summary>
    public sealed class Neo4jPersistenceBatchExecutorTests
    {
        /// <summary>
        /// Confirms empty input records do not execute Cypher or count persistence operations.
        /// </summary>
        /// <returns>A task that completes after the empty-batch scenario has been asserted.</returns>
        [Fact]
        public async Task ExecuteBatchesAsyncSkipsEmptyInputs()
        {
            // Empty sections are common in partial snapshots and should not create unnecessary Neo4j statements.
            RecordingQueryRunner queryRunner = new();
            IReadOnlyList<IReadOnlyDictionary<string, object?>> records = Array.Empty<IReadOnlyDictionary<string, object?>>();

            int operationCount = await Neo4jPersistenceBatchExecutor.ExecuteBatchesAsync(queryRunner, "RETURN $records", "records", records, batchSize: 2);

            Assert.Equal(0, operationCount);
            Assert.Empty(queryRunner.Runs);
        }

        /// <summary>
        /// Confirms an exact multiple of the configured batch size executes one Cypher statement per full batch.
        /// </summary>
        /// <returns>A task that completes after exact-boundary batching has been asserted.</returns>
        [Fact]
        public async Task ExecuteBatchesAsyncExecutesExactBatchBoundaries()
        {
            // Four records with a batch size of two should produce two executed Cypher statements, not four row-level operations.
            RecordingQueryRunner queryRunner = new();
            IReadOnlyList<IReadOnlyDictionary<string, object?>> records = CreateRecords(4);

            int operationCount = await Neo4jPersistenceBatchExecutor.ExecuteBatchesAsync(queryRunner, "RETURN $records", "records", records, batchSize: 2);

            Assert.Equal(2, operationCount);
            Assert.Equal(2, queryRunner.Runs.Count);
            Assert.Equal(new[] { 2, 2 }, queryRunner.BatchSizes);
        }

        /// <summary>
        /// Confirms a final partial batch is executed after all full batches have completed.
        /// </summary>
        /// <returns>A task that completes after partial-tail batching has been asserted.</returns>
        [Fact]
        public async Task ExecuteBatchesAsyncExecutesFinalPartialBatch()
        {
            // Five records with a batch size of two require a final single-record batch so the tail is not dropped.
            RecordingQueryRunner queryRunner = new();
            IReadOnlyList<IReadOnlyDictionary<string, object?>> records = CreateRecords(5);

            int operationCount = await Neo4jPersistenceBatchExecutor.ExecuteBatchesAsync(queryRunner, "RETURN $records", "records", records, batchSize: 2);

            Assert.Equal(3, operationCount);
            Assert.Equal(new[] { 2, 2, 1 }, queryRunner.BatchSizes);
        }

        /// <summary>
        /// Creates deterministic parameter dictionaries for batch executor tests.
        /// </summary>
        /// <param name="count">The number of records to create.</param>
        /// <returns>A read-only list of record dictionaries carrying one stable integer value each.</returns>
        private static IReadOnlyList<IReadOnlyDictionary<string, object?>> CreateRecords(int count)
        {
            // Records use the same dictionary shape as the production mapper while keeping assertions independent of graph domains.
            List<IReadOnlyDictionary<string, object?>> records = new(capacity: count);
            for (int index = 0; index < count; index++)
            {
                records.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["value"] = index
                });
            }

            return records;
        }

        /// <summary>
        /// Records Cypher executions issued by the batch executor without opening a Neo4j connection.
        /// </summary>
        private sealed class RecordingQueryRunner : IAsyncQueryRunner
        {
            /// <summary>
            /// Gets the captured statement executions in call order.
            /// </summary>
            public List<RecordedRun> Runs { get; } = [];

            /// <summary>
            /// Gets the number of records carried by each captured list-parameter batch.
            /// </summary>
            public IReadOnlyList<int> BatchSizes => Runs.Select(static run => run.BatchSize).ToArray();

            /// <summary>
            /// Captures one Cypher execution and returns a cursor that can be consumed by production code.
            /// </summary>
            /// <param name="query">The static Cypher statement supplied by the executor.</param>
            /// <param name="parameters">The parameter object supplied by the executor.</param>
            /// <returns>A cursor whose consumption completes successfully.</returns>
            public Task<IResultCursor> RunAsync(Query query)
            {
                // The Neo4j driver funnels string-and-parameter overloads into this method, so recording here observes executor behavior.
                IReadOnlyDictionary<string, object?> parameters = query.Parameters?.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal)
                    ?? new Dictionary<string, object?>(StringComparer.Ordinal);
                Runs.Add(new RecordedRun(query.Text, parameters));
                return Task.FromResult<IResultCursor>(new ConsumedResultCursor());
            }

            /// <summary>
            /// Captures a parameterless Cypher execution.
            /// </summary>
            /// <param name="query">The static Cypher statement supplied by the caller.</param>
            /// <returns>A cursor whose consumption completes successfully.</returns>
            public Task<IResultCursor> RunAsync(string query)
            {
                // Parameterless calls are still recorded so unsupported executor changes are observable in tests.
                Runs.Add(new RecordedRun(query, new Dictionary<string, object?>(StringComparer.Ordinal)));
                return Task.FromResult<IResultCursor>(new ConsumedResultCursor());
            }

            /// <summary>
            /// Captures a Cypher execution with an anonymous-object parameter payload.
            /// </summary>
            /// <param name="query">The static Cypher statement supplied by the caller.</param>
            /// <param name="parameters">The anonymous parameter object supplied by the caller; this fake rejects it because batching uses dictionary parameters.</param>
            /// <returns>A cursor whose consumption completes successfully.</returns>
            public Task<IResultCursor> RunAsync(string query, object parameters)
            {
                // The batch executor uses dictionaries, so object-parameter calls are not expected in these tests.
                throw new NotSupportedException("The recording query runner supports dictionary parameters for batch tests.");
            }

            /// <summary>
            /// Captures a Cypher execution with dictionary parameters.
            /// </summary>
            /// <param name="query">The static Cypher statement supplied by the caller.</param>
            /// <param name="parameters">The dictionary parameter payload supplied by the caller.</param>
            /// <returns>A cursor whose consumption completes successfully.</returns>
            public Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters)
            {
                // Copying the dictionary freezes the observable payload for assertions after the executor moves to later batches.
                IReadOnlyDictionary<string, object?> copiedParameters = parameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
                Runs.Add(new RecordedRun(query, copiedParameters));
                return Task.FromResult<IResultCursor>(new ConsumedResultCursor());
            }

            /// <summary>
            /// Releases the fake runner's synchronous resources.
            /// </summary>
            public void Dispose()
            {
                // The fake owns no unmanaged resources; the method exists to satisfy the Neo4j runner contract.
            }

            /// <summary>
            /// Releases the fake runner's asynchronous resources.
            /// </summary>
            /// <returns>A completed value task because the fake has no resources to release.</returns>
            public ValueTask DisposeAsync()
            {
                // The fake owns no asynchronous resources; returning a completed task keeps disposal deterministic.
                return ValueTask.CompletedTask;
            }

            /// <summary>
            /// Captures the Cypher text and list-parameter payload for one executor call.
            /// </summary>
            /// <param name="Cypher">The Cypher statement text executed by the batch executor.</param>
            /// <param name="Parameters">The parameters supplied to the Cypher statement.</param>
            public sealed record RecordedRun(string Cypher, IReadOnlyDictionary<string, object?> Parameters)
            {
                /// <summary>
                /// Gets the number of row dictionaries supplied through the executor's <c>records</c> parameter.
                /// </summary>
                public int BatchSize
                {
                    get
                    {
                        // Tests intentionally use the records parameter name so this property can assert the bounded payload size.
                        return ((IReadOnlyList<IReadOnlyDictionary<string, object?>>)Parameters["records"]!).Count;
                    }
                }
            }
        }

        /// <summary>
        /// Provides a minimal consumable Neo4j result cursor for executor unit tests.
        /// </summary>
        private sealed class ConsumedResultCursor : IResultCursor
        {
            /// <summary>
            /// Gets a value indicating whether the fake cursor is still open.
            /// </summary>
            public bool IsOpen => false;

            /// <summary>
            /// Consumes the fake result stream.
            /// </summary>
            /// <returns>A task containing a default cursor summary.</returns>
            public Task<IResultSummary> ConsumeAsync()
            {
                // The batch executor only requires successful consumption, so the summary can be a generated interface proxy.
                return Task.FromResult(ProxyFactory.Create<IResultSummary>());
            }

            /// <summary>
            /// Returns no record keys because the batch executor never reads records from write statements.
            /// </summary>
            /// <returns>A task containing an empty key collection.</returns>
            public Task<string[]> KeysAsync()
            {
                // The executor only consumes cursors, so no returned columns are needed.
                return Task.FromResult(Array.Empty<string>());
            }

            /// <summary>
            /// Peeks at the next record without consuming it.
            /// </summary>
            /// <returns>A task containing <see langword="null" /> because the fake cursor contains no records.</returns>
            public Task<IRecord?> PeekAsync()
            {
                // The fake write cursor is empty by design.
                return Task.FromResult<IRecord?>(null);
            }

            /// <summary>
            /// Returns no records because these tests only validate write-statement execution counts.
            /// </summary>
            /// <returns>A task containing <see langword="false" /> because no records are available.</returns>
            public Task<bool> FetchAsync()
            {
                // Write statements do not need row materialization in these unit tests.
                return Task.FromResult(false);
            }

            /// <summary>
            /// Gets the current cursor record when one is available.
            /// </summary>
            public IRecord Current => throw new InvalidOperationException("The batch executor test cursor does not expose records.");

            /// <summary>
            /// Enumerates the fake cursor records asynchronously.
            /// </summary>
            /// <param name="cancellationToken">A token that would cancel enumeration if records existed.</param>
            /// <returns>An empty async enumerator.</returns>
            public IAsyncEnumerator<IRecord> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                // Returning an empty enumerator protects tests from accidental row materialization.
                return new EmptyAsyncRecordEnumerator();
            }
        }

        /// <summary>
        /// Provides an empty asynchronous enumerator for the fake result cursor.
        /// </summary>
        private sealed class EmptyAsyncRecordEnumerator : IAsyncEnumerator<IRecord>
        {
            /// <summary>
            /// Gets the current record, which is never available for this empty enumerator.
            /// </summary>
            public IRecord Current => throw new InvalidOperationException("The empty batch executor cursor has no current record.");

            /// <summary>
            /// Releases enumerator resources.
            /// </summary>
            /// <returns>A completed value task because no resources are held.</returns>
            public ValueTask DisposeAsync()
            {
                // The enumerator owns no resources.
                return ValueTask.CompletedTask;
            }

            /// <summary>
            /// Advances the enumerator.
            /// </summary>
            /// <returns>A task containing <see langword="false" /> because the enumerator is empty.</returns>
            public ValueTask<bool> MoveNextAsync()
            {
                // The fake cursor contains no records.
                return ValueTask.FromResult(false);
            }
        }

        /// <summary>
        /// Creates proxy implementations for Neo4j interfaces whose members are irrelevant to these unit tests.
        /// </summary>
        private class ProxyFactory : DispatchProxy
        {
            /// <summary>
            /// Creates a proxy implementation of a Neo4j interface.
            /// </summary>
            /// <typeparam name="TInterface">The interface type to proxy.</typeparam>
            /// <returns>A generated proxy instance for the requested interface.</returns>
            public static TInterface Create<TInterface>()
                where TInterface : class
            {
                // DispatchProxy avoids hand-writing members that the batch executor never uses.
                return DispatchProxy.Create<TInterface, ProxyFactory>();
            }

            /// <summary>
            /// Rejects unexpected proxy calls so tests fail loudly if production code starts using additional cursor summary members.
            /// </summary>
            /// <param name="targetMethod">The interface method invoked on the generated proxy.</param>
            /// <param name="args">The arguments supplied to the proxied method.</param>
            /// <returns>No value because all calls are unsupported for this fake.</returns>
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                // The fake summary is intentionally opaque; any member use would represent new behavior requiring a real test double.
                throw new NotSupportedException("The batch executor test proxy does not support member access.");
            }
        }
    }
}
