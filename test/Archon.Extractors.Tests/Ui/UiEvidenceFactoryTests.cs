using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Ui;
using Xunit;

namespace Archon.Extractors.Tests.Ui
{
    /// <summary>
    /// Verifies the shared UI evidence helper behavior that UI-family extractors rely on for credential-safe graph explanations.
    /// </summary>
    public sealed class UiEvidenceFactoryTests
    {
        /// <summary>
        /// Confirms Razor evidence records use repository-relative paths, line spans, snippet hashes, redacted previews, confidence, and detection metadata.
        /// </summary>
        [Fact]
        public void CreateMarkupEvidenceRedactsSecretLikeValuesAndCapturesLocationMetadata()
        {
            // The scenario models a Razor directive with a secret-looking literal so the test covers evidence location and redaction in one focused assertion path.
            StableKey snapshotStableKey = new("snapshot://sample/run-1");
            UiSourceLocation location = new("src/Sample.Client/Pages/Index.razor", 2, 2, "@inject SecretStore Store password=\"open-sesame\"");

            EvidenceRecord evidence = UiEvidenceFactory.CreateMarkupEvidence(snapshotStableKey, location, "Blazor", "Inject", "StaticMarkup", Confidence.High, UnknownState.Known);

            Assert.Equal(EvidenceKind.SourceCode.Value, evidence.EvidenceKind.Value);
            Assert.Equal("src/Sample.Client/Pages/Index.razor", evidence.FilePath.Value);
            Assert.Equal(2, evidence.StartLine);
            Assert.Equal(2, evidence.EndLine);
            Assert.NotNull(evidence.SnippetHash);
            Assert.DoesNotContain("open-sesame", evidence.SnippetPreview, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", evidence.SnippetPreview, StringComparison.Ordinal);
            Assert.Equal(Confidence.High, evidence.Confidence);
            Assert.Equal(UnknownState.Known, evidence.UnknownState);
            Assert.Contains("\"detectionMode\":\"StaticMarkup\"", evidence.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Contains("\"uiFramework\":\"Blazor\"", evidence.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
        }
    }
}