using Archon.Application.Graph.Persistence;
using Xunit;

namespace Archon.Application.Tests.Graph.Persistence
{
    /// <summary>
    /// Verifies application-layer graph initialization result behavior stays infrastructure-neutral and deterministic.
    /// </summary>
    public sealed class GraphInitializationResultTests
    {
        /// <summary>
        /// Confirms successful results preserve statement counts and warning diagnostics without errors.
        /// </summary>
        [Fact]
        public void SuccessCreatesSuccessfulResultWithWarnings()
        {
            // The success helper is the path the Neo4j initializer uses after executing the full schema catalog.
            PersistenceWarning warning = new(PersistenceStage.SchemaInitialization, "SchemaAlreadyCurrent", "The schema was already current.");

            GraphInitializationResult result = GraphInitializationResult.Success(12, new[] { warning });

            Assert.True(result.Succeeded);
            Assert.Equal(12, result.StatementsExecuted);
            Assert.Single(result.Warnings);
            Assert.Empty(result.Errors);
        }

        /// <summary>
        /// Confirms failed results normalize negative counts and preserve safe error diagnostics.
        /// </summary>
        [Fact]
        public void FailureCreatesFailedResultWithSafeError()
        {
            // Negative statement counts should never escape to callers, even when an adapter reports a defensive failure path.
            PersistenceError error = new(PersistenceStage.SchemaInitialization, "SchemaFailed", "Schema initialization failed.");

            GraphInitializationResult result = GraphInitializationResult.Failure(-1, error);

            Assert.False(result.Succeeded);
            Assert.Equal(0, result.StatementsExecuted);
            Assert.Empty(result.Warnings);
            Assert.Single(result.Errors);
            Assert.Equal("SchemaFailed", result.Errors[0].Code);
        }

        /// <summary>
        /// Confirms diagnostic records normalize blank fields to deterministic fallback text.
        /// </summary>
        [Fact]
        public void DiagnosticsNormalizeBlankFields()
        {
            // Normalization prevents empty codes or messages from becoming ambiguous logs or test output.
            PersistenceError error = new(PersistenceStage.Unknown, " ", " ");
            PersistenceWarning warning = new(PersistenceStage.Unknown, " ", " ");

            Assert.Equal("PersistenceError", error.Code);
            Assert.Equal("A persistence error occurred.", error.Message);
            Assert.Equal("PersistenceWarning", warning.Code);
            Assert.Equal("A persistence warning occurred.", warning.Message);
        }
    }
}
