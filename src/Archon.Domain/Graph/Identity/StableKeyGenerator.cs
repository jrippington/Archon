namespace Archon.Domain.Graph.Identity
{
    /// <summary>
    /// Generates deterministic stable keys for architecture graph entities through one shared domain component.
    /// </summary>
    /// <remarks>
    /// Extraction slices should use this generator instead of constructing key strings directly. Centralizing the prefixes and normalization rules prevents divergent identities for equivalent repositories, projects, symbols, files, rules, findings, metrics, and summaries.
    /// </remarks>
    public static class StableKeyGenerator
    {
        /// <summary>
        /// Generates a repository stable key.
        /// </summary>
        /// <param name="repositoryName">The logical repository name or repository identity token.</param>
        /// <returns>A stable key with the <c>repository://</c> prefix.</returns>
        public static StableKey ForRepository(string? repositoryName)
        {
            // Repository names are logical identities and are not path-normalized.
            return CreateNamedKey("repository://", repositoryName, nameof(repositoryName));
        }

        /// <summary>
        /// Generates a solution stable key from a repository-relative solution path.
        /// </summary>
        /// <param name="solutionPath">The repository-relative solution path.</param>
        /// <returns>A stable key with the <c>solution://</c> prefix.</returns>
        public static StableKey ForSolution(string? solutionPath)
        {
            // Solution identity must be relative to the repository so developer machine roots do not affect snapshots.
            return CreatePathKey("solution://", solutionPath);
        }

        /// <summary>
        /// Generates a project stable key from a repository-relative project path.
        /// </summary>
        /// <param name="projectPath">The repository-relative project path.</param>
        /// <returns>A stable key with the <c>project://</c> prefix.</returns>
        public static StableKey ForProject(string? projectPath)
        {
            // Project keys use normalized paths because project files are repository artifacts.
            return CreatePathKey("project://", projectPath);
        }

        /// <summary>
        /// Generates a package stable key.
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <returns>A stable key with the <c>package://</c> prefix.</returns>
        public static StableKey ForPackage(string? packageId)
        {
            // Package IDs are external logical names and are trimmed but otherwise preserved.
            return CreateNamedKey("package://", packageId, nameof(packageId));
        }

        /// <summary>
        /// Generates a namespace stable key.
        /// </summary>
        /// <param name="qualifiedNamespace">The fully qualified namespace name.</param>
        /// <returns>A stable key with the <c>namespace://</c> prefix.</returns>
        public static StableKey ForNamespace(string? qualifiedNamespace)
        {
            // Namespace keys preserve CLR naming case because symbol identity is case-sensitive in C#.
            return CreateNamedKey("namespace://", qualifiedNamespace, nameof(qualifiedNamespace));
        }

        /// <summary>
        /// Generates a type stable key.
        /// </summary>
        /// <param name="qualifiedTypeName">The fully qualified type name.</param>
        /// <returns>A stable key with the <c>type://</c> prefix.</returns>
        public static StableKey ForType(string? qualifiedTypeName)
        {
            // Type keys use the compiler-facing qualified name supplied by extractors.
            return CreateNamedKey("type://", qualifiedTypeName, nameof(qualifiedTypeName));
        }

        /// <summary>
        /// Generates a method stable key.
        /// </summary>
        /// <param name="methodSignature">The fully qualified method signature, including parameters when available.</param>
        /// <returns>A stable key with the <c>method://</c> prefix.</returns>
        public static StableKey ForMethod(string? methodSignature)
        {
            // Method signatures disambiguate overloads and are expected to be canonicalized by symbol extractors.
            return CreateNamedKey("method://", methodSignature, nameof(methodSignature));
        }

        /// <summary>
        /// Generates a property stable key.
        /// </summary>
        /// <param name="qualifiedPropertyName">The fully qualified property name.</param>
        /// <returns>A stable key with the <c>property://</c> prefix.</returns>
        public static StableKey ForProperty(string? qualifiedPropertyName)
        {
            // Property keys preserve the exact qualified symbol identity.
            return CreateNamedKey("property://", qualifiedPropertyName, nameof(qualifiedPropertyName));
        }

        /// <summary>
        /// Generates a field stable key.
        /// </summary>
        /// <param name="qualifiedFieldName">The fully qualified field name.</param>
        /// <returns>A stable key with the <c>field://</c> prefix.</returns>
        public static StableKey ForField(string? qualifiedFieldName)
        {
            // Field keys preserve the exact qualified symbol identity.
            return CreateNamedKey("field://", qualifiedFieldName, nameof(qualifiedFieldName));
        }

        /// <summary>
        /// Generates an endpoint stable key from an HTTP method and route template.
        /// </summary>
        /// <param name="httpMethod">The HTTP method or endpoint verb.</param>
        /// <param name="routeTemplate">The endpoint route template.</param>
        /// <returns>A stable key with the <c>endpoint://</c> prefix.</returns>
        public static StableKey ForEndpoint(string? httpMethod, string? routeTemplate)
        {
            // Endpoint keys normalize the verb to uppercase and ensure the route starts with one slash.
            string method = RequireText(httpMethod, nameof(httpMethod)).ToUpperInvariant();
            string route = RequireText(routeTemplate, nameof(routeTemplate)).TrimStart('/');

            return new StableKey($"endpoint://{method}:/{route}");
        }

        /// <summary>
        /// Generates a controller stable key.
        /// </summary>
        /// <param name="qualifiedControllerName">The fully qualified controller type name.</param>
        /// <returns>A stable key with the <c>controller://</c> prefix.</returns>
        public static StableKey ForController(string? qualifiedControllerName)
        {
            // Controller keys are symbol-based because controllers are architecture types with endpoint behavior.
            return CreateNamedKey("controller://", qualifiedControllerName, nameof(qualifiedControllerName));
        }

        /// <summary>
        /// Generates a hosted-service stable key.
        /// </summary>
        /// <param name="qualifiedHostedServiceName">The fully qualified hosted-service type name.</param>
        /// <returns>A stable key with the <c>hostedservice://</c> prefix.</returns>
        public static StableKey ForHostedService(string? qualifiedHostedServiceName)
        {
            // Hosted-service keys use type names so worker behavior can be compared across snapshots.
            return CreateNamedKey("hostedservice://", qualifiedHostedServiceName, nameof(qualifiedHostedServiceName));
        }

        /// <summary>
        /// Generates a configuration-key stable key.
        /// </summary>
        /// <param name="configurationKey">The normalized configuration key path.</param>
        /// <returns>A stable key with the <c>config://</c> prefix.</returns>
        public static StableKey ForConfigurationKey(string? configurationKey)
        {
            // Configuration keys are logical colon-delimited names and should not be path-normalized.
            return CreateNamedKey("config://", configurationKey, nameof(configurationKey));
        }

        /// <summary>
        /// Generates an Entity Framework DbContext stable key.
        /// </summary>
        /// <param name="qualifiedDbContextName">The fully qualified DbContext type name.</param>
        /// <returns>A stable key with the <c>dbcontext://</c> prefix.</returns>
        public static StableKey ForDbContext(string? qualifiedDbContextName)
        {
            // DbContext keys are type-based because the context class is the architecture concept.
            return CreateNamedKey("dbcontext://", qualifiedDbContextName, nameof(qualifiedDbContextName));
        }

        /// <summary>
        /// Generates a LINQ to SQL data-context stable key.
        /// </summary>
        /// <param name="qualifiedDataContextName">The fully qualified LINQ to SQL data-context type name.</param>
        /// <returns>A stable key with the <c>linqtosql://</c> prefix.</returns>
        public static StableKey ForLinqToSqlDataContext(string? qualifiedDataContextName)
        {
            // LINQ to SQL contexts use their type names so legacy data-access facts remain stable.
            return CreateNamedKey("linqtosql://", qualifiedDataContextName, nameof(qualifiedDataContextName));
        }

        /// <summary>
        /// Generates an entity stable key.
        /// </summary>
        /// <param name="qualifiedEntityName">The fully qualified entity type name.</param>
        /// <returns>A stable key with the <c>entity://</c> prefix.</returns>
        public static StableKey ForEntity(string? qualifiedEntityName)
        {
            // Entity keys are type-based and preserve the extractor-provided canonical name.
            return CreateNamedKey("entity://", qualifiedEntityName, nameof(qualifiedEntityName));
        }

        /// <summary>
        /// Generates a database-table stable key.
        /// </summary>
        /// <param name="schemaName">The database schema name.</param>
        /// <param name="tableName">The database table name.</param>
        /// <returns>A stable key with the <c>dbtable://</c> prefix.</returns>
        public static StableKey ForDatabaseTable(string? schemaName, string? tableName)
        {
            // Schema and table are both required so keys distinguish same-named tables in different schemas.
            return new StableKey($"dbtable://{RequireText(schemaName, nameof(schemaName))}.{RequireText(tableName, nameof(tableName))}");
        }

        /// <summary>
        /// Generates a database-column stable key.
        /// </summary>
        /// <param name="schemaName">The database schema name.</param>
        /// <param name="tableName">The database table name.</param>
        /// <param name="columnName">The database column name.</param>
        /// <returns>A stable key with the <c>dbcolumn://</c> prefix.</returns>
        public static StableKey ForDatabaseColumn(string? schemaName, string? tableName, string? columnName)
        {
            // Column identity includes schema and table to avoid ambiguous cross-table column names.
            return new StableKey($"dbcolumn://{RequireText(schemaName, nameof(schemaName))}.{RequireText(tableName, nameof(tableName))}.{RequireText(columnName, nameof(columnName))}");
        }

        /// <summary>
        /// Generates a stored-procedure stable key.
        /// </summary>
        /// <param name="schemaName">The database schema name.</param>
        /// <param name="procedureName">The stored procedure name.</param>
        /// <returns>A stable key with the <c>storedprocedure://</c> prefix.</returns>
        public static StableKey ForStoredProcedure(string? schemaName, string? procedureName)
        {
            // Stored procedures are schema-qualified for deterministic database identity.
            return new StableKey($"storedprocedure://{RequireText(schemaName, nameof(schemaName))}.{RequireText(procedureName, nameof(procedureName))}");
        }

        /// <summary>
        /// Generates an external-service stable key.
        /// </summary>
        /// <param name="serviceName">The external service name or canonical integration identity.</param>
        /// <returns>A stable key with the <c>externalservice://</c> prefix.</returns>
        public static StableKey ForExternalService(string? serviceName)
        {
            // External service names are logical integration identities supplied by extractors.
            return CreateNamedKey("externalservice://", serviceName, nameof(serviceName));
        }

        /// <summary>
        /// Generates a queue stable key.
        /// </summary>
        /// <param name="queueName">The queue name.</param>
        /// <returns>A stable key with the <c>queue://</c> prefix.</returns>
        public static StableKey ForQueue(string? queueName)
        {
            // Queue names are transport-level logical identifiers and should remain readable.
            return CreateNamedKey("queue://", queueName, nameof(queueName));
        }

        /// <summary>
        /// Generates a topic stable key.
        /// </summary>
        /// <param name="topicName">The topic name.</param>
        /// <returns>A stable key with the <c>topic://</c> prefix.</returns>
        public static StableKey ForTopic(string? topicName)
        {
            // Topic names are transport-level logical identifiers and should remain readable.
            return CreateNamedKey("topic://", topicName, nameof(topicName));
        }

        /// <summary>
        /// Generates a file stable key from a repository-relative file path.
        /// </summary>
        /// <param name="filePath">The repository-relative file path.</param>
        /// <returns>A stable key with the <c>file://</c> prefix.</returns>
        public static StableKey ForFile(string? filePath)
        {
            // File keys are path-based and must be repository-relative for cross-machine determinism.
            return CreatePathKey("file://", filePath);
        }

        /// <summary>
        /// Generates a pipeline stable key from a repository-relative pipeline path.
        /// </summary>
        /// <param name="pipelinePath">The repository-relative pipeline definition path.</param>
        /// <returns>A stable key with the <c>pipeline://</c> prefix.</returns>
        public static StableKey ForPipeline(string? pipelinePath)
        {
            // Pipeline definitions are repository artifacts, so their stable keys use normalized relative paths.
            return CreatePathKey("pipeline://", pipelinePath);
        }

        /// <summary>
        /// Generates a rule stable key.
        /// </summary>
        /// <param name="ruleCode">The rule code.</param>
        /// <param name="ruleVersion">The rule version.</param>
        /// <returns>A stable key with the <c>rule://</c> prefix.</returns>
        public static StableKey ForRule(string? ruleCode, string? ruleVersion)
        {
            // Rule identity includes version so historical findings remain explainable after rule behavior changes.
            return new StableKey($"rule://{RequireText(ruleCode, nameof(ruleCode))}@{RequireText(ruleVersion, nameof(ruleVersion))}");
        }

        /// <summary>
        /// Generates a finding stable key.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="ruleCode">The rule code that produced the finding.</param>
        /// <param name="targetIdentity">The primary target identity or deterministic finding discriminator.</param>
        /// <returns>A stable key with the <c>finding://</c> prefix.</returns>
        public static StableKey ForFinding(string? snapshotStableKey, string? ruleCode, string? targetIdentity)
        {
            // Findings are snapshot-scoped because the same rule and target can appear differently across extraction runs.
            return new StableKey($"finding://{RequireText(snapshotStableKey, nameof(snapshotStableKey))}/{RequireText(ruleCode, nameof(ruleCode))}/{RequireText(targetIdentity, nameof(targetIdentity))}");
        }

        /// <summary>
        /// Generates a metric stable key.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the metric.</param>
        /// <param name="metricName">The metric name.</param>
        /// <param name="scopeIdentity">The metric scope identity or discriminator.</param>
        /// <returns>A stable key with the <c>metric://</c> prefix.</returns>
        public static StableKey ForMetric(string? snapshotStableKey, string? metricName, string? scopeIdentity)
        {
            // Metrics are snapshot-scoped first-class outputs, so the key includes snapshot and scope information.
            return new StableKey($"metric://{RequireText(snapshotStableKey, nameof(snapshotStableKey))}/{RequireText(metricName, nameof(metricName))}/{RequireText(scopeIdentity, nameof(scopeIdentity))}");
        }

        /// <summary>
        /// Generates a generated-summary stable key.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the summary.</param>
        /// <param name="summaryKind">The summary kind or scope name.</param>
        /// <param name="targetIdentity">The target identity or deterministic summary discriminator.</param>
        /// <returns>A stable key with the <c>summary://</c> prefix.</returns>
        public static StableKey ForSummary(string? snapshotStableKey, string? summaryKind, string? targetIdentity)
        {
            // Generated summaries are snapshot-scoped narrative outputs and require a target discriminator.
            return new StableKey($"summary://{RequireText(snapshotStableKey, nameof(snapshotStableKey))}/{RequireText(summaryKind, nameof(summaryKind))}/{RequireText(targetIdentity, nameof(targetIdentity))}");
        }

        /// <summary>
        /// Creates a stable key from a prefix and a repository-relative path payload.
        /// </summary>
        /// <param name="prefix">The stable-key prefix to apply.</param>
        /// <param name="path">The repository-relative path payload.</param>
        /// <returns>A stable key containing the prefix and normalized path payload.</returns>
        private static StableKey CreatePathKey(string prefix, string? path)
        {
            // Path payloads are normalized through RepositoryRelativePath so separators and leading ./ do not affect identity.
            RepositoryRelativePath repositoryRelativePath = RepositoryRelativePath.Parse(path);
            return new StableKey($"{prefix}{repositoryRelativePath.Value}");
        }

        /// <summary>
        /// Creates a stable key from a prefix and a trimmed logical name payload.
        /// </summary>
        /// <param name="prefix">The stable-key prefix to apply.</param>
        /// <param name="value">The logical name payload.</param>
        /// <param name="parameterName">The source parameter name to report in validation failures.</param>
        /// <returns>A stable key containing the prefix and trimmed logical name payload.</returns>
        private static StableKey CreateNamedKey(string prefix, string? value, string parameterName)
        {
            // Named payloads are trimmed but not otherwise transformed so source-specific casing and punctuation remain stable.
            return new StableKey($"{prefix}{RequireText(value, parameterName)}");
        }

        /// <summary>
        /// Requires a non-empty text component and trims surrounding whitespace.
        /// </summary>
        /// <param name="value">The candidate text component.</param>
        /// <param name="parameterName">The source parameter name to report in validation failures.</param>
        /// <returns>The trimmed text component.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace-only.</exception>
        private static string RequireText(string? value, string parameterName)
        {
            // Every generated key segment must be explicit because missing segments create ambiguous identities.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Stable-key components cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }
    }
}
