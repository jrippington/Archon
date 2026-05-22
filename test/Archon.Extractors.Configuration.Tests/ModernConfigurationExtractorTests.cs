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
    /// Verifies the WP007 modern configuration extraction slice for appsettings files, configuration API usage, options binding, and redaction.
    /// </summary>
    public sealed class ModernConfigurationExtractorTests
    {
        /// <summary>
        /// Confirms appsettings JSON files produce deterministic ConfigurationKey nodes, redacted evidence, and environment metadata without executing application code.
        /// </summary>
        [Fact]
        public void ExtractParsesAppsettingsFilesWithNormalizedKeysAndRedactedEvidence()
        {
            // The fixture includes base and environment-specific appsettings files so extraction must treat them as data artifacts, not runtime configuration.
            ModernConfigurationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureNode> configurationNodes = result.Snapshot.Nodes
                .Where(node => node.NodeKind == NodeKind.ConfigurationKey)
                .ToArray();

            Assert.Contains(configurationNodes, node => node.StableKey.Value == "config://ConnectionStrings:Default" && ContainsMetadata(node, "\"environment\":\"Base\"") && ContainsMetadata(node, "\"provider\":\"JsonConfigurationFile\""));
            Assert.Contains(configurationNodes, node => node.StableKey.Value == "config://Service:Endpoint");
            Assert.Contains(configurationNodes, node => node.StableKey.Value == "config://Logging:LogLevel:Default");
            Assert.DoesNotContain(result.Snapshot.Evidence, evidence => ContainsSensitiveText(evidence.SnippetPreview));
            Assert.DoesNotContain(result.Snapshot.Nodes, node => ContainsSensitiveText(node.Metadata.ToCanonicalJson()));
            Assert.Empty(result.Errors);
        }

        /// <summary>
        /// Confirms modern configuration source APIs and options binding create USES_CONFIG relationships with source-code evidence and confidence metadata.
        /// </summary>
        [Fact]
        public void ExtractDetectsConfigurationApiUsageAndOptionsBindingRelationships()
        {
            // The source fixture uses indexers, GetSection, Bind, Get<T>, Configure<TOptions>, and options injection to exercise the modern API catalog.
            ModernConfigurationExtractionResult result = ExtractFixture();
            IReadOnlyList<ArchitectureEdge> usesConfigEdges = result.Snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.UsesConfig)
                .ToArray();

            Assert.Contains(usesConfigEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.Worker" && edge.TargetNodeStableKey.Value == "config://Service:Endpoint" && ContainsMetadata(edge, "\"usageKind\":\"IConfigurationIndexer\""));
            Assert.Contains(usesConfigEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.Worker" && edge.TargetNodeStableKey.Value == "config://FeatureFlags" && ContainsMetadata(edge, "\"usageKind\":\"GetSection\""));
            Assert.Contains(usesConfigEdges, edge => edge.TargetNodeStableKey.Value == "config://Service" && ContainsMetadata(edge, "\"usageKind\":\"ConfigureOptions\"") && ContainsMetadata(edge, "\"optionsType\":\"Sample.App.ServiceOptions\""));
            Assert.Contains(usesConfigEdges, edge => edge.SourceNodeStableKey.Value == "type://Sample.App.ServiceOptions" && edge.TargetNodeStableKey.Value == "config://ServiceOptions" && ContainsMetadata(edge, "\"usageKind\":\"OptionsInjection\""));
            Assert.Contains(result.Snapshot.Evidence, evidence => evidence.EvidenceKind == EvidenceKind.SourceCode && evidence.SnippetPreview?.Contains("configuration[\"Service:Endpoint\"]", StringComparison.Ordinal) == true);
        }

        /// <summary>
        /// Confirms dynamic configuration keys are modeled as explicit unknowns and secret values are absent from diagnostics and metadata.
        /// </summary>
        [Fact]
        public void ExtractModelsDynamicKeysAsUnknownAndNeverEmitsSensitiveText()
        {
            // Dynamic keys are useful evidence but not stable configuration identities, so they must not be represented as certain facts.
            ModernConfigurationExtractionResult result = ExtractFixture();
            ArchitectureEdge dynamicEdge = Assert.Single(result.Snapshot.Edges, edge => edge.EdgeKind == EdgeKind.UsesConfig && edge.TargetNodeStableKey.Value.StartsWith("config://Unknown:", StringComparison.Ordinal));

            Assert.True(dynamicEdge.UnknownState.HasUnknownData);
            Assert.Equal(Confidence.Medium, dynamicEdge.Confidence);
            Assert.Contains(result.Warnings, warning => warning.Contains("Dynamic configuration key", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Warnings, ContainsSensitiveText);
            Assert.DoesNotContain(result.Errors, ContainsSensitiveText);
            Assert.DoesNotContain(result.Snapshot.Edges, edge => ContainsSensitiveText(edge.Metadata.ToCanonicalJson()));
        }

        /// <summary>
        /// Confirms the modern extractor does not claim responsibility for legacy XML configuration artifacts or ConfigurationManager usage.
        /// </summary>
        [Fact]
        public void ExtractDoesNotEmitLegacyConfigurationFacts()
        {
            // Responsibility boundaries matter because legacy .config support is owned by LegacyConfigurationExtractor rather than this modern slice.
            ModernConfigurationExtractionResult result = ExtractFixture();

            Assert.DoesNotContain(result.Snapshot.Nodes, node => node.StableKey.Value.StartsWith("config://Legacy:", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Snapshot.Edges, edge => edge.Metadata.ToCanonicalJson().Contains("ConfigurationManager", StringComparison.Ordinal));
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
            // Edge metadata assertions verify usage classification without depending on object reference identity.
            return edge.Metadata.ToCanonicalJson().Contains(expectedFragment, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a value contains any secret literal from the test fixture.
        /// </summary>
        /// <param name="value">The value to inspect for fixture secrets.</param>
        /// <returns><see langword="true"/> when a known sensitive literal appears; otherwise, <see langword="false"/>.</returns>
        private static bool ContainsSensitiveText(string? value)
        {
            // The test checks every externally visible output surface that could accidentally leak configuration secrets.
            return value?.Contains("SuperSecret", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("password=", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("ApiKeyValue", StringComparison.OrdinalIgnoreCase) == true
                || value?.Contains("ClientSecretValue", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Builds a temporary repository fixture, creates a Roslyn semantic document, and invokes the production configuration extractor.
        /// </summary>
        /// <returns>The modern configuration extraction result for the fixture repository and source document.</returns>
        private static ModernConfigurationExtractionResult ExtractFixture()
        {
            // Each test run uses an isolated repository root so appsettings discovery proves repository-relative determinism.
            string repositoryRoot = Path.Combine(Path.GetTempPath(), "archon-modern-config-fixture", Guid.NewGuid().ToString("N"));
            string projectDirectory = Path.Combine(repositoryRoot, "src", "Sample.App");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(projectDirectory, "appsettings.json"), """
                {
                  "ConnectionStrings": {
                    "Default": "Server=.;Database=Sample;User Id=sa;Password=SuperSecret;"
                  },
                  "Service": {
                    "Endpoint": "https://api.example.test",
                    "ApiKey": "ApiKeyValue"
                  },
                  "ClientSecret": "ClientSecretValue",
                  "FeatureFlags": {
                    "Enabled": true
                  },
                  "Logging": {
                    "LogLevel": {
                      "Default": "Information"
                    }
                  }
                }
                """);
            File.WriteAllText(Path.Combine(projectDirectory, "appsettings.Development.json"), """
                {
                  "Service": {
                    "Endpoint": "https://dev.example.test"
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
                        IConfigurationSection GetSection(string key);
                    }

                    public interface IConfigurationSection : IConfiguration
                    {
                    }

                    public static class ConfigurationBinder
                    {
                        public static void Bind(this IConfiguration configuration, string key, object instance) { }
                        public static T? Get<T>(this IConfiguration configuration) => default;
                    }
                }

                namespace Microsoft.Extensions.DependencyInjection
                {
                    using System;
                    using Microsoft.Extensions.Configuration;

                    public interface IServiceCollection
                    {
                    }

                    public static class OptionsConfigurationServiceCollectionExtensions
                    {
                        public static IServiceCollection Configure<TOptions>(this IServiceCollection services, IConfiguration configuration) where TOptions : class => services;
                        public static IServiceCollection Configure<TOptions>(this IServiceCollection services, IConfiguration configuration, Action<TOptions> configureOptions) where TOptions : class => services;
                    }
                }

                namespace Microsoft.Extensions.Options
                {
                    public interface IOptions<TOptions>
                    {
                    }

                    public interface IOptionsMonitor<TOptions>
                    {
                    }

                    public interface IOptionsSnapshot<TOptions>
                    {
                    }
                }

                namespace Sample.App
                {
                    using System.Configuration;
                    using Microsoft.Extensions.Configuration;
                    using Microsoft.Extensions.DependencyInjection;
                    using Microsoft.Extensions.Options;

                    public sealed class ServiceOptions
                    {
                        public string? Endpoint { get; set; }
                    }

                    public sealed class Worker
                    {
                        public Worker(IConfiguration configuration, IOptions<ServiceOptions> options, IOptionsMonitor<ServiceOptions> monitor, IOptionsSnapshot<ServiceOptions> snapshot)
                        {
                            string? endpoint = configuration["Service:Endpoint"];
                            IConfigurationSection flags = configuration.GetSection("FeatureFlags");
                            configuration.GetSection("Service").Bind(new ServiceOptions());
                            ServiceOptions? bound = configuration.GetSection("Service").Get<ServiceOptions>();
                            string dynamicKey = "Service" + ":Endpoint";
                            string? dynamicValue = configuration[dynamicKey];
                            string? legacy = ConfigurationManager.AppSettings["LegacyFeature"];
                        }
                    }

                    public static class Startup
                    {
                        public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
                        {
                            services.Configure<ServiceOptions>(configuration.GetSection("Service"));
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
            ModernConfigurationExtractor modernExtractor = new();
            return modernExtractor.Extract(request);
        }
    }
}
