using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Configuration;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.Configuration.Tests
{
    /// <summary>
    /// Verifies the top-level configuration extractor that composes modern and legacy configuration slices.
    /// </summary>
    public sealed class ConfigurationExtractorTests
    {
        /// <summary>
        /// Confirms the top-level configuration extractor composes modern and legacy slices into one merged snapshot.
        /// </summary>
        [Fact]
        public void ExtractComposesModernAndLegacySlices()
        {
            // The composed extractor is the intended higher-level entry point when callers need every current configuration source family.
            ModernConfigurationExtractionResult result = ExtractFixture();

            Assert.Contains(result.Snapshot.Nodes, node => node.StableKey.Value == "config://Service:Endpoint");
            Assert.Contains(result.Snapshot.Nodes, node => node.StableKey.Value == "config://Legacy:AppSettings:LegacyFeature");
            Assert.Contains(result.Snapshot.Edges, edge => edge.TargetNodeStableKey.Value == "config://Legacy:AppSettings:LegacyFeature" && ContainsMetadata(edge, "\"usageKind\":\"ConfigurationManager.AppSettings\""));
        }

        /// <summary>
        /// Confirms diagnostics from both composed slices are preserved in the merged result.
        /// </summary>
        [Fact]
        public void ExtractPreservesWarningsFromBothSlices()
        {
            // The fixture contains one dynamic modern key and one malformed legacy XML file so both slices must contribute warnings.
            ModernConfigurationExtractionResult result = ExtractFixture();

            Assert.Contains(result.Warnings, warning => warning.Contains("Dynamic configuration key", StringComparison.Ordinal));
            Assert.Contains(result.Warnings, warning => warning.Contains("Unable to parse legacy configuration file", StringComparison.Ordinal));
        }

        /// <summary>
        /// Confirms composed output remains deterministic when slices contribute stable-keyed graph facts.
        /// </summary>
        [Fact]
        public void ExtractProducesDeterministicMergedStableKeys()
        {
            // Running extraction twice against the same fixture shape should produce the same logical node and edge stable keys.
            ModernConfigurationExtractionResult first = ExtractFixture();
            ModernConfigurationExtractionResult second = ExtractFixture();

            Assert.Equal(
                first.Snapshot.Nodes.Select(node => node.StableKey.Value).Order(StringComparer.Ordinal).ToArray(),
                second.Snapshot.Nodes.Select(node => node.StableKey.Value).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(
                first.Snapshot.Edges.Select(edge => edge.StableKey.Value).Order(StringComparer.Ordinal).ToArray(),
                second.Snapshot.Edges.Select(edge => edge.StableKey.Value).Order(StringComparer.Ordinal).ToArray());
        }

        /// <summary>
        /// Determines whether an edge metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="edge">The architecture edge whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the edge metadata.</param>
        /// <returns><see langword="true"/> when the metadata contains the fragment; otherwise, <see langword="false"/>.</returns>
        private static bool ContainsMetadata(ArchitectureEdge edge, string expectedFragment)
        {
            // Edge metadata assertions verify that the composed extractor preserved slice-specific usage classification.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds a temporary repository fixture, creates a Roslyn semantic document, and invokes the composed configuration extractor.
        /// </summary>
        /// <returns>The composed configuration extraction result for the fixture repository and source document.</returns>
        private static ModernConfigurationExtractionResult ExtractFixture()
        {
            // The fixture deliberately mixes appsettings, .config, modern source usage, and legacy source usage to exercise composition rather than one slice.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-composed-config-fixture", Guid.NewGuid().ToString("N"));
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.App");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(projectDirectory, "appsettings.json"), """
                {
                  "Service": {
                    "Endpoint": "https://api.example.test"
                  }
                }
                """);
            File.WriteAllText(Path.Combine(projectDirectory, "app.config"), """
                <?xml version="1.0" encoding="utf-8" ?>
                <configuration>
                  <appSettings>
                    <add key="LegacyFeature" value="true" />
                  </appSettings>
                </configuration>
                """);
            File.WriteAllText(Path.Combine(projectDirectory, "broken.config"), """
                <configuration><appSettings><add key="Broken" value="true"></appSettings>
                """);
            string sourcePath = Path.Combine(projectDirectory, "Program.cs");
            string source = """
                namespace System.Configuration
                {
                    public sealed class NameValueCollection
                    {
                        public string? this[string name] => null;
                    }

                    public static class ConfigurationManager
                    {
                        public static NameValueCollection AppSettings { get; } = new();
                    }
                }

                namespace Microsoft.Extensions.Configuration
                {
                    public interface IConfiguration
                    {
                        string? this[string key] { get; set; }
                    }
                }

                namespace Sample.App
                {
                    using System.Configuration;
                    using Microsoft.Extensions.Configuration;

                    public sealed class Worker
                    {
                        public void Run(IConfiguration configuration)
                        {
                            string? endpoint = configuration["Service:Endpoint"];
                            string dynamicKey = "Service" + ":Endpoint";
                            string? dynamicValue = configuration[dynamicKey];
                            string? legacy = ConfigurationManager.AppSettings["LegacyFeature"];
                        }
                    }
                }
                """;
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.App",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest semanticRequest = new(repositoryRoot, "src/Sample.App/Sample.App.csproj", sourcePath, syntaxTree, semanticModel);
            ModernConfigurationExtractionRequest request = new(StableKeyGenerator.ForRepository("Sample.App"), repositoryRoot, [semanticRequest]);
            ConfigurationExtractor extractor = new();

            return extractor.Extract(request);
        }
    }
}
