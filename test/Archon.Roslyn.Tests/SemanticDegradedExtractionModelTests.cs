using Archon.Roslyn.SemanticModel;
using Xunit;

namespace Archon.Roslyn.Tests
{
    /// <summary>
    /// Verifies the shared graph-ready contracts used when semantic extraction succeeds only partially.
    /// </summary>
    public sealed class SemanticDegradedExtractionModelTests
    {
        /// <summary>
        /// Confirms that diagnostics are normalized into deterministic graph-ready metadata without requiring callers to keep Roslyn diagnostic instances alive.
        /// </summary>
        [Fact]
        public void DiagnosticFactNormalizesCompilerDiagnosticDetails()
        {
            // The model boundary trims text and preserves source coordinates so diagnostics can be compared and persisted deterministically.
            SemanticEvidence evidence = CreateEvidence("Broken", "src/App/Broken.cs");
            SemanticDiagnosticFact diagnostic = new(
                " CS0246 ",
                SemanticDiagnosticSeverity.Error,
                " The type or namespace name could not be found ",
                " CSharp ",
                evidence,
                new Dictionary<string, string>
                {
                    ["compilerSource"] = "CSharp"
                });

            Assert.Equal("CS0246", diagnostic.DiagnosticId);
            Assert.Equal(SemanticDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("The type or namespace name could not be found", diagnostic.Message);
            Assert.Equal("CSharp", diagnostic.CompilerSource);
            Assert.Equal("src/App/Broken.cs", diagnostic.Evidence.RepositoryRelativeFilePath);
            Assert.Equal("CSharp", diagnostic.Metadata["compilerSource"]);
        }

        /// <summary>
        /// Confirms that unknown facts keep reason, confidence, evidence, source context, and metadata explicit for later graph queries.
        /// </summary>
        [Fact]
        public void UnknownFactPreservesQueryableUnresolvedContext()
        {
            // Unknown facts intentionally carry a source member but no resolved target identity because they represent a semantic gap rather than an invented dependency.
            SemanticSymbolIdentity sourceIdentity = new("Sample.Worker.Run()", "Run", "Sample.Worker.Run()", "Sample.Worker");
            SemanticUnknownFact unknown = new(
                "semantic-unknown://abc",
                SemanticUnknownReason.DynamicDispatch,
                SourceLanguage.CSharp,
                "src/Sample/Sample.csproj",
                sourceIdentity,
                "dynamic target invocation",
                CreateEvidence("Run", "src/Sample/Worker.cs"),
                SemanticFactConfidence.Unresolved,
                new Dictionary<string, string>
                {
                    ["operation"] = "Invocation"
                });

            Assert.Equal(SemanticUnknownReason.DynamicDispatch, unknown.Reason);
            Assert.Equal(SourceLanguage.CSharp, unknown.SourceLanguage);
            Assert.Equal("Sample.Worker.Run()", unknown.SourceSymbolIdentity?.FullyQualifiedName);
            Assert.Equal("dynamic target invocation", unknown.Description);
            Assert.Equal(SemanticFactConfidence.Unresolved, unknown.Confidence);
            Assert.Equal("Invocation", unknown.Metadata["operation"]);
        }

        /// <summary>
        /// Confirms that extraction results copy degraded semantic facts into immutable result collections alongside declarations and relationships.
        /// </summary>
        [Fact]
        public void ExtractionResultCarriesDiagnosticsUnknownsAndEvidenceContributions()
        {
            // The result model is the shared accumulation boundary, so degraded facts must travel with ordinary semantic facts instead of being hidden in warning strings.
            SemanticEvidence evidence = CreateEvidence("PartialWidget", "src/Sample/PartialWidget.cs");
            SemanticDiagnosticFact diagnostic = new("CS0103", SemanticDiagnosticSeverity.Error, "Name does not exist", "CSharp", evidence, metadata: null);
            SemanticUnknownFact unknown = new("semantic-unknown://name", SemanticUnknownReason.UnresolvedSymbol, SourceLanguage.CSharp, "src/Sample/Sample.csproj", sourceSymbolIdentity: null, "Name could not be resolved", evidence, SemanticFactConfidence.Unresolved, metadata: null);
            SemanticEvidenceContribution contribution = new("semantic-type://partial", evidence, generated: false, "PartialDeclaration");

            SemanticExtractionResult result = new(
                declarations: null,
                relationships: null,
                warnings: ["warning"],
                errors: null,
                diagnostics: [diagnostic],
                unknowns: [unknown],
                evidenceContributions: [contribution]);

            Assert.Empty(result.Declarations);
            Assert.Empty(result.Relationships);
            Assert.Single(result.Warnings);
            Assert.Single(result.Diagnostics);
            Assert.Single(result.Unknowns);
            Assert.Single(result.EvidenceContributions);
        }

        /// <summary>
        /// Confirms that stable keys for unknown and diagnostic records are deterministic and keep different reasons distinct.
        /// </summary>
        [Fact]
        public void StableKeyBuilderCreatesDeterministicDegradedFactKeys()
        {
            // Diagnostics and unknowns are graph facts in their own right and need stable keys for repeatable accumulation.
            SemanticEvidence evidence = CreateEvidence("Run", "src/Sample/Worker.cs");
            string firstUnknownKey = SemanticStableKeyBuilder.ForUnknown(SourceLanguage.CSharp, "src/Sample/Sample.csproj", SemanticUnknownReason.ReflectionTarget, evidence, "typeof lookup");
            string secondUnknownKey = SemanticStableKeyBuilder.ForUnknown(SourceLanguage.CSharp, "src/Sample/Sample.csproj", SemanticUnknownReason.ReflectionTarget, evidence, "typeof lookup");
            string differentUnknownKey = SemanticStableKeyBuilder.ForUnknown(SourceLanguage.CSharp, "src/Sample/Sample.csproj", SemanticUnknownReason.DynamicDispatch, evidence, "typeof lookup");
            string diagnosticKey = SemanticStableKeyBuilder.ForDiagnostic("CS0246", evidence);

            Assert.Equal(firstUnknownKey, secondUnknownKey);
            Assert.NotEqual(firstUnknownKey, differentUnknownKey);
            Assert.StartsWith("semantic-unknown://", firstUnknownKey, StringComparison.Ordinal);
            Assert.StartsWith("semantic-diagnostic://", diagnosticKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// Creates compact source evidence for shared model assertions.
        /// </summary>
        /// <param name="symbolName">The symbol name to place in the evidence record.</param>
        /// <param name="path">The repository-relative source path to place in the evidence record.</param>
        /// <returns>A semantic evidence record with deterministic values for tests.</returns>
        private static SemanticEvidence CreateEvidence(string symbolName, string path)
        {
            // Tests do not need real Roslyn spans here because they validate model normalization rather than syntax-tree integration.
            return new SemanticEvidence(path, 1, 1, 1, 10, symbolName, containingSymbolName: null, snippetPreview: symbolName, snippetHash: "sha256:test");
        }
    }
}
