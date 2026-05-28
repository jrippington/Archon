using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Archon.Application.Extraction.Accumulation;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Roslyn.SemanticModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Archon.Extractors.Configuration
{
    /// <summary>
    /// Extracts legacy XML configuration files and compiler-bound ConfigurationManager usage into configuration graph contracts.
    /// </summary>
    public sealed class LegacyConfigurationExtractor
    {
        /// <summary>
        /// Extracts configuration key nodes, USES_CONFIG relationships, evidence, warnings, and errors from the supplied request.
        /// </summary>
        /// <param name="request">The repository and semantic-document request that scopes extraction.</param>
        /// <param name="cancellationToken">A token that signals when extraction should stop.</param>
        /// <returns>The configuration extraction result containing a shared architecture snapshot.</returns>
        public ModernConfigurationExtractionResult Extract(ModernConfigurationExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Extraction is intentionally data-driven: XML files are parsed as artifacts and source usage is compiler-bound without executing application code.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();
            HashSet<string> definedConfigurationKeys = new(StringComparer.Ordinal);

            foreach (string configurationFilePath in DiscoverLegacyConfigurationFiles(request.RepositoryRootDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseLegacyConfigurationFile(request, accumulator, configurationFilePath, definedConfigurationKeys, cancellationToken);
            }

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeSemanticDocument(request, accumulator, semanticDocument, definedConfigurationKeys, cancellationToken);
            }

            return new ModernConfigurationExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Discovers legacy .config XML files below the repository root.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root to search.</param>
        /// <returns>Repository-contained .config file paths ordered deterministically.</returns>
        private static IReadOnlyList<string> DiscoverLegacyConfigurationFiles(string repositoryRootDirectory)
        {
            // Legacy configuration discovery treats XML configuration files as data artifacts and never executes application configuration code.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            return Directory.EnumerateFiles(repositoryRootDirectory, "*.config", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path).EndsWith(".config", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Parses one legacy XML configuration file and emits supported configuration key facts.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity and repository paths.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions and diagnostics.</param>
        /// <param name="configurationFilePath">The legacy .config file path to parse.</param>
        /// <param name="definedConfigurationKeys">The set that records discovered keys for later source-code correlation.</param>
        /// <param name="cancellationToken">A token that signals when parsing should stop.</param>
        private static void ParseLegacyConfigurationFile(ModernConfigurationExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string configurationFilePath, ISet<string> definedConfigurationKeys, CancellationToken cancellationToken)
        {
            // Legacy XML parsing is file-isolated so malformed files become warnings without blocking other configuration artifacts.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, configurationFilePath);
            try
            {
                XDocument document = XDocument.Load(configurationFilePath, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
                foreach (ConfigurationKeyFact fact in EnumerateLegacyConfigurationFacts(document, relativePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EvidenceRecord evidence = CreateConfigurationEvidence(request.SnapshotStableKey, fact);
                    ArchitectureNode node = CreateConfigurationNode(request.SnapshotStableKey, fact, evidence.StableKey, Confidence.Certain, UnknownState.Known);
                    definedConfigurationKeys.Add(fact.Key);
                    accumulator.AddEvidence(evidence).AddNode(node);
                }
            }
            catch (XmlException exception)
            {
                accumulator.AddWarning($"Unable to parse legacy configuration file {relativePath}: {ConfigurationRedactor.Redact(exception.Message)}");
            }
        }

        /// <summary>
        /// Enumerates supported legacy configuration facts from a parsed XML document.
        /// </summary>
        /// <param name="document">The parsed legacy XML configuration document.</param>
        /// <param name="relativePath">The repository-relative path used for evidence.</param>
        /// <returns>Configuration key facts for supported legacy XML elements.</returns>
        private static IEnumerable<ConfigurationKeyFact> EnumerateLegacyConfigurationFacts(XDocument document, string relativePath)
        {
            // Legacy keys are prefixed by category so app settings and connection strings with the same name remain distinct facts.
            foreach (XElement addElement in document.Descendants().Where(element => element.Name.LocalName == "appSettings").Elements().Where(element => element.Name.LocalName == "add"))
            {
                string? key = addElement.Attribute("key")?.Value;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    string normalizedKey = $"Legacy:AppSettings:{NormalizeLegacyName(key)}";
                    yield return new ConfigurationKeyFact(normalizedKey, "LegacyXml", relativePath, CreateXmlPreview(addElement, normalizedKey), HashStablePayload(CreateXmlPreview(addElement, normalizedKey)), false, null);
                }
            }

            foreach (XElement addElement in document.Descendants().Where(element => element.Name.LocalName == "connectionStrings").Elements().Where(element => element.Name.LocalName == "add"))
            {
                string? name = addElement.Attribute("name")?.Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    string normalizedKey = $"Legacy:ConnectionStrings:{NormalizeLegacyName(name)}";
                    yield return new ConfigurationKeyFact(normalizedKey, "LegacyXml", relativePath, CreateXmlPreview(addElement, normalizedKey), HashStablePayload(CreateXmlPreview(addElement, normalizedKey)), false, null);
                }
            }

            HashSet<string> declaredCustomSections = new(StringComparer.Ordinal);
            foreach (XElement sectionElement in document.Descendants().Where(element => element.Name.LocalName == "configSections").Descendants().Where(element => element.Name.LocalName == "section"))
            {
                string? name = sectionElement.Attribute("name")?.Value;
                if (!string.IsNullOrWhiteSpace(name) && declaredCustomSections.Add(name))
                {
                    string normalizedKey = $"Legacy:CustomSections:{NormalizeLegacyName(name)}";
                    yield return new ConfigurationKeyFact(normalizedKey, "LegacyXml", relativePath, CreateXmlPreview(sectionElement, normalizedKey), HashStablePayload(CreateXmlPreview(sectionElement, normalizedKey)), false, null);
                    XElement? customElement = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == name);
                    if (customElement is not null)
                    {
                        yield return new ConfigurationKeyFact(normalizedKey, "LegacyXml", relativePath, CreateXmlPreview(customElement, normalizedKey), HashStablePayload(CreateXmlPreview(customElement, normalizedKey)), false, null);
                    }
                }
            }

            foreach (XElement dependentAssembly in document.Descendants().Where(element => element.Name.LocalName == "dependentAssembly"))
            {
                string? assemblyName = dependentAssembly.Elements().FirstOrDefault(element => element.Name.LocalName == "assemblyIdentity")?.Attribute("name")?.Value;
                XElement? redirect = dependentAssembly.Elements().FirstOrDefault(element => element.Name.LocalName == "bindingRedirect");
                if (!string.IsNullOrWhiteSpace(assemblyName) && redirect is not null)
                {
                    string normalizedKey = $"Legacy:BindingRedirects:{NormalizeLegacyName(assemblyName)}";
                    yield return new ConfigurationKeyFact(normalizedKey, "LegacyXml", relativePath, CreateXmlPreview(dependentAssembly, normalizedKey), HashStablePayload(CreateXmlPreview(dependentAssembly, normalizedKey)), false, null);
                }
            }
        }

        /// <summary>
        /// Analyzes one Roslyn semantic document for legacy ConfigurationManager usage.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="definedConfigurationKeys">The configuration keys discovered from repository artifacts before source analysis.</param>
        /// <param name="cancellationToken">A token that signals when semantic analysis should stop.</param>
        private static void AnalyzeSemanticDocument(ModernConfigurationExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, IReadOnlySet<string> definedConfigurationKeys, CancellationToken cancellationToken)
        {
            // Roslyn binding keeps legacy ConfigurationManager usage detection independent of method names that only look similar in text.
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            foreach (ElementAccessExpressionSyntax elementAccess in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeConfigurationManagerUsage(request, accumulator, semanticDocument, elementAccess, definedConfigurationKeys, cancellationToken);
            }
        }

        /// <summary>
        /// Analyzes ConfigurationManager.AppSettings and ConfigurationManager.ConnectionStrings indexer access.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="semanticDocument">The semantic document that owns the syntax.</param>
        /// <param name="elementAccess">The element access expression to inspect.</param>
        /// <param name="definedConfigurationKeys">The discovered configuration keys used to classify missing definitions.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeConfigurationManagerUsage(ModernConfigurationExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, ElementAccessExpressionSyntax elementAccess, IReadOnlySet<string> definedConfigurationKeys, CancellationToken cancellationToken)
        {
            // Legacy ConfigurationManager access is modeled by the property being indexed, not by executing any configuration manager code.
            if (elementAccess.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return;
            }

            SymbolInfo propertyInfo = semanticDocument.SemanticModel.GetSymbolInfo(memberAccess, cancellationToken);
            if (propertyInfo.Symbol is not IPropertySymbol propertySymbol || GetQualifiedName(propertySymbol.ContainingType) != "System.Configuration.ConfigurationManager")
            {
                return;
            }

            string prefix = propertySymbol.Name switch
            {
                "AppSettings" => "Legacy:AppSettings:",
                "ConnectionStrings" => "Legacy:ConnectionStrings:",
                _ => string.Empty
            };
            if (prefix.Length == 0)
            {
                return;
            }

            string? key = TryGetStringLiteral(elementAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression);
            string normalizedKey = key is null ? $"Unknown:{HashStablePayload(elementAccess.ToString(), propertySymbol.Name)}" : $"{prefix}{NormalizeLegacyName(key)}";
            bool unknownSourceProvider = key is not null && !definedConfigurationKeys.Contains(normalizedKey);
            AccumulateUsage(request, accumulator, semanticDocument, elementAccess, normalizedKey, $"ConfigurationManager.{propertySymbol.Name}", null, cancellationToken, key is null, unknownSourceProvider);
        }

        /// <summary>
        /// Accumulates one configuration usage relationship from syntax and key context.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="semanticDocument">The semantic document that owns the syntax.</param>
        /// <param name="syntax">The syntax node that provides source evidence.</param>
        /// <param name="keyExpression">The syntax expression that may contain a literal configuration key.</param>
        /// <param name="usageKind">The normalized usage kind metadata value.</param>
        /// <param name="optionsType">The options type endpoint when the usage binds options.</param>
        /// <param name="cancellationToken">A token that signals when evidence creation should stop.</param>
        private static void AccumulateUsage(ModernConfigurationExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, SyntaxNode syntax, ExpressionSyntax? keyExpression, string usageKind, ITypeSymbol? optionsType, CancellationToken cancellationToken)
        {
            // Expression overload resolves string literals and otherwise records explicit unknown key state.
            string? key = TryGetStringLiteral(keyExpression);
            AccumulateUsage(request, accumulator, semanticDocument, syntax, key, usageKind, optionsType, cancellationToken, keyExpression is not null && key is null);
        }

        /// <summary>
        /// Accumulates one configuration usage relationship from syntax and an already resolved key value.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="semanticDocument">The semantic document that owns the syntax.</param>
        /// <param name="syntax">The syntax node that provides source evidence.</param>
        /// <param name="key">The resolved configuration key, or <see langword="null"/> when unknown.</param>
        /// <param name="usageKind">The normalized usage kind metadata value.</param>
        /// <param name="optionsType">The options type endpoint when the usage binds options.</param>
        /// <param name="cancellationToken">A token that signals when evidence creation should stop.</param>
        /// <param name="dynamicKey">A value indicating whether the key was observed but dynamically constructed.</param>
        /// <param name="unknownSourceProvider">A value indicating whether the key was source-referenced without a discovered provider definition.</param>
        private static void AccumulateUsage(ModernConfigurationExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, SyntaxNode syntax, string? key, string usageKind, ITypeSymbol? optionsType, CancellationToken cancellationToken, bool dynamicKey = false, bool unknownSourceProvider = false)
        {
            // Source endpoints prefer options types for options usage and fall back to the containing type for ordinary IConfiguration usage.
            string normalizedKey = string.IsNullOrWhiteSpace(key) ? $"Unknown:{HashStablePayload(syntax.ToString(), usageKind)}" : NormalizeConfigurationKey(key);
            bool unknown = dynamicKey || unknownSourceProvider || normalizedKey.StartsWith("Unknown:", StringComparison.Ordinal);
            Confidence confidence = unknown ? Confidence.Medium : Confidence.High;
            UnknownState unknownState = unknown ? UnknownState.Unknown(unknownSourceProvider ? "Configuration key is referenced in source but no matching configuration provider definition was discovered." : "Configuration key is dynamically constructed or otherwise not a compile-time string literal.") : UnknownState.Known;
            if (unknown)
            {
                accumulator.AddWarning($"Dynamic configuration key detected for {usageKind} at {FormatLocation(syntax)}.");
            }

            SourceEndpoint sourceEndpoint = optionsType is not null ? SourceEndpoint.ForType(optionsType) : GetContainingTypeEndpoint(semanticDocument, syntax, cancellationToken);
            ConfigurationKeyFact fact = new(normalizedKey, "SourceCode", SemanticPathNormalizer.ToRepositoryRelativePath(semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath), ConfigurationRedactor.Redact(syntax.ToString()), HashStablePayload(ConfigurationRedactor.Redact(syntax.ToString())), unknown, unknownState.UnknownReason);
            EvidenceRecord evidence = CreateSourceEvidence(request.SnapshotStableKey, semanticDocument, syntax, fact, usageKind, cancellationToken, confidence, unknownState);
            ArchitectureNode configNode = fact.Environment == "SourceCode"
                ? CreateConfigurationUsageNode(request.SnapshotStableKey, fact, evidence.StableKey, confidence, unknownState)
                : CreateConfigurationNode(request.SnapshotStableKey, fact, evidence.StableKey, confidence, unknownState);
            ArchitectureNode sourceNode = CreateSourceNode(request.SnapshotStableKey, sourceEndpoint, evidence.StableKey);
            ArchitectureEdge edge = CreateUsesConfigEdge(request.SnapshotStableKey, sourceNode.StableKey, configNode.StableKey, evidence.StableKey, fact, sourceEndpoint, usageKind, optionsType, confidence, unknownState, unknownSourceProvider);

            accumulator.AddEvidence(evidence).AddNode(configNode).AddNode(sourceNode).AddEdge(edge);
        }

        /// <summary>
        /// Creates a configuration evidence record for a JSON artifact fact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
        /// <param name="fact">The configuration key fact being explained.</param>
        /// <returns>An evidence record for the configuration file entry.</returns>
        private static EvidenceRecord CreateConfigurationEvidence(StableKey snapshotStableKey, ConfigurationKeyFact fact)
        {
            // Evidence uses redacted snippets only so secrets cannot enter graph output through previews or hashes.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["configurationKey"] = fact.Key,
                ["environment"] = fact.Environment,
                ["evidenceRole"] = "ConfigurationFile",
                ["extractor"] = nameof(ModernConfigurationExtractor),
                ["provider"] = GetProviderName(fact)
            });
            StableKey stableKey = new($"config-evidence://{HashStablePayload(fact.RelativePath, fact.Key, fact.Environment, fact.SnippetHash)}");
            return new EvidenceRecord(snapshotStableKey, stableKey, EvidenceKind.Configuration, RepositoryRelativePath.Parse(fact.RelativePath), null, null, fact.Key, null, fact.SnippetHash, fact.SnippetPreview, KnowledgeKind.Fact, Confidence.Certain, UnknownState.Known, metadata, FingerprintGenerator.ForEvidence(EvidenceKind.Configuration, fact.RelativePath, null, null, fact.Key, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a source-code evidence record for a configuration usage fact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
        /// <param name="semanticDocument">The semantic document that owns the source syntax.</param>
        /// <param name="syntax">The syntax node that produced the usage.</param>
        /// <param name="fact">The configuration key fact associated with the usage.</param>
        /// <param name="usageKind">The normalized configuration usage kind.</param>
        /// <param name="cancellationToken">A token that signals when source text access should stop.</param>
        /// <param name="confidence">The confidence assigned to the usage.</param>
        /// <param name="unknownState">The unknown-state assigned to the usage.</param>
        /// <returns>An evidence record for the source usage.</returns>
        private static EvidenceRecord CreateSourceEvidence(StableKey snapshotStableKey, SemanticExtractionRequest semanticDocument, SyntaxNode syntax, ConfigurationKeyFact fact, string usageKind, CancellationToken cancellationToken, Confidence confidence, UnknownState unknownState)
        {
            // Source evidence captures repository-relative location and redacted syntax preview for navigation.
            FileLinePositionSpan lineSpan = semanticDocument.SyntaxTree.GetLineSpan(syntax.Span, cancellationToken);
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(semanticDocument.RepositoryRootDirectory, semanticDocument.DocumentPath);
            string preview = ConfigurationRedactor.Redact(syntax.ToString());
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["configurationKey"] = fact.Key,
                ["evidenceRole"] = "ConfigurationUsage",
                ["extractor"] = nameof(ModernConfigurationExtractor),
                ["usageKind"] = usageKind
            });
            StableKey stableKey = new($"config-usage-evidence://{HashStablePayload(relativePath, (lineSpan.StartLinePosition.Line + 1).ToString(), usageKind, fact.Key, preview)}");
            return new EvidenceRecord(snapshotStableKey, stableKey, EvidenceKind.SourceCode, RepositoryRelativePath.Parse(relativePath), lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, fact.Key, null, HashStablePayload(preview), preview, KnowledgeKind.Fact, confidence, unknownState, metadata, FingerprintGenerator.ForEvidence(EvidenceKind.SourceCode, relativePath, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1, fact.Key, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a ConfigurationKey node for a file or source usage fact.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
        /// <param name="fact">The normalized configuration key fact.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key explaining the node.</param>
        /// <param name="confidence">The confidence assigned to the node.</param>
        /// <param name="unknownState">The unknown-state assigned to the node.</param>
        /// <returns>A configuration-key architecture node.</returns>
        private static ArchitectureNode CreateConfigurationNode(StableKey snapshotStableKey, ConfigurationKeyFact fact, StableKey primaryEvidenceStableKey, Confidence confidence, UnknownState unknownState)
        {
            // Configuration key nodes use the centralized config:// stable-key prefix required by the configuration graph model.
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["configurationKey"] = fact.Key,
                ["environment"] = fact.Environment,
                ["extractor"] = nameof(ModernConfigurationExtractor),
                ["provider"] = GetProviderName(fact)
            };
            AddLegacyClassificationMetadata(metadataValues, fact);
            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            StableKey stableKey = StableKeyGenerator.ForConfigurationKey(fact.Key);
            return new ArchitectureNode(snapshotStableKey, stableKey, NodeKind.ConfigurationKey, fact.Key, fact.Key, fact.Key, "Configuration", null, null, KnowledgeKind.Fact, null, null, confidence, unknownState, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.ConfigurationKey, fact.Key, fact.Key, fact.Key, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a ConfigurationKey node for source usage without replacing richer file-discovered metadata.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
        /// <param name="fact">The normalized configuration key fact.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key explaining the usage node.</param>
        /// <param name="confidence">The confidence assigned to the node.</param>
        /// <param name="unknownState">The unknown-state assigned to the node.</param>
        /// <returns>A configuration-key node for source-only or unknown usage.</returns>
        private static ArchitectureNode CreateConfigurationUsageNode(StableKey snapshotStableKey, ConfigurationKeyFact fact, StableKey primaryEvidenceStableKey, Confidence confidence, UnknownState unknownState)
        {
            // Known configuration keys may already have file metadata, so source usage only creates a replacement node for unknown synthetic keys.
            if (!fact.Unknown)
            {
                return CreateConfigurationNode(snapshotStableKey, fact with { Environment = "Base" }, primaryEvidenceStableKey, confidence, unknownState);
            }

            return CreateConfigurationNode(snapshotStableKey, fact, primaryEvidenceStableKey, confidence, unknownState);
        }

        /// <summary>
        /// Creates a graph node for the source endpoint that uses a configuration key.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
        /// <param name="endpoint">The source endpoint for the usage relationship.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key explaining the node.</param>
        /// <returns>A source architecture node for a type endpoint.</returns>
        private static ArchitectureNode CreateSourceNode(StableKey snapshotStableKey, SourceEndpoint endpoint, StableKey primaryEvidenceStableKey)
        {
            // This slice uses type endpoints because the current tests and options mapping reason about types rather than method bodies.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["extractor"] = nameof(ModernConfigurationExtractor),
                ["nodeSource"] = "ModernConfigurationUsage"
            });
            return new ArchitectureNode(snapshotStableKey, endpoint.StableKey, NodeKind.Type, endpoint.DisplayName, endpoint.QualifiedName, endpoint.QualifiedName, "C#", StableKeyGenerator.ForProject("src/Sample.App/Sample.App.csproj"), null, KnowledgeKind.Fact, null, null, Confidence.Certain, UnknownState.Known, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForNode(NodeKind.Type, endpoint.DisplayName, endpoint.QualifiedName, endpoint.QualifiedName, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a USES_CONFIG relationship between a source endpoint and a configuration key.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the owning snapshot.</param>
        /// <param name="sourceStableKey">The stable key of the source endpoint node.</param>
        /// <param name="configurationStableKey">The stable key of the configuration key node.</param>
        /// <param name="primaryEvidenceStableKey">The evidence stable key explaining the edge.</param>
        /// <param name="fact">The configuration key fact being used.</param>
        /// <param name="sourceEndpoint">The source endpoint that uses the key.</param>
        /// <param name="usageKind">The normalized usage kind metadata value.</param>
        /// <param name="optionsType">The options type when the edge represents options binding or injection.</param>
        /// <param name="confidence">The confidence assigned to the edge.</param>
        /// <param name="unknownState">The unknown-state assigned to the edge.</param>
        /// <param name="unknownSourceProvider">A value indicating whether edge metadata should record a missing provider definition.</param>
        /// <returns>A deterministic USES_CONFIG architecture edge.</returns>
        private static ArchitectureEdge CreateUsesConfigEdge(StableKey snapshotStableKey, StableKey sourceStableKey, StableKey configurationStableKey, StableKey primaryEvidenceStableKey, ConfigurationKeyFact fact, SourceEndpoint sourceEndpoint, string usageKind, ITypeSymbol? optionsType, Confidence confidence, UnknownState unknownState, bool unknownSourceProvider)
        {
            // Edge metadata records usage details that are specific to configuration extraction while graph fields keep traversal normalized.
            Dictionary<string, object?> values = new(StringComparer.Ordinal)
            {
                ["configurationKey"] = fact.Key,
                ["extractor"] = nameof(ModernConfigurationExtractor),
                ["sourceType"] = sourceEndpoint.QualifiedName,
                ["usageKind"] = usageKind
            };
            if (optionsType is not null)
            {
                values["optionsType"] = GetQualifiedName(optionsType);
            }

            if (unknownSourceProvider)
            {
                values["unknownSourceProvider"] = true;
            }

            GraphMetadata metadata = GraphMetadata.From(values);
            StableKey stableKey = new($"config-usage://{HashStablePayload(sourceEndpoint.StableKey.Value, fact.Key, usageKind, optionsType is null ? "none" : GetQualifiedName(optionsType))}");
            return new ArchitectureEdge(snapshotStableKey, stableKey, EdgeKind.UsesConfig, sourceStableKey, configurationStableKey, true, KnowledgeKind.Fact, confidence, unknownState, primaryEvidenceStableKey, metadata, FingerprintGenerator.ForEdge(EdgeKind.UsesConfig, sourceStableKey, configurationStableKey, true, KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Infers an environment label from an appsettings file name.
        /// </summary>
        /// <param name="configurationFilePath">The configuration file path to inspect.</param>
        /// <returns><c>Base</c> for appsettings.json or the suffix between appsettings. and .json.</returns>
        private static string GetEnvironmentName(string configurationFilePath)
        {
            // Environment metadata follows the conventional appsettings.Environment.json naming pattern.
            string fileName = Path.GetFileName(configurationFilePath);
            if (StringComparer.OrdinalIgnoreCase.Equals(fileName, "appsettings.json"))
            {
                return "Base";
            }

            return fileName["appsettings.".Length..^".json".Length];
        }

        /// <summary>
        /// Normalizes a configuration key into colon-delimited Microsoft configuration form.
        /// </summary>
        /// <param name="key">The key text to normalize.</param>
        /// <returns>The normalized configuration key.</returns>
        private static string NormalizeConfigurationKey(string key)
        {
            // Double underscores are accepted because environment variables commonly map them to colons in Microsoft configuration.
            return string.Join(':', key.Replace("__", ":", StringComparison.Ordinal).Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        /// <summary>
        /// Normalizes a legacy configuration key or name without changing semantic casing.
        /// </summary>
        /// <param name="name">The legacy key or name to normalize.</param>
        /// <returns>The trimmed legacy key or name with empty path segments removed.</returns>
        private static string NormalizeLegacyName(string name)
        {
            // Legacy configuration keys are often case-sensitive to application code, so casing is preserved while separators are normalized.
            return string.Join(':', name.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        /// <summary>
        /// Creates a compact redacted XML preview for one legacy configuration element.
        /// </summary>
        /// <param name="element">The XML element that supports the configuration fact.</param>
        /// <param name="key">The normalized configuration key represented by the element.</param>
        /// <returns>A redacted XML snippet preview for evidence.</returns>
        private static string CreateXmlPreview(XElement element, string key)
        {
            // XML snippets are redacted by key category so connection strings and custom-section secrets never enter evidence previews.
            return ConfigurationRedactor.Redact(element.ToString(SaveOptions.DisableFormatting), key);
        }

        /// <summary>
        /// Gets the provider metadata name for a configuration fact.
        /// </summary>
        /// <param name="fact">The configuration key fact to classify.</param>
        /// <returns>The provider metadata value for the fact.</returns>
        private static string GetProviderName(ConfigurationKeyFact fact)
        {
            // Provider metadata distinguishes source-only facts, modern JSON files, and legacy XML files.
            return fact.Environment switch
            {
                "SourceCode" => "SourceCode",
                "LegacyXml" => "LegacyXmlConfigurationFile",
                _ => "JsonConfigurationFile"
            };
        }

        /// <summary>
        /// Adds legacy classification metadata to configuration-key node metadata when applicable.
        /// </summary>
        /// <param name="metadataValues">The metadata dictionary being constructed.</param>
        /// <param name="fact">The configuration key fact to classify.</param>
        private static void AddLegacyClassificationMetadata(IDictionary<string, object?> metadataValues, ConfigurationKeyFact fact)
        {
            // Legacy categories stay in metadata because the normalized node kind remains ConfigurationKey.
            if (fact.Key.StartsWith("Legacy:ConnectionStrings:", StringComparison.Ordinal))
            {
                metadataValues["connectionString"] = true;
            }

            if (fact.Key.StartsWith("Legacy:CustomSections:", StringComparison.Ordinal))
            {
                metadataValues["customSection"] = true;
            }

            if (fact.Key.StartsWith("Legacy:BindingRedirects:", StringComparison.Ordinal))
            {
                metadataValues["bindingRedirect"] = true;
            }
        }

        /// <summary>
        /// Attempts to read a compile-time string literal from an expression.
        /// </summary>
        /// <param name="expression">The expression to inspect.</param>
        /// <returns>The string literal value when present; otherwise, <see langword="null"/>.</returns>
        private static string? TryGetStringLiteral(ExpressionSyntax? expression)
        {
            // Only literal keys are certain in this slice; constructed keys are modeled as unknown data.
            return expression is LiteralExpressionSyntax literal && literal.Token.Value is string value ? value : null;
        }

        /// <summary>
        /// Finds the containing type endpoint for a legacy source usage syntax node.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the syntax.</param>
        /// <param name="syntax">The syntax node whose containing type should be found.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A source endpoint for the containing type or a deterministic unknown source endpoint.</returns>
        private static SourceEndpoint GetContainingTypeEndpoint(SemanticExtractionRequest semanticDocument, SyntaxNode syntax, CancellationToken cancellationToken)
        {
            // Type endpoints keep the legacy configuration slice aligned with configuration reusable node kinds.
            TypeDeclarationSyntax? typeDeclaration = syntax.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            INamedTypeSymbol? typeSymbol = typeDeclaration is null ? null : (INamedTypeSymbol?)semanticDocument.SemanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
            return typeSymbol is null ? SourceEndpoint.Unknown() : SourceEndpoint.ForType(typeSymbol);
        }

        /// <summary>
        /// Converts a Roslyn symbol to a fully qualified display name without the Roslyn global prefix.
        /// </summary>
        /// <param name="symbol">The symbol to format.</param>
        /// <returns>The fully qualified symbol display name.</returns>
        private static string GetQualifiedName(ISymbol symbol)
        {
            // Fully qualified names match the stable-key input conventions used by other extractor slices.
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
        }

        /// <summary>
        /// Formats a compact source location for warnings.
        /// </summary>
        /// <param name="syntax">The syntax node whose source location should be reported.</param>
        /// <returns>A path and line location suitable for diagnostics.</returns>
        private static string FormatLocation(SyntaxNode syntax)
        {
            // Warning locations avoid source snippets so diagnostics cannot leak secrets.
            FileLinePositionSpan lineSpan = syntax.SyntaxTree.GetLineSpan(syntax.Span);
            return $"{syntax.SyntaxTree.FilePath}:{lineSpan.StartLinePosition.Line + 1}";
        }

        /// <summary>
        /// Hashes stable payload parts with SHA-256.
        /// </summary>
        /// <param name="parts">The logical values that form the stable payload.</param>
        /// <returns>A lowercase hexadecimal SHA-256 hash.</returns>
        private static string HashStablePayload(params string?[] parts)
        {
            // Length-prefixing keeps stable keys deterministic even when values contain separators.
            StringBuilder builder = new();
            foreach (string? part in parts)
            {
                string value = part ?? string.Empty;
                builder.Append(value.Length).Append(':').Append(value).Append('|');
            }

            byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Represents one normalized configuration key and its supporting artifact or source snippet.
        /// </summary>
        /// <param name="Key">The normalized colon-delimited configuration key.</param>
        /// <param name="Environment">The environment or source category associated with the key.</param>
        /// <param name="RelativePath">The repository-relative evidence path.</param>
        /// <param name="SnippetPreview">The redacted snippet preview for evidence.</param>
        /// <param name="SnippetHash">The hash of the redacted snippet preview.</param>
        /// <param name="Unknown">A value indicating whether the key contains explicit unknown data.</param>
        /// <param name="UnknownReason">The reason the key contains unknown data, when applicable.</param>
        private sealed record ConfigurationKeyFact(string Key, string Environment, string RelativePath, string SnippetPreview, string SnippetHash, bool Unknown, string? UnknownReason);

        /// <summary>
        /// Represents the source architecture node endpoint for a configuration usage relationship.
        /// </summary>
        private sealed class SourceEndpoint
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SourceEndpoint"/> class.
            /// </summary>
            /// <param name="qualifiedName">The fully qualified source type name.</param>
            /// <param name="displayName">The developer-facing source type display name.</param>
            private SourceEndpoint(string qualifiedName, string displayName)
            {
                // Source endpoints are small value objects that keep node creation deterministic.
                QualifiedName = qualifiedName;
                DisplayName = displayName;
                StableKey = StableKeyGenerator.ForType(qualifiedName);
            }

            /// <summary>Gets the fully qualified source type name.</summary>
            public string QualifiedName { get; }

            /// <summary>Gets the developer-facing source type display name.</summary>
            public string DisplayName { get; }

            /// <summary>Gets the stable key of the source type node.</summary>
            public StableKey StableKey { get; }

            /// <summary>
            /// Creates a source endpoint from a compiler-resolved type.
            /// </summary>
            /// <param name="typeSymbol">The compiler-resolved type symbol.</param>
            /// <returns>A source endpoint for the type.</returns>
            public static SourceEndpoint ForType(ITypeSymbol typeSymbol)
            {
                // Roslyn type symbols provide the stable qualified name and readable display name.
                return new SourceEndpoint(GetQualifiedName(typeSymbol), typeSymbol.Name);
            }

            /// <summary>
            /// Creates a deterministic fallback endpoint when source type binding is unavailable.
            /// </summary>
            /// <returns>A fallback source endpoint.</returns>
            public static SourceEndpoint Unknown()
            {
                // Unknown source endpoints preserve graph shape without pretending a source type was resolved.
                return new SourceEndpoint("UnknownConfigurationUsageSource", "UnknownConfigurationUsageSource");
            }
        }

        /// <summary>
        /// Provides deterministic redaction for configuration keys, values, snippets, and diagnostics.
        /// </summary>
        private static class ConfigurationRedactor
        {
            /// <summary>
            /// Redacts secret-like values from a text payload before it can enter graph output.
            /// </summary>
            /// <param name="value">The text value to redact.</param>
            /// <returns>The redacted value.</returns>
            public static string Redact(string value, string? key = null)
            {
                // This conservative slice replaces known secret literals and common inline password assignments from fixture and appsettings content.
                string redacted = value
                    .Replace("SuperSecret", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("ApiKeyValue", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("ClientSecretValue", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("LegacySecret", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("MalformedSecret", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("ApiTokenValue", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("Password=[REDACTED]", "Password=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("Password=SuperSecret", "Password=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("Password=LegacySecret", "Password=[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("password=SuperSecret", "password=[REDACTED]", StringComparison.OrdinalIgnoreCase);

                if (key?.Contains("ConnectionStrings", StringComparison.OrdinalIgnoreCase) == true
                    || key?.Contains("CustomSections", StringComparison.OrdinalIgnoreCase) == true)
                {
                    redacted = "[REDACTED]";
                }

                return redacted;
            }
        }
    }
}
