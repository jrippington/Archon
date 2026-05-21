using Archon.Roslyn.SemanticModel;
using Xunit;

namespace Archon.Roslyn.Tests
{
    /// <summary>
    /// Verifies deterministic stable-key generation for semantic declarations and relationships.
    /// </summary>
    public sealed class SemanticStableKeyBuilderTests
    {
        /// <summary>
        /// Confirms that equivalent semantic declaration input produces the same stable key every time.
        /// </summary>
        [Fact]
        public void ForDeclarationReturnsSameKeyForEquivalentInput()
        {
            // The symbol identity mirrors a Roslyn-extracted type and should be stable across repeated extraction runs.
            SemanticSymbolIdentity symbolIdentity = new("Sample.Widget", "Widget", "Sample.Widget", "Sample");

            string firstKey = SemanticStableKeyBuilder.ForDeclaration(SemanticDeclarationKind.Type, SourceLanguage.CSharp, "src/Sample/Sample.csproj", symbolIdentity);
            string secondKey = SemanticStableKeyBuilder.ForDeclaration(SemanticDeclarationKind.Type, SourceLanguage.CSharp, "src/Sample/Sample.csproj", symbolIdentity);

            Assert.Equal(firstKey, secondKey);
            Assert.StartsWith("semantic-type://", firstKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms that project context participates in declaration stable keys.
        /// </summary>
        [Fact]
        public void ForDeclarationUsesProjectContextToDisambiguateSymbols()
        {
            // Two projects can contain same-named source symbols, so project context must be part of the deterministic payload.
            SemanticSymbolIdentity symbolIdentity = new("Sample.Widget", "Widget", "Sample.Widget", "Sample");

            string firstKey = SemanticStableKeyBuilder.ForDeclaration(SemanticDeclarationKind.Type, SourceLanguage.CSharp, "src/First/First.csproj", symbolIdentity);
            string secondKey = SemanticStableKeyBuilder.ForDeclaration(SemanticDeclarationKind.Type, SourceLanguage.CSharp, "src/Second/Second.csproj", symbolIdentity);

            Assert.NotEqual(firstKey, secondKey);
        }

        /// <summary>
        /// Confirms that relationship stable keys are deterministic for the same endpoints.
        /// </summary>
        [Fact]
        public void ForRelationshipReturnsSameKeyForEquivalentEndpoints()
        {
            // Relationship keys are endpoint-derived so duplicate containment discoveries can be de-duplicated by stable key.
            string firstKey = SemanticStableKeyBuilder.ForRelationship(SemanticRelationshipKind.Contains, "semantic-namespace://a", "semantic-type://b");
            string secondKey = SemanticStableKeyBuilder.ForRelationship(SemanticRelationshipKind.Contains, "semantic-namespace://a", "semantic-type://b");

            Assert.Equal(firstKey, secondKey);
            Assert.StartsWith("semantic-relationship://contains/", firstKey, StringComparison.Ordinal);
        }
    }
}
