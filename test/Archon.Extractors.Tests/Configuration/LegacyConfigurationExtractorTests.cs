using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;
using Archon.Extractors.Configuration;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Archon.Extractors.Tests.Configuration
{
    /// <summary>
    /// Verifies the legacy configuration extraction slice for XML configuration artifacts and ConfigurationManager source usage.
    /// </summary>
    public sealed class LegacyConfigurationExtractorTests
    {
        /// <summary>
        /// Confirms legacy XML configuration files produce configuration-key nodes for app settings, connection strings, custom sections, and binding redirects.
        /// </summary>
        [Fact]
        public void ExtractParsesLegacyConfigFilesWithRedactedEvidenceAndMetadata()
        {
            // The fixture includes app.config and web.config so discovery must treat legacy XML files as static artifacts without executing the application.
            ModernConfigurationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> configurationNodes = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.ConfigurationKey)
                .ToArray();

            Assert.Contains(configurationNodes, node => node.StableKey.Value == "config://Legacy:AppSettings:LegacyFeature");
            Assert.Contains(configurationNodes, node => node.StableKey.Value == "config://Legacy:ConnectionStrings:MainDb" && ContainsMetadata(node, "\"connectionString\":true"));
            Assert.Contains(configurationNodes, node => node.StableKey.Value == "config://Legacy:CustomSections:sampleSection" && ContainsMetadata(node, "\"customSection\":true"));
            Assert.Contains(configurationNodes, node => node.StableKey.Value == "config://Legacy:BindingRedirects:Newtonsoft.Json" && ContainsMetadata(node, "\"bindingRedirect\":true"));
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview));
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
        }

        /// <summary>
        /// Confirms ConfigurationManager AppSettings and ConnectionStrings usage produces USES_CONFIG relationships and unknown-source facts when definitions are not discovered.
        /// </summary>
        [Fact]
        public void ExtractDetectsConfigurationManagerUsageAndUnknownReferencedKeys()
        {
            // The source fixture references one file-defined app setting, one file-defined connection string, and one missing app setting.
            ModernConfigurationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> usesConfigEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.UsesConfig)
                .ToArray();

            Assert.Contains(usesConfigEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.Legacy.LegacyWorker" && edge.TargetNodeStableKey.Value == "config://Legacy:AppSettings:LegacyFeature" && ContainsMetadata(edge, "\"usageKind\":\"ConfigurationManager.AppSettings\""));
            Assert.Contains(usesConfigEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.Legacy.LegacyWorker" && edge.TargetNodeStableKey.Value == "config://Legacy:ConnectionStrings:MainDb" && ContainsMetadata(edge, "\"usageKind\":\"ConfigurationManager.ConnectionStrings\""));
            ArchitectureEdge missingEdge = Assert.Single(usesConfigEdges, edge => edge.TargetNodeStableKey.Value == "config://Legacy:AppSettings:MissingSetting");
            Assert.True(missingEdge.UnknownState.HasUnknownData);
            Assert.Equal(Confidence.Medium, missingEdge.Confidence);
            Assert.True(ContainsMetadata(missingEdge, "\"unknownSourceProvider\":true"));
        }

        /// <summary>
        /// Confirms malformed legacy XML produces a redacted warning and does not prevent partial facts from other files.
        /// </summary>
        [Fact]
        public void ExtractWarnsForMalformedLegacyXmlWithoutLeakingSecrets()
        {
            // Malformed configuration must be visible to callers as a warning while other repository-contained configuration files still contribute facts.
            ModernConfigurationExtractionResult result = ExtractFixture();

            Assert.Contains(result.Warnings, warning => warning.Contains("Unable to parse legacy configuration file", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Warnings, ContainsSensitiveText);
            Assert.Contains(result.Snapshot.Nodes, node => node.StableKey.Value == "config://Legacy:AppSettings:LegacyFeature");
        }

        /// <summary>
        /// Determines whether a node metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="node">The architecture node whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the node metadata.</param>
        /// <returns><see langword="true"/> when the metadata contains the fragment; otherwise, <see langword="false"/>.</returns>
        private static bool ContainsMetadata(ArchitectureNode node, string expectedFragment)
        {
            // Canonical JSON comparisons keep assertions stable regardless of dictionary construction order.
            return node.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an edge metadata payload contains an expected canonical JSON fragment.
        /// </summary>
        /// <param name="edge">The architecture edge whose metadata should be inspected.</param>
        /// <param name="expectedFragment">The canonical JSON fragment expected in the edge metadata.</param>
        /// <returns><see langword="true"/> when the metadata contains the fragment; otherwise, <see langword="false"/>.</returns>
        private static bool ContainsMetadata(ArchitectureEdge edge, string expectedFragment)
        {
            // Edge metadata assertions verify legacy usage classification without depending on object reference identity.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a value contains any sensitive literal from the legacy fixture.
        /// </summary>
        /// <param name="value">The value to inspect for fixture secrets.</param>
        /// <returns><see langword="true"/> when a known sensitive literal appears; otherwise, <see langword="false"/>.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // Legacy tests verify that XML values and diagnostics do not leak password-like or token-like values.
            return value?.Contains("LegacySecret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("MalformedSecret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("ApiTokenValue", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("Password=", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Builds a temporary legacy repository fixture, creates a Roslyn semantic document, and invokes the legacy configuration extractor.
        /// </summary>
        /// <returns>The configuration extraction result for the legacy fixture repository and source document.</returns>
        private static ModernConfigurationExtractionResult ExtractFixture()
        {
            // The fixture writes legacy XML and C# source into one repository root so artifact and semantic usage facts can be correlated.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-legacy-config-fixture", Guid.NewGuid().ToString("N"));
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.Legacy");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(projectDirectory, "app.config"), """
                <?xml version="1.0" encoding="utf-8" ?>
                <configuration>
                  <configSections>
                    <section name="sampleSection" type="Sample.Legacy.SampleSection, Sample.Legacy" />
                  </configSections>
                  <appSettings>
                    <add key="LegacyFeature" value="true" />
                    <add key="ApiToken" value="ApiTokenValue" />
                  </appSettings>
                  <connectionStrings>
                    <add name="MainDb" connectionString="Server=.;Database=Legacy;User Id=sa;Password=LegacySecret;" providerName="System.Data.SqlClient" />
                  </connectionStrings>
                  <sampleSection enabled="true" secret="LegacySecret" />
                  <runtime>
                    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                      <dependentAssembly>
                        <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                        <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                      </dependentAssembly>
                    </assemblyBinding>
                  </runtime>
                </configuration>
                """);
            File.WriteAllText(Path.Combine(projectDirectory, "web.config"), """
                <?xml version="1.0" encoding="utf-8" ?>
                <configuration>
                  <appSettings>
                    <add key="WebMode" value="Classic" />
                  </appSettings>
                </configuration>
                """);
            File.WriteAllText(Path.Combine(projectDirectory, "broken.config"), """
                <configuration><appSettings><add key="BrokenSecret" value="MalformedSecret"></appSettings>
                """);
            string sourcePath = Path.Combine(projectDirectory, "LegacyWorker.cs");
            string source = """
                namespace System.Configuration
                {
                    public sealed class ConnectionStringSettings
                    {
                        public string? ConnectionString { get; set; }
                    }

                    public sealed class ConnectionStringSettingsCollection
                    {
                        public ConnectionStringSettings? this[string name] => null;
                    }

                    public sealed class NameValueCollection
                    {
                        public string? this[string name] => null;
                    }

                    public static class ConfigurationManager
                    {
                        public static NameValueCollection AppSettings { get; } = new();
                        public static ConnectionStringSettingsCollection ConnectionStrings { get; } = new();
                    }
                }

                namespace Sample.Legacy
                {
                    using System.Configuration;

                    public sealed class LegacyWorker
                    {
                        public void Run()
                        {
                            string? enabled = ConfigurationManager.AppSettings["LegacyFeature"];
                            string? missing = ConfigurationManager.AppSettings["MissingSetting"];
                            string? connection = ConfigurationManager.ConnectionStrings["MainDb"]?.ConnectionString;
                        }
                    }
                }
                """;
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "Sample.Legacy",
                [syntaxTree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Microsoft.CodeAnalysis.SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SemanticExtractionRequest semanticRequest = new(repositoryRoot, "src/Sample.Legacy/Sample.Legacy.csproj", sourcePath, syntaxTree, semanticModel);
            ModernConfigurationExtractionRequest request = new(StableKeyGenerator.ForRepository("Sample.Legacy"), repositoryRoot, [semanticRequest]);
            LegacyConfigurationExtractor extractor = new();

            return extractor.Extract(request);
        }
    }
}
