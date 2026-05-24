using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Loads and validates WP012 JSON rule catalog files from copied runtime output content.
    /// </summary>
    public sealed partial class RuleCatalogLoader
    {
        /// <summary>
        /// Defines the accepted semantic version shape for authored rule versions.
        /// </summary>
        private static readonly Regex s_semanticVersionPattern = CreateSemanticVersionPattern();

        /// <summary>
        /// Stores runtime folder options for rule loading.
        /// </summary>
        private readonly RuleCatalogOptions _options;

        /// <summary>
        /// Logs credential-safe loader events, validation failures, and duplicate detection.
        /// </summary>
        private readonly ILogger<RuleCatalogLoader> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogLoader"/> class.
        /// </summary>
        /// <param name="options">The options that identify the runtime rule folder.</param>
        public RuleCatalogLoader(RuleCatalogOptions options)
            : this(options, NullLogger<RuleCatalogLoader>.Instance)
        {
            // This overload keeps tests and simple application use cases concise while still allowing hosts to pass a real logger.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogLoader"/> class.
        /// </summary>
        /// <param name="options">The options that identify the runtime rule folder.</param>
        /// <param name="logger">The logger used for credential-safe loader events and diagnostics.</param>
        public RuleCatalogLoader(RuleCatalogOptions options, ILogger<RuleCatalogLoader> logger)
        {
            // Constructor injection keeps the loader independent from host service locators and easy to exercise in targeted tests.
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Loads rule JSON files from the configured runtime folder and returns validated entries plus diagnostics.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token for asynchronous file reads.</param>
        /// <returns>The complete catalog loading result.</returns>
        public async Task<RuleCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            // The loader reads copied output content only; it never walks back to repository-root source paths.
            string rulesDirectory = _options.RulesDirectory;
            if (!Directory.Exists(rulesDirectory))
            {
                _logger.LogError("Rule catalog folder {RulesDirectory} was not found.", rulesDirectory);
                return new RuleCatalogLoadResult([], [new RuleCatalogDiagnostic(
                    RuleCatalogDiagnosticCodes.RuleFolderMissing,
                    $"Rule catalog folder '{rulesDirectory}' was not found. Ensure ./rules content is copied to runtime output.")]);
            }

            IReadOnlyList<string> filePaths;
            try
            {
                // Deterministic file ordering makes diagnostics and duplicate selection reproducible across operating systems.
                filePaths = Directory.EnumerateFiles(rulesDirectory, "*.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(static filePath => filePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(exception, "Rule catalog folder {RulesDirectory} could not be enumerated.", rulesDirectory);
                return new RuleCatalogLoadResult([], [new RuleCatalogDiagnostic(
                    RuleCatalogDiagnosticCodes.RuleFolderUnreadable,
                    $"Rule catalog folder '{rulesDirectory}' could not be read: {exception.Message}")]);
            }

            List<RuleCatalogEntry> rules = [];
            List<RuleCatalogDiagnostic> diagnostics = [];
            foreach (string filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RuleFileValidationResult fileResult = await LoadFileAsync(filePath, cancellationToken).ConfigureAwait(false);
                diagnostics.AddRange(fileResult.Diagnostics);
                if (fileResult.Rule is not null)
                {
                    rules.Add(fileResult.Rule);
                }
            }

            AddDuplicateDiagnostics(rules, diagnostics);
            if (diagnostics.Count > 0)
            {
                _logger.LogError("Rule catalog validation found {DiagnosticCount} diagnostics in {RuleFileCount} files.", diagnostics.Count, filePaths.Count);
            }
            else
            {
                _logger.LogInformation("Rule catalog loaded {RuleCount} rules from {RuleDirectory}.", rules.Count, rulesDirectory);
            }

            return new RuleCatalogLoadResult(diagnostics.Count == 0 ? rules : [], diagnostics);
        }

        /// <summary>
        /// Loads the runtime catalog and throws a visible exception when validation fails.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token for asynchronous file reads.</param>
        /// <returns>The validated rule catalog entries.</returns>
        public async Task<IReadOnlyList<RuleCatalogEntry>> EnsureValidCatalogAsync(CancellationToken cancellationToken)
        {
            // Hosts and extraction initialization can use this fail-fast path so invalid built-in rules cannot be ignored silently.
            RuleCatalogLoadResult result = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!result.IsValid)
            {
                throw new RuleCatalogValidationException(result.Diagnostics);
            }

            return result.Rules;
        }

        /// <summary>
        /// Loads and validates one JSON rule file.
        /// </summary>
        /// <param name="filePath">The rule file path to read.</param>
        /// <param name="cancellationToken">The cancellation token for the file read.</param>
        /// <returns>The loaded rule when valid, plus file-level diagnostics.</returns>
        private async Task<RuleFileValidationResult> LoadFileAsync(string filePath, CancellationToken cancellationToken)
        {
            // Each file is isolated so parse or validation failures do not prevent other files from producing diagnostics.
            string json;
            try
            {
                json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(exception, "Rule file {RuleFilePath} could not be read.", filePath);
                return new RuleFileValidationResult(null, [new RuleCatalogDiagnostic(
                    RuleCatalogDiagnosticCodes.RuleFolderUnreadable,
                    $"Rule file '{filePath}' could not be read: {exception.Message}",
                    filePath)]);
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
                return ValidateRuleDocument(document.RootElement, json, filePath);
            }
            catch (JsonException exception)
            {
                long? lineNumber = exception.LineNumber.HasValue ? exception.LineNumber.Value + 1 : null;
                RuleCatalogDiagnostic diagnostic = new(
                    RuleCatalogDiagnosticCodes.JsonParseFailed,
                    $"Rule JSON parse failed at line {lineNumber?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, byte {exception.BytePositionInLine?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}: {exception.Message}",
                    filePath,
                    exception.Path,
                    lineNumber,
                    exception.BytePositionInLine);
                _logger.LogError("Rule file {RuleFilePath} failed JSON parsing with diagnostic {DiagnosticCode}.", filePath, diagnostic.Code);
                return new RuleFileValidationResult(null, [diagnostic]);
            }
        }

        /// <summary>
        /// Performs schema-equivalent and semantic validation for one parsed rule document.
        /// </summary>
        /// <param name="root">The parsed root JSON element.</param>
        /// <param name="definitionJson">The original JSON content for the rule.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <returns>The validated rule when successful, plus any diagnostics.</returns>
        private static RuleFileValidationResult ValidateRuleDocument(JsonElement root, string definitionJson, string filePath)
        {
            // Validation aggregates independent errors so authors get actionable feedback in one run.
            List<RuleCatalogDiagnostic> diagnostics = [];
            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.RequiredFieldMissing, "Rule file root must be a JSON object.", filePath, "$"));
                return new RuleFileValidationResult(null, diagnostics);
            }

            string? ruleCode = ReadRequiredString(root, "ruleCode", filePath, diagnostics);
            string? name = ReadRequiredString(root, "name", filePath, diagnostics);
            RuleCategory? category = ReadControlledValue<RuleCategory>(root, "category", filePath, RuleCatalogDiagnosticCodes.InvalidCategory, diagnostics);
            FindingSeverity? severity = ReadControlledValue<FindingSeverity>(root, "severity", filePath, RuleCatalogDiagnosticCodes.InvalidSeverity, diagnostics);
            RuleFindingStatus? defaultStatus = ReadControlledValue<RuleFindingStatus>(root, "defaultStatus", filePath, RuleCatalogDiagnosticCodes.InvalidStatus, diagnostics);
            bool enabled = ReadOptionalBoolean(root, "enabled", defaultValue: true);
            string? version = ReadRequiredString(root, "version", filePath, diagnostics);
            string? description = ReadRequiredString(root, "description", filePath, diagnostics);
            bool builtIn = ReadOptionalBoolean(root, "builtIn", defaultValue: false);
            string? ownerScope = ReadOptionalString(root, "ownerScope");
            IReadOnlyList<string> sourceUrls = ReadOptionalStringArray(root, "sourceUrls");
            IReadOnlyList<string> impact = ReadOptionalStringArray(root, "impact");
            IReadOnlyList<string> evidenceRequirements = ReadOptionalStringArray(root, "evidenceRequirements");
            IReadOnlyList<string> recommendedActions = ReadOptionalStringArray(root, "recommendedActions");
            IReadOnlyList<string> tags = ReadOptionalStringArray(root, "tags");
            GraphMetadata metadata = ReadMetadata(root);
            RuleDetectionGroup? detection = ReadDetection(root, filePath, diagnostics);

            if (!string.IsNullOrWhiteSpace(version) && !s_semanticVersionPattern.IsMatch(version.Trim()))
            {
                diagnostics.Add(new RuleCatalogDiagnostic(
                    RuleCatalogDiagnosticCodes.InvalidVersion,
                    $"Rule version '{version}' must use semantic version format major.minor.patch with optional prerelease or build suffix.",
                    filePath,
                    "version"));
            }

            if (diagnostics.Count > 0 || ruleCode is null || name is null || category is null || severity is null || defaultStatus is null || version is null || description is null || detection is null)
            {
                return new RuleFileValidationResult(null, diagnostics);
            }

            RuleCatalogEntry rule = new(
                ruleCode,
                name,
                category,
                severity,
                defaultStatus,
                enabled,
                version,
                description,
                definitionJson,
                sourceUrls,
                builtIn,
                ownerScope,
                impact,
                evidenceRequirements,
                recommendedActions,
                tags,
                metadata,
                detection,
                filePath);

            return new RuleFileValidationResult(rule, []);
        }

        /// <summary>
        /// Reads and validates the root detection group from a rule JSON document.
        /// </summary>
        /// <param name="root">The parsed rule root object.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        /// <returns>The validated detection group, or <see langword="null"/> when validation fails.</returns>
        private static RuleDetectionGroup? ReadDetection(JsonElement root, string filePath, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Detection is required because a rule without a predicate cannot be evaluated deterministically later.
            if (!root.TryGetProperty("detection", out JsonElement detectionElement) || detectionElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.DetectionMissing, "Rule detection block is required.", filePath, "detection"));
                return null;
            }

            return ReadDetectionGroup(detectionElement, filePath, "detection", diagnostics);
        }

        /// <summary>
        /// Reads and validates one detection group, including nested groups.
        /// </summary>
        /// <param name="element">The JSON object that contains the detection group.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="path">The JSON path for diagnostics.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        /// <returns>The validated detection group, or <see langword="null"/> when validation fails.</returns>
        private static RuleDetectionGroup? ReadDetectionGroup(JsonElement element, string filePath, string path, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Group validation is recursive so nested boolean structures receive the same schema and semantic checks as the root.
            if (element.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.DetectionMissing, "Detection group must be a JSON object.", filePath, path));
                return null;
            }

            IReadOnlyList<NodeKind> nodeKinds = ReadNodeKinds(element, filePath, path, diagnostics);
            RuleDetectionMatch? match = ReadMatch(element, filePath, path, diagnostics);
            IReadOnlyList<RuleCondition> conditions = ReadConditions(element, filePath, path, diagnostics);
            IReadOnlyList<RuleDetectionGroup> groups = ReadNestedGroups(element, filePath, path, diagnostics);
            if (conditions.Count == 0 && groups.Count == 0)
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.EmptyDetectionGroup, "Detection group must contain at least one condition or nested group.", filePath, path));
            }

            return match is null ? null : new RuleDetectionGroup(nodeKinds, match, conditions, groups);
        }

        /// <summary>
        /// Reads candidate node kind filters from a detection group.
        /// </summary>
        /// <param name="element">The detection group JSON element.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="path">The JSON path for diagnostics.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        /// <returns>The parsed node kind values.</returns>
        private static IReadOnlyList<NodeKind> ReadNodeKinds(JsonElement element, string filePath, string path, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Node kind filters are optional, but supplied values must match the shared graph controlled-value vocabulary.
            if (!element.TryGetProperty("nodeKinds", out JsonElement nodeKindsElement) || nodeKindsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<NodeKind> nodeKinds = [];
            int index = 0;
            foreach (JsonElement nodeKindElement in nodeKindsElement.EnumerateArray())
            {
                string? nodeKindValue = nodeKindElement.ValueKind == JsonValueKind.String ? nodeKindElement.GetString() : null;
                if (!NodeKind.TryParse(nodeKindValue, out NodeKind? nodeKind))
                {
                    diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.InvalidNodeKind, $"Node kind '{nodeKindValue}' is not supported.", filePath, $"{path}.nodeKinds[{index}]"));
                }
                else
                {
                    nodeKinds.Add(nodeKind);
                }

                index++;
            }

            return nodeKinds;
        }

        /// <summary>
        /// Reads the detection match mode from a detection group.
        /// </summary>
        /// <param name="element">The detection group JSON element.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="path">The JSON path for diagnostics.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        /// <returns>The parsed match mode, defaulting to all when omitted.</returns>
        private static RuleDetectionMatch? ReadMatch(JsonElement element, string filePath, string path, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Omitted match defaults to all so simple single-condition rules stay concise and deterministic.
            string? matchValue = element.TryGetProperty("match", out JsonElement matchElement) && matchElement.ValueKind == JsonValueKind.String
                ? matchElement.GetString()
                : RuleDetectionMatch.MatchAll.Value;
            if (RuleDetectionMatch.TryParse(matchValue, out RuleDetectionMatch? match))
            {
                return match;
            }

            diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.InvalidMatch, $"Detection match value '{matchValue}' is not supported. Use all, any, or none.", filePath, $"{path}.match"));
            return null;
        }

        /// <summary>
        /// Reads condition operands from a detection group.
        /// </summary>
        /// <param name="element">The detection group JSON element.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="path">The JSON path for diagnostics.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        /// <returns>The parsed condition operands that passed condition-level validation.</returns>
        private static IReadOnlyList<RuleCondition> ReadConditions(JsonElement element, string filePath, string path, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Condition arrays are optional only when nested groups provide operands.
            if (!element.TryGetProperty("conditions", out JsonElement conditionsElement) || conditionsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<RuleCondition> conditions = [];
            int index = 0;
            foreach (JsonElement conditionElement in conditionsElement.EnumerateArray())
            {
                RuleCondition? condition = ReadCondition(conditionElement, filePath, $"{path}.conditions[{index}]", diagnostics);
                if (condition is not null)
                {
                    conditions.Add(condition);
                }

                index++;
            }

            return conditions;
        }

        /// <summary>
        /// Reads and validates one condition operand.
        /// </summary>
        /// <param name="element">The condition JSON element.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="path">The JSON path for diagnostics.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        /// <returns>The parsed condition when valid; otherwise, <see langword="null"/>.</returns>
        private static RuleCondition? ReadCondition(JsonElement element, string filePath, string path, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Condition validation separates unsupported vocabulary from payload/operator compatibility so authors get precise messages.
            if (element.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.UnsupportedConditionKind, "Condition must be a JSON object.", filePath, path));
                return null;
            }

            string? kindValue = element.TryGetProperty("kind", out JsonElement kindElement) && kindElement.ValueKind == JsonValueKind.String ? kindElement.GetString() : null;
            string? operatorValue = element.TryGetProperty("operator", out JsonElement operatorElement) && operatorElement.ValueKind == JsonValueKind.String ? operatorElement.GetString() : null;
            RuleConditionKind? kind = null;
            RuleConditionOperator? conditionOperator = null;
            if (!RuleConditionKind.TryParse(kindValue, out kind))
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.UnsupportedConditionKind, $"Condition kind '{kindValue}' is not supported.", filePath, $"{path}.kind"));
            }

            if (!RuleConditionOperator.TryParse(operatorValue, out conditionOperator))
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.UnsupportedOperator, $"Condition operator '{operatorValue}' is not supported.", filePath, $"{path}.operator"));
            }

            if (kind is null || conditionOperator is null)
            {
                return null;
            }

            ValidateConditionPayload(kind, conditionOperator, element, filePath, path, diagnostics);
            return new RuleCondition(kind, conditionOperator, element);
        }

        /// <summary>
        /// Validates required condition payload fields and operator compatibility.
        /// </summary>
        /// <param name="kind">The parsed condition kind.</param>
        /// <param name="conditionOperator">The parsed condition operator.</param>
        /// <param name="element">The condition JSON element.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="path">The JSON path for diagnostics.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        private static void ValidateConditionPayload(RuleConditionKind kind, RuleConditionOperator conditionOperator, JsonElement element, string filePath, string path, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Work Item 1 validates the DSL shape; later evaluator slices will attach these payloads to graph fact accessors.
            if (kind == RuleConditionKind.MetricThreshold)
            {
                ValidateMetricThresholdCondition(conditionOperator, element, filePath, path, diagnostics);
                return;
            }

            bool hasValue = element.TryGetProperty("value", out JsonElement valueElement) && valueElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
            bool hasValues = element.TryGetProperty("values", out JsonElement valuesElement) && valuesElement.ValueKind == JsonValueKind.Array && valuesElement.GetArrayLength() > 0;
            if (!hasValue && !hasValues)
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.RequiredFieldMissing, "Condition must define value or values.", filePath, path));
            }

            if ((conditionOperator == RuleConditionOperator.In || conditionOperator == RuleConditionOperator.NotIn) && !hasValues)
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.OperatorIncompatibleWithCondition, $"Operator '{conditionOperator.Value}' requires a non-empty values array.", filePath, $"{path}.operator"));
            }

            if (IsNumericOperator(conditionOperator))
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.OperatorIncompatibleWithCondition, $"Operator '{conditionOperator.Value}' is only supported for metric-threshold conditions.", filePath, $"{path}.operator"));
            }
        }

        /// <summary>
        /// Validates a metric-threshold condition payload and its numeric operator.
        /// </summary>
        /// <param name="conditionOperator">The parsed condition operator.</param>
        /// <param name="element">The condition JSON element.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="path">The JSON path for diagnostics.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        private static void ValidateMetricThresholdCondition(RuleConditionOperator conditionOperator, JsonElement element, string filePath, string path, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Metric thresholds require a metric name and numeric comparison value because they operate over numeric metric facts.
            if (!element.TryGetProperty("metric", out JsonElement metricElement) || metricElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(metricElement.GetString()))
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.RequiredFieldMissing, "Metric-threshold condition must define metric.", filePath, $"{path}.metric"));
            }

            if (!element.TryGetProperty("value", out JsonElement valueElement) || valueElement.ValueKind != JsonValueKind.Number)
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.RequiredFieldMissing, "Metric-threshold condition must define a numeric value.", filePath, $"{path}.value"));
            }

            if (!IsNumericOperator(conditionOperator))
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.OperatorIncompatibleWithCondition, $"Operator '{conditionOperator.Value}' is not compatible with metric-threshold conditions.", filePath, $"{path}.operator"));
            }
        }

        /// <summary>
        /// Reads nested detection-group operands from a detection group.
        /// </summary>
        /// <param name="element">The detection group JSON element.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="path">The JSON path for diagnostics.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        /// <returns>The parsed nested groups that passed group-level validation.</returns>
        private static IReadOnlyList<RuleDetectionGroup> ReadNestedGroups(JsonElement element, string filePath, string path, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Nested groups let authors compose boolean predicates recursively without adding executable code to rule files.
            if (!element.TryGetProperty("groups", out JsonElement groupsElement) || groupsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<RuleDetectionGroup> groups = [];
            int index = 0;
            foreach (JsonElement groupElement in groupsElement.EnumerateArray())
            {
                RuleDetectionGroup? group = ReadDetectionGroup(groupElement, filePath, $"{path}.groups[{index}]", diagnostics);
                if (group is not null)
                {
                    groups.Add(group);
                }

                index++;
            }

            return groups;
        }

        /// <summary>
        /// Reads a required string property and appends a diagnostic when the value is missing or blank.
        /// </summary>
        /// <param name="element">The object containing the property.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        /// <returns>The trimmed string value when valid; otherwise, <see langword="null"/>.</returns>
        private static string? ReadRequiredString(JsonElement element, string propertyName, string filePath, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Required text fields must be meaningful because they form catalog identity and author-facing explanations.
            string? value = ReadOptionalString(element, propertyName);
            if (string.IsNullOrWhiteSpace(value))
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.RequiredFieldMissing, $"Rule field '{propertyName}' is required.", filePath, propertyName));
                return null;
            }

            return value.Trim();
        }

        /// <summary>
        /// Reads an optional string property from a JSON object.
        /// </summary>
        /// <param name="element">The object containing the property.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>The trimmed string value when present and non-blank; otherwise, <see langword="null"/>.</returns>
        private static string? ReadOptionalString(JsonElement element, string propertyName)
        {
            // Optional strings normalize whitespace-only values to null so callers can apply required-field rules explicitly.
            if (!element.TryGetProperty(propertyName, out JsonElement valueElement) || valueElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? value = valueElement.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Reads an optional boolean property from a JSON object.
        /// </summary>
        /// <param name="element">The object containing the property.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <param name="defaultValue">The fallback value when the property is absent or not a boolean.</param>
        /// <returns>The boolean value or the supplied default.</returns>
        private static bool ReadOptionalBoolean(JsonElement element, string propertyName, bool defaultValue)
        {
            // Optional booleans default deterministically so missing flags never depend on serializer behavior.
            if (!element.TryGetProperty(propertyName, out JsonElement valueElement))
            {
                return defaultValue;
            }

            return valueElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => defaultValue
            };
        }

        /// <summary>
        /// Reads an optional string array property from a JSON object.
        /// </summary>
        /// <param name="element">The object containing the property.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <returns>A deterministic array of non-empty strings.</returns>
        private static IReadOnlyList<string> ReadOptionalStringArray(JsonElement element, string propertyName)
        {
            // Optional arrays ignore non-string and blank values because Work Item 1 only needs deterministic catalog loading.
            if (!element.TryGetProperty(propertyName, out JsonElement arrayElement) || arrayElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return arrayElement.EnumerateArray()
                .Where(static value => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                .Select(static value => value.GetString()!.Trim())
                .ToArray();
        }

        /// <summary>
        /// Reads a controlled value property from a JSON object.
        /// </summary>
        /// <typeparam name="TValue">The controlled-value type to parse.</typeparam>
        /// <param name="element">The object containing the property.</param>
        /// <param name="propertyName">The property to read.</param>
        /// <param name="filePath">The runtime rule file path.</param>
        /// <param name="diagnosticCode">The diagnostic code to emit when parsing fails.</param>
        /// <param name="diagnostics">The diagnostics collection receiving validation failures.</param>
        /// <returns>The parsed controlled value when valid; otherwise, <see langword="null"/>.</returns>
        private static TValue? ReadControlledValue<TValue>(JsonElement element, string propertyName, string filePath, string diagnosticCode, List<RuleCatalogDiagnostic> diagnostics)
            where TValue : ControlledValue<TValue>
        {
            // Controlled values keep external JSON strings aligned with the domain vocabulary and reject accidental enum drift.
            string? value = ReadOptionalString(element, propertyName);
            if (string.IsNullOrWhiteSpace(value))
            {
                diagnostics.Add(new RuleCatalogDiagnostic(RuleCatalogDiagnosticCodes.RequiredFieldMissing, $"Rule field '{propertyName}' is required.", filePath, propertyName));
                return null;
            }

            if (ControlledValue<TValue>.TryParse(value, out TValue? parsed))
            {
                return parsed;
            }

            diagnostics.Add(new RuleCatalogDiagnostic(diagnosticCode, $"Value '{value}' is not supported for '{propertyName}'.", filePath, propertyName));
            return null;
        }

        /// <summary>
        /// Reads metadata from a rule JSON object.
        /// </summary>
        /// <param name="element">The object containing optional metadata.</param>
        /// <returns>The deterministic metadata object.</returns>
        private static GraphMetadata ReadMetadata(JsonElement element)
        {
            // Metadata remains optional and schema-light in Work Item 1 while preserving deterministic canonical serialization.
            if (!element.TryGetProperty("metadata", out JsonElement metadataElement) || metadataElement.ValueKind != JsonValueKind.Object)
            {
                return GraphMetadata.Empty;
            }

            Dictionary<string, object?> values = [];
            foreach (JsonProperty property in metadataElement.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                values[property.Name] = ConvertMetadataValue(property.Value);
            }

            return GraphMetadata.From(values);
        }

        /// <summary>
        /// Converts a JSON metadata value into a GraphMetadata-compatible CLR value.
        /// </summary>
        /// <param name="element">The JSON element to convert.</param>
        /// <returns>A JSON-compatible CLR value.</returns>
        private static object? ConvertMetadataValue(JsonElement element)
        {
            // The converter preserves primitive and nested JSON shapes without keeping JsonDocument-owned elements alive.
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out long longValue) => longValue,
                JsonValueKind.Number when element.TryGetDouble(out double doubleValue) => doubleValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Array => element.EnumerateArray().Select(ConvertMetadataValue).ToArray(),
                JsonValueKind.Object => element.EnumerateObject().ToDictionary(static property => property.Name, property => ConvertMetadataValue(property.Value), StringComparer.Ordinal),
                _ => null
            };
        }

        /// <summary>
        /// Adds duplicate rule identity diagnostics for repeated code and version combinations.
        /// </summary>
        /// <param name="rules">The loaded rules to inspect.</param>
        /// <param name="diagnostics">The diagnostics collection receiving duplicate findings.</param>
        private static void AddDuplicateDiagnostics(IReadOnlyList<RuleCatalogEntry> rules, List<RuleCatalogDiagnostic> diagnostics)
        {
            // Rule code plus version is the durable catalog identity, so duplicates would make persisted findings ambiguous.
            foreach (IGrouping<string, RuleCatalogEntry> group in rules.GroupBy(static rule => rule.RuleCode + "@" + rule.Version, StringComparer.Ordinal).Where(static group => group.Count() > 1))
            {
                RuleCatalogEntry firstRule = group.First();
                diagnostics.Add(new RuleCatalogDiagnostic(
                    RuleCatalogDiagnosticCodes.DuplicateRuleIdentity,
                    $"Rule identity '{firstRule.RuleCode}' version '{firstRule.Version}' appears in multiple files: {string.Join(", ", group.Select(static rule => Path.GetFileName(rule.SourceFilePath)).OrderBy(static fileName => fileName, StringComparer.OrdinalIgnoreCase))}.",
                    firstRule.SourceFilePath,
                    "ruleCode"));
            }
        }

        /// <summary>
        /// Determines whether an operator performs numeric comparison.
        /// </summary>
        /// <param name="conditionOperator">The operator to inspect.</param>
        /// <returns><see langword="true"/> when the operator compares numeric values; otherwise, <see langword="false"/>.</returns>
        private static bool IsNumericOperator(RuleConditionOperator conditionOperator)
        {
            // Numeric comparison is currently reserved for metric thresholds because other condition payloads are string-like identifiers.
            return conditionOperator == RuleConditionOperator.GreaterThan
                || conditionOperator == RuleConditionOperator.GreaterThanOrEqual
                || conditionOperator == RuleConditionOperator.LessThan
                || conditionOperator == RuleConditionOperator.LessThanOrEqual;
        }

        /// <summary>
        /// Creates the semantic-version regular expression used by rule validation.
        /// </summary>
        /// <returns>The semantic-version pattern.</returns>
        private static Regex CreateSemanticVersionPattern()
        {
            // A bounded timeout keeps authored version validation deterministic without relying on source-generated regex support.
            return new Regex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        }

        /// <summary>
        /// Represents the result of validating one rule file.
        /// </summary>
        /// <param name="Rule">The validated rule when the file passed validation.</param>
        /// <param name="Diagnostics">The validation diagnostics for the file.</param>
        private sealed record RuleFileValidationResult(RuleCatalogEntry? Rule, IReadOnlyList<RuleCatalogDiagnostic> Diagnostics);
    }
}
