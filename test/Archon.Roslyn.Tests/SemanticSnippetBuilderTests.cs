using Archon.Roslyn.SemanticModel;
using Xunit;

namespace Archon.Roslyn.Tests
{
    /// <summary>
    /// Verifies deterministic source snippet preview and hash behavior for semantic evidence.
    /// </summary>
    public sealed class SemanticSnippetBuilderTests
    {
        /// <summary>
        /// Confirms that snippet previews collapse whitespace and respect the requested preview limit.
        /// </summary>
        [Fact]
        public void CreatePreviewNormalizesWhitespaceAndAppliesLimit()
        {
            // The preview should be compact enough for evidence display and deterministic across line-ending styles.
            string? preview = SemanticSnippetBuilder.CreatePreview("  public\r\n   class   Widget   { }  ", previewLimit: 20);

            Assert.Equal("public class Widget", preview);
        }

        /// <summary>
        /// Confirms that snippet hashes are deterministic for identical source text.
        /// </summary>
        [Fact]
        public void CreateHashReturnsSameHashForSameSnippet()
        {
            // Hash determinism lets later extraction runs compare evidence spans without storing whole source files.
            string? firstHash = SemanticSnippetBuilder.CreateHash("public class Widget { }");
            string? secondHash = SemanticSnippetBuilder.CreateHash("public class Widget { }");

            Assert.Equal(firstHash, secondHash);
            Assert.StartsWith("sha256:", firstHash, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms that blank snippets do not create misleading preview or hash values.
        /// </summary>
        [Fact]
        public void BlankSnippetReturnsNullPreviewAndHash()
        {
            // Missing or whitespace-only source text should be represented as unavailable evidence details rather than fake values.
            Assert.Null(SemanticSnippetBuilder.CreatePreview("   "));
            Assert.Null(SemanticSnippetBuilder.CreateHash("   "));
        }
    }
}
