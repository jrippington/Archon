using Archon.Roslyn.SemanticModel;
using Xunit;

namespace Archon.Roslyn.Tests
{
    /// <summary>
    /// Verifies repository-relative path normalization for semantic source evidence.
    /// </summary>
    public sealed class SemanticPathNormalizerTests
    {
        /// <summary>
        /// Confirms that absolute document paths under the repository root are converted to forward-slash repository-relative paths.
        /// </summary>
        [Fact]
        public void ToRepositoryRelativePathReturnsRepositoryRelativePathForDocumentInsideRoot()
        {
            // The test uses platform path APIs for setup, then asserts the extractor-facing normalized form.
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-path-root"));
            string documentPath = Path.Combine(repositoryRoot, "src", "Sample", "Widget.cs");

            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRoot, documentPath);

            Assert.Equal("src/Sample/Widget.cs", relativePath);
        }

        /// <summary>
        /// Confirms that already-relative paths are normalized without requiring a physical file.
        /// </summary>
        [Fact]
        public void ToRepositoryRelativePathNormalizesAlreadyRelativePath()
        {
            // In-memory Roslyn tests often provide relative paths directly, and the helper should keep those fixture paths deterministic.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath("C:/repo", @".\src\Sample\Widget.cs");

            Assert.Equal("src/Sample/Widget.cs", relativePath);
        }

        /// <summary>
        /// Confirms that absolute document paths outside the repository root are rejected before evidence is created.
        /// </summary>
        [Fact]
        public void ToRepositoryRelativePathRejectsDocumentOutsideRoot()
        {
            // Rejecting outside-root paths prevents developer-machine paths from leaking into graph evidence.
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-root-a"));
            string documentPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archon-root-b", "Widget.cs"));

            Assert.Throws<ArgumentException>(() => SemanticPathNormalizer.ToRepositoryRelativePath(repositoryRoot, documentPath));
        }
    }
}
