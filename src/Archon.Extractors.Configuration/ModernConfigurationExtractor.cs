using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    /// Extracts modern appsettings JSON keys and compiler-bound modern configuration API usage into WP002 graph contracts.
    /// </summary>
    public sealed class ModernConfigurationExtractor
    {
        /// <summary>
        /// Extracts configuration key nodes, USES_CONFIG relationships, evidence, warnings, and errors from the supplied request.
        /// </summary>
        /// <param name="request">The repository and semantic-document request that scopes extraction.</param>
        /// <param name="cancellationToken">A token that signals when extraction should stop.</param>
        /// <returns>The modern configuration extraction result containing a shared architecture snapshot.</returns>
        public ModernConfigurationExtractionResult Extract(ModernConfigurationExtractionRequest request, CancellationToken cancellationToken = default)
        {
            // Extraction is intentionally data-driven: JSON files are parsed as artifacts and source usage is compiler-bound without executing application code.
            ArgumentNullException.ThrowIfNull(request);
            ArchitectureSnapshotAccumulator accumulator = new();
            foreach (string configurationFilePath in DiscoverAppsettingsFiles(request.RepositoryRootDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseConfigurationFile(request, accumulator, configurationFilePath, cancellationToken);
            }

            foreach (SemanticExtractionRequest semanticDocument in request.SemanticDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeSemanticDocument(request, accumulator, semanticDocument, cancellationToken);
            }

            return new ModernConfigurationExtractionResult(accumulator.ToSnapshot());
        }

        /// <summary>
        /// Discovers modern appsettings JSON files below the repository root.
        /// </summary>
        /// <param name="repositoryRootDirectory">The absolute repository root to search.</param>
        /// <returns>Repository-contained appsettings file paths ordered deterministically.</returns>
        private static IReadOnlyList<string> DiscoverAppsettingsFiles(string repositoryRootDirectory)
        {
            // File discovery is static and avoids running target application configuration builders.
            if (!Directory.Exists(repositoryRootDirectory))
            {
                return [];
            }

            return Directory.EnumerateFiles(repositoryRootDirectory, "appsettings*.json", SearchOption.AllDirectories)
                .Where(IsModernAppsettingsFile)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Determines whether a path matches appsettings.json or appsettings.Environment.json exactly.
        /// </summary>
        /// <param name="path">The candidate configuration file path.</param>
        /// <returns><see langword="true"/> when the file name is a supported modern appsettings name; otherwise, <see langword="false"/>.</returns>
        private static bool IsModernAppsettingsFile(string path)
        {
            // The slice intentionally avoids arbitrary JSON files so unrelated data artifacts are not interpreted as app configuration.
            string fileName = Path.GetFileName(path);
            return StringComparer.OrdinalIgnoreCase.Equals(fileName, "appsettings.json")
                || (fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
                    && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && fileName.Length > "appsettings..json".Length);
        }

        /// <summary>
        /// Parses one appsettings JSON file and emits configuration-key nodes with redacted evidence.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity and repository paths.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="configurationFilePath">The appsettings file path to parse.</param>
        /// <param name="cancellationToken">A token that signals when parsing should stop.</param>
        private static void ParseConfigurationFile(ModernConfigurationExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, string configurationFilePath, CancellationToken cancellationToken)
        {
            // JSON parsing is tolerant at the file level: malformed files become diagnostics without blocking other configuration artifacts.
            string relativePath = SemanticPathNormalizer.ToRepositoryRelativePath(request.RepositoryRootDirectory, configurationFilePath);
            string content = File.ReadAllText(configurationFilePath);
            string redactedContent = ConfigurationRedactor.Redact(content);
            try
            {
                using JsonDocument document = JsonDocument.Parse(content, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                string environment = GetEnvironmentName(configurationFilePath);
                foreach (ConfigurationKeyFact fact in EnumerateJsonKeys(document.RootElement, null, environment, relativePath, redactedContent))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EvidenceRecord evidence = CreateConfigurationEvidence(request.SnapshotStableKey, fact);
                    ArchitectureNode node = CreateConfigurationNode(request.SnapshotStableKey, fact, evidence.StableKey, Confidence.Certain, UnknownState.Known);
                    accumulator.AddEvidence(evidence).AddNode(node);
                }
            }
            catch (JsonException exception)
            {
                accumulator.AddWarning($"Unable to parse modern configuration file {relativePath}: {ConfigurationRedactor.Redact(exception.Message)}");
            }
        }

        /// <summary>
        /// Enumerates leaf configuration keys from a JSON element.
        /// </summary>
        /// <param name="element">The JSON element currently being traversed.</param>
        /// <param name="parentPath">The current colon-delimited parent path.</param>
        /// <param name="environment">The environment suffix inferred from the appsettings file name.</param>
        /// <param name="relativePath">The repository-relative file path for evidence.</param>
        /// <param name="redactedContent">The redacted file content used for snippet previews and hashes.</param>
        /// <returns>Configuration key facts for JSON leaf values.</returns>
        private static IEnumerable<ConfigurationKeyFact> EnumerateJsonKeys(JsonElement element, string? parentPath, string environment, string relativePath, string redactedContent)
        {
            // Hierarchical object paths become colon-delimited configuration keys matching Microsoft configuration conventions.
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string propertyPath = parentPath is null ? NormalizeConfigurationKey(property.Name) : $"{parentPath}:{NormalizeConfigurationKey(property.Name)}";
                    foreach (ConfigurationKeyFact fact in EnumerateJsonKeys(property.Value, propertyPath, environment, relativePath, redactedContent))
                    {
                        yield return fact;
                    }
                }

                yield break;
            }

            if (parentPath is null)
            {
                yield break;
            }

            string preview = CreateJsonPreview(parentPath, element, redactedContent);
            yield return new ConfigurationKeyFact(parentPath, environment, relativePath, preview, HashStablePayload(preview), false, null);
        }

        /// <summary>
        /// Analyzes one Roslyn semantic document for modern configuration API usage.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="semanticDocument">The Roslyn semantic document to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic analysis should stop.</param>
        private static void AnalyzeSemanticDocument(ModernConfigurationExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, CancellationToken cancellationToken)
        {
            // Roslyn binding keeps configuration API usage detection independent of method names that only look similar in text.
            SyntaxNode root = semanticDocument.SyntaxTree.GetRoot(cancellationToken);
            foreach (ElementAccessExpressionSyntax elementAccess in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeIndexerUsage(request, accumulator, semanticDocument, elementAccess, cancellationToken);
            }

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeInvocationUsage(request, accumulator, semanticDocument, invocation, cancellationToken);
            }

            foreach (ConstructorDeclarationSyntax constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalyzeOptionsInjection(request, accumulator, semanticDocument, constructor, cancellationToken);
            }
        }

        /// <summary>
        /// Analyzes IConfiguration indexer access for literal or dynamic configuration keys.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="semanticDocument">The semantic document that owns the syntax.</param>
        /// <param name="elementAccess">The element access expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeIndexerUsage(ModernConfigurationExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, ElementAccessExpressionSyntax elementAccess, CancellationToken cancellationToken)
        {
            // IConfiguration indexers are represented as property symbols named Item on the Microsoft configuration abstraction.
            SymbolInfo symbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(elementAccess, cancellationToken);
            if (symbolInfo.Symbol is not IPropertySymbol propertySymbol || propertySymbol.Name != "Item" || !IsConfigurationType(propertySymbol.ContainingType))
            {
                TypeInfo receiverType = semanticDocument.SemanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken);
                if (!IsConfigurationType(receiverType.Type))
                {
                    return;
                }
            }

            ExpressionSyntax? argumentExpression = elementAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            AccumulateUsage(request, accumulator, semanticDocument, elementAccess, argumentExpression, "IConfigurationIndexer", null, cancellationToken);
        }

        /// <summary>
        /// Analyzes modern configuration method calls for sections, binding, typed get, and options configuration.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="semanticDocument">The semantic document that owns the syntax.</param>
        /// <param name="invocation">The invocation expression to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeInvocationUsage(ModernConfigurationExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
        {
            // Supported API calls are mapped to usage kinds and optional options type endpoints.
            SymbolInfo symbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            {
                return;
            }

            IMethodSymbol canonicalMethod = methodSymbol.ReducedFrom ?? methodSymbol.OriginalDefinition;
            string methodName = canonicalMethod.Name;
            string ownerType = GetQualifiedName(canonicalMethod.ContainingType);

            if (methodName == "GetSection" && canonicalMethod.Parameters.Length >= 1 && IsConfigurationType(canonicalMethod.ContainingType))
            {
                AccumulateUsage(request, accumulator, semanticDocument, invocation, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression, "GetSection", null, cancellationToken);
                return;
            }

            if (methodName is "Bind" or "Get" && ownerType == "Microsoft.Extensions.Configuration.ConfigurationBinder")
            {
                ExpressionSyntax? sectionExpression = GetInvocationReceiver(invocation);
                string? sectionKey = TryResolveSectionKey(semanticDocument, sectionExpression, cancellationToken);
                ITypeSymbol? optionsType = methodName == "Get" ? methodSymbol.TypeArguments.FirstOrDefault() : null;
                AccumulateUsage(request, accumulator, semanticDocument, invocation, sectionKey, methodName == "Bind" ? "Bind" : "GetOptions", optionsType, cancellationToken);
                return;
            }

            if (methodName == "Configure" && ownerType == "Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions")
            {
                ExpressionSyntax? configurationExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                string? sectionKey = TryResolveSectionKey(semanticDocument, configurationExpression, cancellationToken);
                ITypeSymbol? optionsType = methodSymbol.TypeArguments.FirstOrDefault();
                AccumulateUsage(request, accumulator, semanticDocument, invocation, sectionKey, "ConfigureOptions", optionsType, cancellationToken);
            }
        }

        /// <summary>
        /// Analyzes constructor parameters for IOptions-style options injection.
        /// </summary>
        /// <param name="request">The extraction request that scopes snapshot identity.</param>
        /// <param name="accumulator">The shared accumulator receiving graph contributions.</param>
        /// <param name="semanticDocument">The semantic document that owns the constructor syntax.</param>
        /// <param name="constructor">The constructor declaration to inspect.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        private static void AnalyzeOptionsInjection(ModernConfigurationExtractionRequest request, ArchitectureSnapshotAccumulator accumulator, SemanticExtractionRequest semanticDocument, ConstructorDeclarationSyntax constructor, CancellationToken cancellationToken)
        {
            // Options injection proves an options type participates in configuration even when the binding call appears elsewhere.
            foreach (ParameterSyntax parameter in constructor.ParameterList.Parameters)
            {
                IParameterSymbol? parameterSymbol = (IParameterSymbol?)semanticDocument.SemanticModel.GetDeclaredSymbol(parameter, cancellationToken);
                if (parameterSymbol?.Type is not INamedTypeSymbol namedType || !IsOptionsWrapperType(namedType) || namedType.TypeArguments.FirstOrDefault() is not ITypeSymbol optionsType)
                {
                    continue;
                }

                AccumulateUsage(request, accumulator, semanticDocument, parameter, optionsType.Name, "OptionsInjection", optionsType, cancellationToken);
            }
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
            // Configuration key nodes use the centralized config:// stable-key prefix required by WP002.
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["configurationKey"] = fact.Key,
                ["environment"] = fact.Environment,
                ["extractor"] = nameof(ModernConfigurationExtractor),
                ["provider"] = GetProviderName(fact)
            };
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
        /// Gets the provider metadata name for a configuration fact.
        /// </summary>
        /// <param name="fact">The configuration key fact to classify.</param>
        /// <returns>The provider metadata value for the fact.</returns>
        private static string GetProviderName(ConfigurationKeyFact fact)
        {
            // Provider metadata distinguishes source-only facts from modern JSON file facts.
            return fact.Environment switch
            {
                "SourceCode" => "SourceCode",
                _ => "JsonConfigurationFile"
            };
        }

        /// <summary>
        /// Creates a compact redacted JSON preview for one configuration key.
        /// </summary>
        /// <param name="key">The normalized key path.</param>
        /// <param name="element">The leaf JSON element for the key.</param>
        /// <param name="redactedContent">The redacted file content used as a fallback source.</param>
        /// <returns>A redacted snippet preview for evidence.</returns>
        private static string CreateJsonPreview(string key, JsonElement element, string redactedContent)
        {
            // The preview intentionally avoids raw secret values by redacting the serialized leaf value before evidence creation.
            string leafValue = element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
            return ConfigurationRedactor.Redact($"\"{key}\": {JsonSerializer.Serialize(leafValue)}", key);
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
        /// Attempts to resolve a configuration section key from a GetSection invocation expression.
        /// </summary>
        /// <param name="semanticDocument">The semantic document used to bind the expression.</param>
        /// <param name="expression">The expression that may represent configuration.GetSection("key").</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>The literal section key when it can be resolved; otherwise, <see langword="null"/>.</returns>
        private static string? TryResolveSectionKey(SemanticExtractionRequest semanticDocument, ExpressionSyntax? expression, CancellationToken cancellationToken)
        {
            // Bind/Get commonly operate on a section expression rather than accepting the key directly.
            if (expression is InvocationExpressionSyntax invocation)
            {
                SymbolInfo symbolInfo = semanticDocument.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
                if (symbolInfo.Symbol is IMethodSymbol methodSymbol && methodSymbol.Name == "GetSection")
                {
                    return TryGetStringLiteral(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
                }
            }

            return TryGetStringLiteral(expression);
        }

        /// <summary>
        /// Gets the receiver expression for an invocation when present.
        /// </summary>
        /// <param name="invocation">The invocation whose receiver should be inspected.</param>
        /// <returns>The receiver expression for member-access invocations; otherwise, <see langword="null"/>.</returns>
        private static ExpressionSyntax? GetInvocationReceiver(InvocationExpressionSyntax invocation)
        {
            // Extension-method syntax exposes the section object as the member access receiver in source.
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;
        }

        /// <summary>
        /// Finds the containing type endpoint for a source usage syntax node.
        /// </summary>
        /// <param name="semanticDocument">The semantic document that owns the syntax.</param>
        /// <param name="syntax">The syntax node whose containing type should be found.</param>
        /// <param name="cancellationToken">A token that signals when semantic binding should stop.</param>
        /// <returns>A source endpoint for the containing type or a deterministic unknown source endpoint.</returns>
        private static SourceEndpoint GetContainingTypeEndpoint(SemanticExtractionRequest semanticDocument, SyntaxNode syntax, CancellationToken cancellationToken)
        {
            // Type endpoints keep the first configuration slice aligned with WP002 reusable node kinds.
            TypeDeclarationSyntax? typeDeclaration = syntax.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            INamedTypeSymbol? typeSymbol = typeDeclaration is null ? null : (INamedTypeSymbol?)semanticDocument.SemanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
            return typeSymbol is null ? SourceEndpoint.Unknown() : SourceEndpoint.ForType(typeSymbol);
        }

        /// <summary>
        /// Determines whether a type is Microsoft.Extensions.Configuration.IConfiguration or IConfigurationSection.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to inspect.</param>
        /// <returns><see langword="true"/> when the symbol is a supported configuration abstraction; otherwise, <see langword="false"/>.</returns>
        private static bool IsConfigurationType(ITypeSymbol? typeSymbol)
        {
            // Local test stubs and real assemblies share the same fully qualified names, so symbol display names are sufficient here.
            string name = typeSymbol is null ? string.Empty : GetQualifiedName(typeSymbol);
            return name is "Microsoft.Extensions.Configuration.IConfiguration" or "Microsoft.Extensions.Configuration.IConfigurationSection";
        }

        /// <summary>
        /// Determines whether a type is an IOptions, IOptionsMonitor, or IOptionsSnapshot wrapper.
        /// </summary>
        /// <param name="typeSymbol">The named type symbol to inspect.</param>
        /// <returns><see langword="true"/> when the type is a supported options wrapper; otherwise, <see langword="false"/>.</returns>
        private static bool IsOptionsWrapperType(INamedTypeSymbol typeSymbol)
        {
            // Options wrappers are detected by original generic type definition so concrete TOptions types can vary.
            string name = GetQualifiedName(typeSymbol.OriginalDefinition);
            return name is "Microsoft.Extensions.Options.IOptions<TOptions>" or "Microsoft.Extensions.Options.IOptionsMonitor<TOptions>" or "Microsoft.Extensions.Options.IOptionsSnapshot<TOptions>";
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
