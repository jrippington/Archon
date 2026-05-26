using ArchonMcp.McpEnvelope;
using ArchonMcp.McpRuntime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies the WP015 Work Item 2 shared MCP envelope contracts, structured failures, and safe response helpers.
    /// </summary>
    public sealed class ArchonMcpEnvelopeContractTests
    {
        /// <summary>
        /// Confirms the baseline operation can produce the common success envelope shape used by later tools and resources.
        /// </summary>
        [Fact]
        public void BaselineHealthOperationReturnsCommonSuccessEnvelope()
        {
            // The baseline operation service exercises the same envelope contract without exposing a public MCP endpoint yet.
            using WebApplication app = Program.BuildApplication(Array.Empty<string>());
            IArchonMcpBaselineOperation operation = app.Services.GetRequiredService<IArchonMcpBaselineOperation>();

            ArchonMcpEnvelope<IReadOnlyList<ArchonMcpFact>> envelope = operation.GetHealthEnvelope();

            // The common envelope must carry operation identity, safe summary text, confidence, facts, limits, and follow-ups.
            Assert.Equal(ArchonMcpBaselineCapabilities.Health.Name, envelope.Operation);
            Assert.Null(envelope.Snapshot);
            Assert.Equal("Archon MCP runtime baseline is ready.", envelope.Summary);
            Assert.Equal(ArchonMcpConfidenceLevel.High, envelope.Confidence.Level);
            ArchonMcpFact fact = Assert.Single(envelope.Facts);
            Assert.Equal("mcp-runtime-baseline", fact.StableKey);
            Assert.Equal("OperationalCapability", fact.Kind);
            Assert.Empty(envelope.Evidence);
            Assert.Empty(envelope.Findings);
            Assert.Empty(envelope.Unknowns);
            Assert.Empty(envelope.Warnings);
            Assert.False(envelope.Limits.Truncated);
            Assert.Contains(envelope.SuggestedFollowUps, followUp => followUp.Operation == "archon.health");
        }

        /// <summary>
        /// Confirms every required structured error category has a stable safe error shape.
        /// </summary>
        [Fact]
        public void StructuredErrorsCoverRequiredFailureCategories()
        {
            // Required error categories come from the WP015 failure contract and must be deterministic for future handlers.
            ArchonMcpErrorCategory[] requiredCategories =
            [
                ArchonMcpErrorCategory.Validation,
                ArchonMcpErrorCategory.UnsupportedOperation,
                ArchonMcpErrorCategory.NotFound,
                ArchonMcpErrorCategory.Ambiguous,
                ArchonMcpErrorCategory.Unauthorized,
                ArchonMcpErrorCategory.Forbidden,
                ArchonMcpErrorCategory.DependencyUnavailable,
                ArchonMcpErrorCategory.QueryLayerFailure,
                ArchonMcpErrorCategory.ServerError
            ];

            foreach (ArchonMcpErrorCategory category in requiredCategories)
            {
                ArchonMcpErrorResponse error = ArchonMcpErrorResponse.Create(
                    "archon.test",
                    category,
                    "Safe test message.",
                    [new ArchonMcpSuggestedFollowUp("Review request parameters", "user.question", null)]);

                // The structured error omits stack traces and exposes only stable category/code/message/follow-up fields.
                Assert.Equal("archon.test", error.Operation);
                Assert.Equal(category, error.Error.Category);
                Assert.False(string.IsNullOrWhiteSpace(error.Error.Code));
                Assert.Equal("Safe test message.", error.Error.Message);
                Assert.DoesNotContain("System.", error.Error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(" at ", error.Error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Single(error.SuggestedFollowUps);
            }
        }

        /// <summary>
        /// Confirms result limits return truncation metadata and a safe narrowing follow-up when a requested count is exceeded.
        /// </summary>
        [Fact]
        public void LimitGuardReturnsTruncationMetadataAndSuggestedNarrowing()
        {
            // A small configured limit keeps the assertion focused on metadata rather than large response payloads.
            ArchonMcpLimitGuard guard = new(Options.Create(new ArchonMcpLimitsOptions
            {
                MaxResultCount = 2,
                MaxTraversalDepth = 3,
                MaxEvidenceCount = 10,
                MaxPathCount = 5,
                MaxSerializedContextCharacters = 24000
            }));

            ArchonMcpLimitedList<int> limited = guard.ApplyResultLimit([1, 2, 3], requestedLimit: 10, "archon.test");

            // The guard returns bounded items, records the applied limit, and suggests a safe narrowing action.
            Assert.Equal([1, 2], limited.Items);
            Assert.True(limited.Limits.Truncated);
            Assert.Equal("resultCount", limited.Limits.LimitKind);
            Assert.Equal(2, limited.Limits.AppliedLimit);
            Assert.Equal(3, limited.Limits.OriginalCount);
            Assert.Equal(2, limited.Limits.ReturnedCount);
            Assert.Contains(limited.SuggestedFollowUps, followUp => followUp.Operation == "user.question");
        }

        /// <summary>
        /// Confirms validation rejects malformed stable keys, unsafe search text, excessive limits, and excessive traversal depth.
        /// </summary>
        [Fact]
        public void ValidatorRejectsMalformedInputsBeforeQueryExecution()
        {
            // The validator is intentionally reusable so later tools can fail before calling application/query dependencies.
            ArchonMcpRequestValidator validator = new(Options.Create(new ArchonMcpLimitsOptions
            {
                MaxResultCount = 5,
                MaxTraversalDepth = 2,
                MaxEvidenceCount = 10,
                MaxPathCount = 5,
                MaxSerializedContextCharacters = 24000
            }));

            ArchonMcpValidationResult result = validator.Validate(new ArchonMcpValidationRequest(
                StableKey: "123",
                SnapshotSelector: "latest snapshot",
                SearchText: "",
                Filters: ["valid-filter", ""],
                RequestedCount: 50,
                RequestedDepth: 3,
                PageNumber: 0,
                PageSize: -1));

            // All invalid fields should be reported together so clients can fix one request rather than retrying repeatedly.
            Assert.False(result.IsValid);
            Assert.Contains(result.Failures, failure => failure.Field == "stableKey");
            Assert.Contains(result.Failures, failure => failure.Field == "snapshotSelector");
            Assert.Contains(result.Failures, failure => failure.Field == "searchText");
            Assert.Contains(result.Failures, failure => failure.Field == "filters");
            Assert.Contains(result.Failures, failure => failure.Field == "requestedCount");
            Assert.Contains(result.Failures, failure => failure.Field == "requestedDepth");
            Assert.Contains(result.Failures, failure => failure.Field == "pageNumber");
            Assert.Contains(result.Failures, failure => failure.Field == "pageSize");
        }

        /// <summary>
        /// Confirms evidence mapping redacts sensitive snippet previews and prevents Neo4j internal identifiers from leaking.
        /// </summary>
        [Fact]
        public void EvidenceMappingRedactsSecretsAndRejectsNeo4jInternalIds()
        {
            // Evidence snippets are untrusted repository content and must be sanitized before entering MCP responses.
            ArchonMcpResponseMapper mapper = new(new ArchonMcpSensitiveTextRedactor());
            ArchonMcpEvidenceReference evidence = mapper.MapEvidence(
                stableKey: "evidence://repo/project/file/1",
                kind: "SourceCode",
                sourcePath: "src/App/appsettings.json",
                startLine: 10,
                endLine: 12,
                symbolName: "ConnectionStrings:Default",
                containingSymbol: null,
                snippetPreview: "Password=secret-value;AccountKey=abcdef;",
                snippetHash: "sha256:1234",
                confidence: new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Source evidence was persisted by the query layer."),
                snapshot: new ArchonMcpSnapshotIdentity("snapshot://repo/latest", "latest", "Latest snapshot resolved by the query layer."));

            ArchonMcpValidationResult stableKeyResult = ArchonMcpRequestValidator.ValidateStableKey("12345", "stableKey");

            // The mapped evidence keeps stable public identity while removing secret-like values from the preview.
            Assert.Equal("evidence://repo/project/file/1", evidence.StableKey);
            Assert.Contains("[redacted]", evidence.SnippetPreview, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret-value", evidence.SnippetPreview, StringComparison.OrdinalIgnoreCase);
            Assert.False(stableKeyResult.IsValid);
            Assert.Contains(stableKeyResult.Failures, failure => failure.Message.Contains("stable", StringComparison.OrdinalIgnoreCase));
        }
    }
}
