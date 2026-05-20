using Archon.Application.Graph.Persistence;
using Xunit;

namespace Archon.Application.Tests.Graph.Persistence
{
    /// <summary>
    /// Verifies the application-layer graph recreation contracts make destructive intent explicit and report safe diagnostics.
    /// </summary>
    public sealed class GraphRecreationResultTests
    {
        /// <summary>
        /// Confirms the authorized request factory supplies the exact destructive guard phrase and preserves trimmed reason text.
        /// </summary>
        [Fact]
        public void CreateAuthorizedCreatesRequestWithRequiredConfirmationPhrase()
        {
            // The factory is the preferred local and test path because it centralizes the loud destructive confirmation phrase.
            GraphRecreationRequest request = GraphRecreationRequest.CreateAuthorized(" reset integration data ");

            Assert.True(request.IsAuthorized);
            Assert.Equal(GraphRecreationRequest.RequiredConfirmationPhrase, request.ConfirmationPhrase);
            Assert.Equal("reset integration data", request.Reason);
        }

        /// <summary>
        /// Confirms requests with missing, blank, or near-miss confirmation phrases do not authorize destructive recreation.
        /// </summary>
        /// <param name="confirmationPhrase">The caller-supplied phrase that should fail the exact authorization guard.</param>
        [Theory]
        [InlineData("")]
        [InlineData("delete archon graph data and recreate schema")]
        [InlineData("DELETE ARCHON GRAPH DATA")]
        [InlineData("DELETE ARCHON GRAPH DATA AND RECREATE SCHEMA ")]
        public void RequestRequiresExactConfirmationPhrase(string confirmationPhrase)
        {
            // The guard uses ordinal exact matching so casing, truncation, and whitespace changes cannot accidentally erase data.
            GraphRecreationRequest request = new(confirmationPhrase);

            Assert.False(request.IsAuthorized);
        }

        /// <summary>
        /// Confirms unauthorized results are explicit failures with no deleted data and a stable safe diagnostic code.
        /// </summary>
        [Fact]
        public void UnauthorizedCreatesGuardFailureResult()
        {
            // Infrastructure returns this result before opening a write transaction when the destructive confirmation phrase is absent.
            GraphRecreationResult result = GraphRecreationResult.Unauthorized();

            Assert.False(result.Succeeded);
            Assert.False(result.Authorized);
            Assert.Equal(0, result.RecordsDeleted);
            Assert.Equal(0, result.SchemaStatementsExecuted);
            Assert.Empty(result.Warnings);
            PersistenceError error = Assert.Single(result.Errors);
            Assert.Equal(PersistenceStage.GraphRecreation, error.Stage);
            Assert.Equal("GraphRecreationNotAuthorized", error.Code);
        }

        /// <summary>
        /// Confirms successful recreation results preserve deletion and schema counts while avoiding fatal errors.
        /// </summary>
        [Fact]
        public void SuccessCreatesAuthorizedResultWithCountsAndWarnings()
        {
            // A successful result means the guard passed, records were cleared, and schema initialization completed afterward.
            PersistenceWarning warning = new(PersistenceStage.GraphRecreation, "GraphWasEmpty", "No Archon graph records were present.");

            GraphRecreationResult result = GraphRecreationResult.Success(7, 36, new[] { warning });

            Assert.True(result.Succeeded);
            Assert.True(result.Authorized);
            Assert.Equal(7, result.RecordsDeleted);
            Assert.Equal(36, result.SchemaStatementsExecuted);
            Assert.Single(result.Warnings);
            Assert.Empty(result.Errors);
        }

        /// <summary>
        /// Confirms failed authorized recreation results normalize counts and preserve safe fatal diagnostics.
        /// </summary>
        [Fact]
        public void FailureCreatesAuthorizedResultWithSafeError()
        {
            // Negative defensive counts should never leak through result contracts even if an adapter fails before metrics are known.
            PersistenceError error = new(PersistenceStage.GraphRecreation, "GraphClearFailed", "Graph recreation failed.");

            GraphRecreationResult result = GraphRecreationResult.Failure(-1, -2, error);

            Assert.False(result.Succeeded);
            Assert.True(result.Authorized);
            Assert.Equal(0, result.RecordsDeleted);
            Assert.Equal(0, result.SchemaStatementsExecuted);
            Assert.Empty(result.Warnings);
            Assert.Equal("GraphClearFailed", Assert.Single(result.Errors).Code);
        }
    }
}
