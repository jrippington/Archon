using System.Text.Json;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Facts
{
    /// <summary>
    /// Implements controlled WP014 fact queries over extracted in-memory architecture snapshots.
    /// </summary>
    public sealed class FactQueryService : IFactQueryService
    {
        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Defines supported data-access family filters exposed by the public query API.
        /// </summary>
        private static readonly HashSet<string> s_dataAccessFamilies = new(StringComparer.OrdinalIgnoreCase)
        {
            "LinqToSql",
            "EFClassic",
            "EF6",
            "EFCore",
            "AdoNet",
            "TypedDataSet",
            "RawSql",
            "StoredProcedure",
            "Entity",
            "Table"
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="FactQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public FactQueryService(IArchitectureSnapshotWriter snapshotWriter)
        {
            // Fact queries use the same snapshot seam as earlier WP014 slices so tests and local hosts do not require Neo4j.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <inheritdoc />
        public Task<DataAccessFactResult> ListDataAccessFactsAsync(DataAccessFactQuery query, CancellationToken cancellationToken)
        {
            // Data-access lookup validates bounded options, resolves one snapshot, maps persisted graph facts, then applies controlled filters.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<FactQueryValidationError> optionErrors = ValidatePaging(query.Skip, query.Take);
            if (!string.IsNullOrWhiteSpace(query.Family) && !s_dataAccessFamilies.Contains(query.Family.Trim()))
            {
                optionErrors.Add(new FactQueryValidationError(FactQueryValidationCodes.FactFamilyUnsupported, "Data-access family must be LinqToSql, EFClassic, EF6, EFCore, AdoNet, TypedDataSet, RawSql, StoredProcedure, Entity, or Table."));
            }

            if (optionErrors.Count > 0)
            {
                return Task.FromResult(new DataAccessFactResult(optionErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new DataAccessFactResult(resolution.ValidationErrors));
            }

            FactQueryContext context = BuildContext(query.Selector, resolution, "dataAccessFacts", "No persisted data-access facts were available in the selected snapshot.");
            DataAccessFactDto[] filtered = BuildDataAccessFacts(resolution.Snapshot!)
                .Where(item => string.IsNullOrWhiteSpace(query.Family) || string.Equals(item.Family, NormalizeDataAccessFamilyFilter(query.Family), StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(query.ProjectStableKey) || StringComparer.Ordinal.Equals(item.ProjectStableKey, query.ProjectStableKey.Trim()))
                .Where(item => string.IsNullOrWhiteSpace(query.UsageSite) || item.UsageSites.Any(site => Contains(site, query.UsageSite)))
                .Where(item => string.IsNullOrWhiteSpace(query.Entity) || Contains(item.EntityStableKey, query.Entity) || Contains(item.Name, query.Entity))
                .Where(item => string.IsNullOrWhiteSpace(query.Table) || Contains(item.TableStableKey, query.Table) || Contains(item.Name, query.Table))
                .Where(item => string.IsNullOrWhiteSpace(query.StoredProcedure) || Contains(item.StoredProcedureStableKey, query.StoredProcedure) || Contains(item.Name, query.StoredProcedure))
                .OrderBy(static item => item.Family, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.ProjectStableKey ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            FactQueryContext responseContext = filtered.Length == 0 ? MergeUnknownContext(context, new FactUnknownDto("dataAccessFacts", "No persisted data-access facts matched the selected query scope.")) : context;
            return Task.FromResult(new DataAccessFactResult(ToPage(filtered, query.Skip, query.Take), responseContext));
        }

        /// <inheritdoc />
        public Task<ConfigurationUsageResult> ListConfigurationUsageAsync(ConfigurationUsageQuery query, CancellationToken cancellationToken)
        {
            // Configuration lookup exposes key metadata only and intentionally never maps values, connection strings, tokens, or secrets.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<FactQueryValidationError> optionErrors = ValidatePaging(query.Skip, query.Take);
            if (optionErrors.Count > 0)
            {
                return Task.FromResult(new ConfigurationUsageResult(optionErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new ConfigurationUsageResult(resolution.ValidationErrors));
            }

            FactQueryContext context = BuildContext(query.Selector, resolution, "configurationUsage", "No persisted configuration key facts were available in the selected snapshot.");
            ConfigurationUsageDto[] filtered = BuildConfigurationUsage(resolution.Snapshot!)
                .Where(item => string.IsNullOrWhiteSpace(query.ConfigurationKey) || Contains(item.Key, query.ConfigurationKey) || Contains(item.StableKey, query.ConfigurationKey))
                .Where(item => string.IsNullOrWhiteSpace(query.ProjectStableKey) || StringComparer.Ordinal.Equals(item.ProjectStableKey, query.ProjectStableKey.Trim()))
                .Where(item => string.IsNullOrWhiteSpace(query.ConsumerStableKey) || item.ConsumerStableKeys.Any(key => StringComparer.Ordinal.Equals(key, query.ConsumerStableKey.Trim())))
                .Where(item => string.IsNullOrWhiteSpace(query.Provider) || item.Providers.Any(provider => Contains(provider, query.Provider)))
                .Where(item => string.IsNullOrWhiteSpace(query.Environment) || Contains(item.Environment, query.Environment))
                .Where(item => string.IsNullOrWhiteSpace(query.SourceFile) || item.SourceFiles.Any(file => Contains(file, query.SourceFile)))
                .OrderBy(static item => item.ProjectStableKey ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            FactQueryContext responseContext = filtered.Length == 0 ? MergeUnknownContext(context, new FactUnknownDto("configurationUsage", "No persisted configuration usage facts matched the selected query scope.")) : context;
            return Task.FromResult(new ConfigurationUsageResult(ToPage(filtered, query.Skip, query.Take), responseContext));
        }

        /// <inheritdoc />
        public Task<IntegrationFactResult> ListIntegrationFactsAsync(IntegrationFactQuery query, CancellationToken cancellationToken)
        {
            // Integration lookup returns service names, protocols, client types, and safe configuration keys while redacting unsafe target details.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<FactQueryValidationError> optionErrors = ValidatePaging(query.Skip, query.Take);
            if (optionErrors.Count > 0)
            {
                return Task.FromResult(new IntegrationFactResult(optionErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new IntegrationFactResult(resolution.ValidationErrors));
            }

            FactQueryContext context = BuildContext(query.Selector, resolution, "integrationFacts", "No persisted external integration facts were available in the selected snapshot.");
            IntegrationFactDto[] filtered = BuildIntegrationFacts(resolution.Snapshot!)
                .Where(item => string.IsNullOrWhiteSpace(query.ProjectStableKey) || StringComparer.Ordinal.Equals(item.ProjectStableKey, query.ProjectStableKey.Trim()))
                .Where(item => string.IsNullOrWhiteSpace(query.IntegrationKind) || Contains(item.IntegrationKind, query.IntegrationKind))
                .Where(item => string.IsNullOrWhiteSpace(query.EndpointHost) || Contains(item.EndpointHost, query.EndpointHost) || Contains(item.Name, query.EndpointHost))
                .Where(item => string.IsNullOrWhiteSpace(query.Protocol) || Contains(item.Protocol, query.Protocol))
                .Where(item => string.IsNullOrWhiteSpace(query.ClientType) || Contains(item.ClientType, query.ClientType))
                .Where(item => string.IsNullOrWhiteSpace(query.ConfigurationKey) || item.ConfigurationKeys.Any(key => Contains(key, query.ConfigurationKey)))
                .OrderBy(static item => item.ProjectStableKey ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static item => item.IntegrationKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            FactQueryContext responseContext = filtered.Length == 0 ? MergeUnknownContext(context, new FactUnknownDto("integrationFacts", "No persisted integration facts matched the selected query scope.")) : context;
            return Task.FromResult(new IntegrationFactResult(ToPage(filtered, query.Skip, query.Take), responseContext));
        }

        /// <inheritdoc />
        public Task<UiTechnologyFactResult> ListUiTechnologyFactsAsync(UiTechnologyFactQuery query, CancellationToken cancellationToken)
        {
            // UI fact lookup is backend data only; it does not create or require Discovery UI pages, components, or assets.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<FactQueryValidationError> optionErrors = ValidatePaging(query.Skip, query.Take);
            if (optionErrors.Count > 0)
            {
                return Task.FromResult(new UiTechnologyFactResult(optionErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new UiTechnologyFactResult(resolution.ValidationErrors));
            }

            FactQueryContext context = BuildContext(query.Selector, resolution, "uiTechnologyFacts", "No persisted UI-technology facts were available in the selected snapshot.");
            UiTechnologyFactDto[] filtered = BuildUiTechnologyFacts(resolution.Snapshot!)
                .Where(item => string.IsNullOrWhiteSpace(query.Technology) || Contains(item.Technology, query.Technology))
                .Where(item => string.IsNullOrWhiteSpace(query.ProjectStableKey) || StringComparer.Ordinal.Equals(item.ProjectStableKey, query.ProjectStableKey.Trim()))
                .Where(item => string.IsNullOrWhiteSpace(query.Route) || Contains(item.Route, query.Route))
                .Where(item => string.IsNullOrWhiteSpace(query.Component) || Contains(item.Name, query.Component) || item.RelatedStableKeys.Any(key => Contains(key, query.Component)))
                .OrderBy(static item => item.ProjectStableKey ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static item => item.Technology, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.FactKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            FactQueryContext responseContext = filtered.Length == 0 ? MergeUnknownContext(context, new FactUnknownDto("uiTechnologyFacts", "No persisted UI-technology facts matched the selected query scope.")) : context;
            return Task.FromResult(new UiTechnologyFactResult(ToPage(filtered, query.Skip, query.Take), responseContext));
        }

        /// <summary>
        /// Reads snapshots from the in-memory fallback writer when that diagnostic path is available.
        /// </summary>
        /// <returns>The snapshots available to application-layer query services.</returns>
        private IReadOnlyList<ExtractedArchitectureSnapshot> GetSnapshots()
        {
            // Infrastructure-backed stores can replace this service later; the current slice uses the repository-standard in-memory query seam.
            return _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics()
                : [];
        }

        /// <summary>
        /// Resolves and validates the selected fact snapshot scope.
        /// </summary>
        /// <param name="selector">The repository, solution, and snapshot selector supplied by the query.</param>
        /// <returns>A successful snapshot resolution or deterministic validation errors.</returns>
        private SnapshotResolution ResolveSnapshot(FactSnapshotSelector selector)
        {
            // Scope validation runs before graph matching so missing or malformed selectors produce client-correctable problem details.
            List<FactQueryValidationError> validationErrors = ValidateSelector(selector);
            if (validationErrors.Count > 0)
            {
                return SnapshotResolution.Failed(validationErrors);
            }

            ExtractedArchitectureSnapshot[] repositorySnapshots = GetSnapshots()
                .Where(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.RepositoryStableKey.Value, selector.RepositoryStableKey))
                .ToArray();
            if (repositorySnapshots.Length == 0)
            {
                FactQueryValidationError error = new(FactQueryValidationCodes.RepositoryNotFound, "The requested repository scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot[] scopedSnapshots = ApplySolutionScope(repositorySnapshots, selector);
            if (selector.SolutionStableKey is not null && scopedSnapshots.Length == 0)
            {
                FactQueryValidationError error = new(FactQueryValidationCodes.SolutionNotFound, "The requested solution scope was not found for the repository.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot? selectedSnapshot = ResolveSelectedSnapshot(scopedSnapshots, selector);
            if (selectedSnapshot?.SnapshotHeader is null)
            {
                FactQueryValidationError error = new(FactQueryValidationCodes.SnapshotNotFound, "The requested snapshot scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            return SnapshotResolution.Success(selectedSnapshot, scopedSnapshots);
        }

        /// <summary>
        /// Validates selector syntax before any snapshot matching occurs.
        /// </summary>
        /// <param name="selector">The caller-supplied fact snapshot selector.</param>
        /// <returns>A deterministic list of selector validation errors.</returns>
        private static List<FactQueryValidationError> ValidateSelector(FactSnapshotSelector selector)
        {
            // Repository scope is required because latest resolution must be bounded to one repository.
            List<FactQueryValidationError> errors = [];
            if (selector.RepositoryStableKey is null)
            {
                errors.Add(new FactQueryValidationError(FactQueryValidationCodes.RepositoryStableKeyRequired, "A repository stable key is required for fact queries."));
            }

            if (!selector.RequestsLatestSnapshot && !selector.SnapshotStableKey.StartsWith("snapshot://", StringComparison.Ordinal))
            {
                errors.Add(new FactQueryValidationError(FactQueryValidationCodes.SnapshotSelectorInvalid, "Snapshot selector must be 'latest', 'current', or a snapshot:// stable key."));
            }

            return errors;
        }

        /// <summary>
        /// Applies the optional solution scope to repository snapshots.
        /// </summary>
        /// <param name="repositorySnapshots">The snapshots already matched to the requested repository.</param>
        /// <param name="selector">The caller-supplied fact snapshot selector.</param>
        /// <returns>The snapshots matching the optional solution scope.</returns>
        private static ExtractedArchitectureSnapshot[] ApplySolutionScope(IEnumerable<ExtractedArchitectureSnapshot> repositorySnapshots, FactSnapshotSelector selector)
        {
            // Solution scope is resolved through snapshot-level solution facts just like existing WP014 query scope resolution.
            return selector.SolutionStableKey is null
                ? repositorySnapshots.ToArray()
                : repositorySnapshots
                    .Where(snapshot => snapshot.Solutions.Any(solution => StringComparer.Ordinal.Equals(solution.StableKey.Value, selector.SolutionStableKey)))
                    .ToArray();
        }

        /// <summary>
        /// Resolves the selected snapshot from an already scoped snapshot set.
        /// </summary>
        /// <param name="scopedSnapshots">The repository and solution scoped snapshots.</param>
        /// <param name="selector">The caller-supplied fact snapshot selector.</param>
        /// <returns>The selected snapshot, or null when none matches.</returns>
        private static ExtractedArchitectureSnapshot? ResolveSelectedSnapshot(IEnumerable<ExtractedArchitectureSnapshot> scopedSnapshots, FactSnapshotSelector selector)
        {
            // Latest resolution uses completed time, started time, then stable key so repeated calls remain deterministic.
            return selector.RequestsLatestSnapshot
                ? scopedSnapshots
                    .Where(static snapshot => snapshot.SnapshotHeader is not null)
                    .OrderByDescending(static snapshot => snapshot.SnapshotHeader!.CompletedUtc ?? snapshot.SnapshotHeader.StartedUtc)
                    .ThenByDescending(static snapshot => snapshot.SnapshotHeader!.StartedUtc)
                    .ThenByDescending(static snapshot => snapshot.SnapshotHeader!.StableKey.Value, StringComparer.Ordinal)
                    .FirstOrDefault()
                : scopedSnapshots.FirstOrDefault(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.StableKey.Value, selector.SnapshotStableKey));
        }

        /// <summary>
        /// Validates shared paging values before snapshot work starts.
        /// </summary>
        /// <param name="skip">The requested skip value.</param>
        /// <param name="take">The requested take value.</param>
        /// <returns>A deterministic list of paging validation errors.</returns>
        private static List<FactQueryValidationError> ValidatePaging(int skip, int take)
        {
            // Paging bounds keep fact responses predictable and prevent accidental large graph reads.
            List<FactQueryValidationError> errors = [];
            if (skip < 0)
            {
                errors.Add(new FactQueryValidationError(FactQueryValidationCodes.SkipInvalid, "Fact query skip must be greater than or equal to zero."));
            }

            if (take < 1 || take > FactQueryLimits.MaximumTake)
            {
                errors.Add(new FactQueryValidationError(FactQueryValidationCodes.TakeInvalid, $"Fact query take must be between 1 and {FactQueryLimits.MaximumTake}."));
            }

            return errors;
        }

        /// <summary>
        /// Builds the fact query context shared by API envelopes.
        /// </summary>
        /// <param name="selector">The caller-supplied fact snapshot selector.</param>
        /// <param name="resolution">The successful snapshot resolution.</param>
        /// <param name="emptyField">The unknown field used when the selected fact family has no data.</param>
        /// <param name="emptyReason">The safe unknown reason used when the selected fact family has no data.</param>
        /// <returns>The fact query context for response mapping.</returns>
        private static FactQueryContext BuildContext(FactSnapshotSelector selector, SnapshotResolution resolution, string emptyField, string emptyReason)
        {
            // Context construction centralizes envelope metadata so all fact endpoints report scope consistently.
            ExtractedArchitectureSnapshot snapshot = resolution.Snapshot!;
            RepositoryModel? repository = snapshot.Repositories.FirstOrDefault(repository => StringComparer.Ordinal.Equals(repository.StableKey.Value, selector.RepositoryStableKey));
            SolutionModel? solution = selector.SolutionStableKey is null
                ? snapshot.Solutions.OrderBy(static candidate => candidate.StableKey.Value, StringComparer.Ordinal).FirstOrDefault()
                : snapshot.Solutions.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.StableKey.Value, selector.SolutionStableKey));
            ProjectScopeDto scope = new(selector.RepositoryStableKey!, repository?.Name, solution?.StableKey.Value, solution?.Name);
            ProjectSnapshotMetadataDto snapshotMetadata = new(
                snapshot.SnapshotHeader!.StableKey.Value,
                selector.SnapshotStableKey,
                selector.RequestsLatestSnapshot,
                snapshot.SnapshotHeader.CommitSha,
                snapshot.SnapshotHeader.StartedUtc,
                snapshot.SnapshotHeader.CompletedUtc,
                snapshot.SnapshotHeader.Status);
            FactWarningDto[] warnings = snapshot.Warnings.Select(static warning => new FactWarningDto("SnapshotWarning", warning)).ToArray();
            List<FactUnknownDto> unknowns = [];
            if (snapshot.Errors.Any())
            {
                unknowns.Add(new FactUnknownDto("factExtraction", "The selected snapshot contains extraction errors, so fact query data may be incomplete."));
            }

            if (!snapshot.Nodes.Any(node => IsAnyFactNode(node)))
            {
                unknowns.Add(new FactUnknownDto(emptyField, emptyReason));
            }

            return new FactQueryContext(scope, snapshotMetadata, warnings, unknowns);
        }

        /// <summary>
        /// Builds all data-access fact rows from the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <returns>The complete unfiltered data-access rows for the snapshot.</returns>
        private static DataAccessFactDto[] BuildDataAccessFacts(ExtractedArchitectureSnapshot snapshot)
        {
            // Data-access nodes are authoritative while graph edges enrich entity, table, procedure, operation, usage-site, and evidence detail.
            return snapshot.Nodes
                .Where(static node => IsDataAccessNode(node))
                .Select(node => BuildDataAccessFact(snapshot, node))
                .ToArray();
        }

        /// <summary>
        /// Builds one data-access fact row from a graph node and its relationships.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The data-access node being mapped.</param>
        /// <returns>The mapped data-access fact row.</returns>
        private static DataAccessFactDto BuildDataAccessFact(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node)
        {
            // Related nodes identify mapped entities, tables, procedures, and usage sites without requiring arbitrary traversal.
            ArchitectureEdge[] relatedEdges = RelatedEdges(snapshot, node.StableKey.Value).ToArray();
            ArchitectureNode[] relatedNodes = RelatedNodes(snapshot, relatedEdges).ToArray();
            string[] operations = relatedEdges.Select(edge => DataAccessOperation(edge.EdgeKind, edge.Metadata)).Where(static value => value is not null).Select(static value => value!).Concat(MetadataStrings(node.Metadata, "operation", "operations", "method", "methods")).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return new DataAccessFactDto(
                node.StableKey.Value,
                NormalizeDataAccessFamily(node),
                node.DisplayName,
                node.ProjectStableKey?.Value,
                SelectStableKey(node, relatedNodes, NodeKind.DbContext, NodeKind.LinqToSqlDataContext),
                SelectStableKey(node, relatedNodes, NodeKind.Entity),
                SelectStableKey(node, relatedNodes, NodeKind.DatabaseTable),
                SelectStableKey(node, relatedNodes, NodeKind.StoredProcedure),
                BuildUsageSites(relatedNodes),
                operations,
                BuildEvidenceStableKeys(node, relatedEdges),
                node.Confidence.Value,
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason,
                PublicMetadataSanitizer.Sanitize(node.Metadata));
        }

        /// <summary>
        /// Builds all configuration usage rows from the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <returns>The complete unfiltered configuration rows for the snapshot.</returns>
        private static ConfigurationUsageDto[] BuildConfigurationUsage(ExtractedArchitectureSnapshot snapshot)
        {
            // Configuration key nodes expose key names and relationships only; values are never read from metadata into public DTOs.
            return snapshot.Nodes
                .Where(static node => node.NodeKind == NodeKind.ConfigurationKey)
                .Select(node => BuildConfigurationUsage(snapshot, node))
                .ToArray();
        }

        /// <summary>
        /// Builds one configuration usage row from a graph node and its relationships.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The configuration key node being mapped.</param>
        /// <returns>The mapped configuration usage row.</returns>
        private static ConfigurationUsageDto BuildConfigurationUsage(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node)
        {
            // Consumers come from direct graph relationships while provider and environment hints come from sanitized metadata and evidence.
            ArchitectureEdge[] relatedEdges = RelatedEdges(snapshot, node.StableKey.Value).ToArray();
            string key = MetadataString(node.Metadata, "configurationKey") ?? MetadataString(node.Metadata, "key") ?? node.DisplayName;
            string[] sourceFiles = BuildEvidenceReferences(snapshot, BuildEvidenceStableKeys(node, relatedEdges)).Select(static evidence => evidence.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return new ConfigurationUsageDto(
                node.StableKey.Value,
                key,
                node.ProjectStableKey?.Value,
                RelatedNodes(snapshot, relatedEdges).Where(static related => related.NodeKind != NodeKind.ConfigurationKey).Select(static related => related.StableKey.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                MetadataStrings(node.Metadata, "provider", "providers", "configurationProvider"),
                MetadataString(node.Metadata, "environment"),
                sourceFiles,
                HasAnyMetadataName(node.Metadata, "value", "defaultValue", "resolvedValue"),
                IsSecretLike(key),
                BuildEvidenceStableKeys(node, relatedEdges),
                node.Confidence.Value,
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason,
                PublicMetadataSanitizer.Sanitize(node.Metadata));
        }

        /// <summary>
        /// Builds all external integration rows from the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <returns>The complete unfiltered integration rows for the snapshot.</returns>
        private static IntegrationFactDto[] BuildIntegrationFacts(ExtractedArchitectureSnapshot snapshot)
        {
            // External services, queues, topics, OpenAPI documents, and endpoint-like imported targets are treated as integration facts.
            return snapshot.Nodes
                .Where(static node => IsIntegrationNode(node))
                .Select(node => BuildIntegrationFact(snapshot, node))
                .Where(static item => !string.IsNullOrWhiteSpace(item.EndpointHost))
                .ToArray();
        }

        /// <summary>
        /// Builds one external integration row from a graph node and its relationships.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The integration node being mapped.</param>
        /// <returns>The mapped integration fact row.</returns>
        private static IntegrationFactDto BuildIntegrationFact(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node)
        {
            // Safe target metadata is reduced to a host or service name so URLs with credentials, paths, or query strings are not exposed.
            ArchitectureEdge[] relatedEdges = RelatedEdges(snapshot, node.StableKey.Value).ToArray();
            ArchitectureNode[] relatedNodes = RelatedNodes(snapshot, relatedEdges).ToArray();
            return new IntegrationFactDto(
                node.StableKey.Value,
                node.DisplayName,
                MetadataString(node.Metadata, "integrationKind") ?? NormalizeIntegrationKind(node),
                node.ProjectStableKey?.Value,
                SafeHost(MetadataString(node.Metadata, "endpointHost") ?? MetadataString(node.Metadata, "host") ?? MetadataString(node.Metadata, "serviceName") ?? MetadataString(node.Metadata, "url") ?? node.DisplayName),
                MetadataString(node.Metadata, "protocol") ?? InferProtocol(node),
                MetadataString(node.Metadata, "clientType") ?? MetadataString(node.Metadata, "client"),
                BuildRelatedConfigurationKeys(relatedNodes),
                relatedNodes.Where(static related => related.NodeKind != NodeKind.ConfigurationKey).Select(static related => related.StableKey.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                BuildEvidenceStableKeys(node, relatedEdges),
                node.Confidence.Value,
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason,
                PublicMetadataSanitizer.Sanitize(node.Metadata));
        }

        /// <summary>
        /// Builds all backend UI-technology fact rows from the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <returns>The complete unfiltered UI-technology rows for the snapshot.</returns>
        private static UiTechnologyFactDto[] BuildUiTechnologyFacts(ExtractedArchitectureSnapshot snapshot)
        {
            // UI graph nodes are exposed as backend query data and never imply creation of frontend assets.
            return snapshot.Nodes
                .Where(static node => IsUiNode(node))
                .Select(node => BuildUiTechnologyFact(snapshot, node))
                .ToArray();
        }

        /// <summary>
        /// Builds one UI-technology fact row from a graph node and its relationships.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="node">The UI node being mapped.</param>
        /// <returns>The mapped UI-technology fact row.</returns>
        private static UiTechnologyFactDto BuildUiTechnologyFact(ExtractedArchitectureSnapshot snapshot, ArchitectureNode node)
        {
            // Technology names are inferred from explicit metadata first, then project metadata, then node kind for broadly useful filtering.
            ArchitectureEdge[] relatedEdges = RelatedEdges(snapshot, node.StableKey.Value).ToArray();
            ArchitectureNode? project = FindNode(snapshot, node.ProjectStableKey?.Value);
            return new UiTechnologyFactDto(
                node.StableKey.Value,
                MetadataString(node.Metadata, "uiTechnology") ?? MetadataString(node.Metadata, "technology") ?? MetadataString(project?.Metadata, "uiTechnology") ?? InferUiTechnology(node, project),
                node.NodeKind.Value,
                node.DisplayName,
                node.ProjectStableKey?.Value,
                MetadataString(node.Metadata, "route") ?? MetadataString(node.Metadata, "path") ?? MetadataString(node.Metadata, "viewPath"),
                RelatedNodes(snapshot, relatedEdges).Where(static related => !IsUiSelfReference(related)).Select(static related => related.StableKey.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                BuildEvidenceStableKeys(node, relatedEdges),
                node.Confidence.Value,
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason,
                PublicMetadataSanitizer.Sanitize(node.Metadata));
        }

        /// <summary>
        /// Creates a bounded page from an already ordered sequence.
        /// </summary>
        /// <typeparam name="TItem">The page item type.</typeparam>
        /// <param name="items">The ordered full result set.</param>
        /// <param name="skip">The number of records to skip.</param>
        /// <param name="take">The maximum number of records to return.</param>
        /// <returns>A bounded query page retaining total count metadata.</returns>
        private static PagedQueryResult<TItem> ToPage<TItem>(IReadOnlyList<TItem> items, int skip, int take)
        {
            // The page is materialized after filtering and deterministic ordering so repeated calls have stable slices.
            return new PagedQueryResult<TItem>(items.Skip(skip).Take(take).ToArray(), items.Count, skip, take);
        }

        /// <summary>
        /// Merges one additional unknown into an existing context.
        /// </summary>
        /// <param name="context">The base fact query context.</param>
        /// <param name="unknown">The unknown value to append when not already present.</param>
        /// <returns>A context containing the additional unknown value.</returns>
        private static FactQueryContext MergeUnknownContext(FactQueryContext context, FactUnknownDto unknown)
        {
            // Unknown aggregation de-duplicates by field and reason so clients receive compact metadata.
            FactUnknownDto[] unknowns = context.Unknowns.Append(unknown).DistinctBy(static value => value.Field + "\u001f" + value.Reason).ToArray();
            return new FactQueryContext(context.Scope, context.Snapshot, context.Warnings, unknowns);
        }

        /// <summary>
        /// Resolves all edges directly connected to a stable node key.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="stableKey">The stable node key whose incident edges should be found.</param>
        /// <returns>The directly related edges.</returns>
        private static IEnumerable<ArchitectureEdge> RelatedEdges(ExtractedArchitectureSnapshot snapshot, string stableKey)
        {
            // Direct relationship lookup is bounded and avoids arbitrary graph traversal.
            return snapshot.Edges.Where(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, stableKey) || StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, stableKey));
        }

        /// <summary>
        /// Resolves nodes at either end of a related edge collection.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="edges">The related edges whose source and target nodes should be resolved.</param>
        /// <returns>The related nodes that matched edge endpoints.</returns>
        private static IEnumerable<ArchitectureNode> RelatedNodes(ExtractedArchitectureSnapshot snapshot, IEnumerable<ArchitectureEdge> edges)
        {
            // Related-node resolution uses stable keys only and skips missing nodes so partial graph extraction remains safe.
            HashSet<string> keys = new(edges.SelectMany(static edge => new[] { edge.SourceNodeStableKey.Value, edge.TargetNodeStableKey.Value }), StringComparer.Ordinal);
            return snapshot.Nodes.Where(node => keys.Contains(node.StableKey.Value));
        }

        /// <summary>
        /// Finds one node by stable key when a stable key is available.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="stableKey">The optional stable key to find.</param>
        /// <returns>The matching node, or null when no key or node is available.</returns>
        private static ArchitectureNode? FindNode(ExtractedArchitectureSnapshot snapshot, string? stableKey)
        {
            // Stable-key lookup avoids display-name ambiguity when enriching fact rows with project metadata.
            return string.IsNullOrWhiteSpace(stableKey)
                ? null
                : snapshot.Nodes.FirstOrDefault(node => StringComparer.Ordinal.Equals(node.StableKey.Value, stableKey));
        }

        /// <summary>
        /// Selects a stable key from the current node or related nodes when the kind matches.
        /// </summary>
        /// <param name="node">The current fact node.</param>
        /// <param name="relatedNodes">The nodes directly related to the current fact.</param>
        /// <param name="nodeKinds">The acceptable node kinds.</param>
        /// <returns>The first deterministic matching stable key, or null when none exists.</returns>
        private static string? SelectStableKey(ArchitectureNode node, IEnumerable<ArchitectureNode> relatedNodes, params NodeKind[] nodeKinds)
        {
            // The current node is considered first so entity/table/procedure facts identify themselves when appropriate.
            HashSet<string> allowedKinds = new(nodeKinds.Select(static kind => kind.Value), StringComparer.Ordinal);
            return new[] { node }
                .Concat(relatedNodes)
                .Where(candidate => allowedKinds.Contains(candidate.NodeKind.Value))
                .OrderBy(static candidate => candidate.StableKey.Value, StringComparer.Ordinal)
                .Select(static candidate => candidate.StableKey.Value)
                .FirstOrDefault();
        }

        /// <summary>
        /// Builds stable or display usage-site names from related graph nodes.
        /// </summary>
        /// <param name="relatedNodes">The nodes related to a data-access fact.</param>
        /// <returns>The deterministic usage-site identifiers.</returns>
        private static string[] BuildUsageSites(IEnumerable<ArchitectureNode> relatedNodes)
        {
            // Usage sites focus on code/runtime nodes rather than repeating database artifacts already represented in dedicated fields.
            return relatedNodes
                .Where(static node => !IsDataAccessNode(node) && node.NodeKind != NodeKind.ConfigurationKey)
                .Select(static node => node.StableKey.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Builds safe configuration key names from related nodes.
        /// </summary>
        /// <param name="relatedNodes">The nodes related to an integration fact.</param>
        /// <returns>The deterministic safe configuration key names.</returns>
        private static string[] BuildRelatedConfigurationKeys(IEnumerable<ArchitectureNode> relatedNodes)
        {
            // Configuration values are never surfaced; only the key name from related configuration-key nodes is returned.
            return relatedNodes
                .Where(static node => node.NodeKind == NodeKind.ConfigurationKey)
                .Select(static node => node.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Builds safe evidence references for selected stable evidence keys.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="evidenceStableKeys">The evidence stable keys to resolve.</param>
        /// <returns>The safe evidence references that matched the selected keys.</returns>
        private static FactEvidenceReferenceDto[] BuildEvidenceReferences(ExtractedArchitectureSnapshot snapshot, IEnumerable<string> evidenceStableKeys)
        {
            // Evidence references preserve source location and small previews but do not expand arbitrary source files.
            HashSet<string> keys = new(evidenceStableKeys.Where(static key => !string.IsNullOrWhiteSpace(key)), StringComparer.Ordinal);
            return snapshot.Evidence
                .Where(evidence => keys.Contains(evidence.StableKey.Value))
                .OrderBy(static evidence => evidence.StableKey.Value, StringComparer.Ordinal)
                .Select(static evidence => new FactEvidenceReferenceDto(
                    evidence.StableKey.Value,
                    evidence.EvidenceKind.Value,
                    evidence.FilePath.Value,
                    evidence.StartLine,
                    evidence.EndLine,
                    evidence.SymbolName,
                    evidence.ContainingSymbol,
                    evidence.SnippetHash,
                    BoundSnippet(evidence.SnippetPreview),
                    evidence.Confidence.Value))
                .ToArray();
        }

        /// <summary>
        /// Builds evidence stable keys from a node and related edges.
        /// </summary>
        /// <param name="node">The node whose primary evidence may be included.</param>
        /// <param name="edges">The related edges whose primary evidence may be included.</param>
        /// <returns>The stable evidence keys associated with the node and edges.</returns>
        private static string[] BuildEvidenceStableKeys(ArchitectureNode node, IEnumerable<ArchitectureEdge> edges)
        {
            // Evidence stable-key aggregation removes duplicates so response rows remain compact and deterministic.
            return new[] { node.PrimaryEvidenceStableKey?.Value }
                .Concat(edges.Select(static edge => edge.PrimaryEvidenceStableKey?.Value))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Determines whether a node belongs to any fact family covered by this service.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <returns><see langword="true"/> when the node is a queryable fact node; otherwise, <see langword="false"/>.</returns>
        private static bool IsAnyFactNode(ArchitectureNode node)
        {
            // The context-level unknown check uses broad fact coverage to avoid claiming total absence when any supported family exists.
            return IsDataAccessNode(node) || node.NodeKind == NodeKind.ConfigurationKey || IsIntegrationNode(node) || IsUiNode(node);
        }

        /// <summary>
        /// Determines whether a node is a data-access concept.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <returns><see langword="true"/> when the node is a data-access fact; otherwise, <see langword="false"/>.</returns>
        private static bool IsDataAccessNode(ArchitectureNode node)
        {
            // Data-access concepts are normalized by node kind and may be further classified by extractor metadata.
            return node.NodeKind == NodeKind.DbContext
                || node.NodeKind == NodeKind.LinqToSqlDataContext
                || node.NodeKind == NodeKind.Entity
                || node.NodeKind == NodeKind.DatabaseTable
                || node.NodeKind == NodeKind.StoredProcedure
                || node.NodeKind == NodeKind.SqlScript
                || HasMetadataValue(node.Metadata, "dataAccessFamily", "AdoNet")
                || HasMetadataValue(node.Metadata, "dataAccessFamily", "TypedDataSet")
                || HasMetadataValue(node.Metadata, "dataAccessFamily", "RawSql");
        }

        /// <summary>
        /// Determines whether a node is an external integration concept.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <returns><see langword="true"/> when the node is an integration fact; otherwise, <see langword="false"/>.</returns>
        private static bool IsIntegrationNode(ArchitectureNode node)
        {
            // Integration facts include explicit external service nodes and messaging/API artifacts that represent external boundaries.
            return node.NodeKind == NodeKind.ExternalService
                || node.NodeKind == NodeKind.Queue
                || node.NodeKind == NodeKind.Topic
                || node.NodeKind == NodeKind.OpenApiDocument
                || HasMetadataName(node.Metadata, "integrationKind")
                || HasMetadataName(node.Metadata, "endpointHost")
                || HasMetadataName(node.Metadata, "serviceName");
        }

        /// <summary>
        /// Determines whether a node is a backend UI-technology concept.
        /// </summary>
        /// <param name="node">The architecture node to inspect.</param>
        /// <returns><see langword="true"/> when the node is a UI-technology fact; otherwise, <see langword="false"/>.</returns>
        private static bool IsUiNode(ArchitectureNode node)
        {
            // UI query facts are backend graph facts and deliberately exclude any host UI implementation concern.
            return node.NodeKind == NodeKind.UiApplication
                || node.NodeKind == NodeKind.UiComponent
                || node.NodeKind == NodeKind.UiPage
                || node.NodeKind == NodeKind.UiView
                || node.NodeKind == NodeKind.UiLayout
                || node.NodeKind == NodeKind.UiRoute
                || node.NodeKind == NodeKind.UiControl
                || node.NodeKind == NodeKind.UiResource
                || node.NodeKind == NodeKind.UiStyle
                || node.NodeKind == NodeKind.ViewModel
                || node.NodeKind == NodeKind.Command
                || node.NodeKind == NodeKind.Binding;
        }

        /// <summary>
        /// Determines whether a related node should be removed from UI self-reference output.
        /// </summary>
        /// <param name="node">The related node to inspect.</param>
        /// <returns><see langword="true"/> for repository/solution nodes that do not help UI fact consumers; otherwise, <see langword="false"/>.</returns>
        private static bool IsUiSelfReference(ArchitectureNode node)
        {
            // Repository and solution nodes are scope rather than actionable UI relationships in this response family.
            return node.NodeKind == NodeKind.Repository || node.NodeKind == NodeKind.Solution;
        }

        /// <summary>
        /// Normalizes a data-access family filter into the canonical public family name.
        /// </summary>
        /// <param name="family">The family filter supplied by the caller.</param>
        /// <returns>The canonical public data-access family name.</returns>
        private static string NormalizeDataAccessFamilyFilter(string? family)
        {
            // Aliases help callers use common terms while responses remain deterministic.
            return family?.Trim().ToLowerInvariant() switch
            {
                "efclassic" => "EFClassic",
                "ef6" => "EF6",
                "efcore" => "EFCore",
                "linqtosql" => "LinqToSql",
                "adonet" => "AdoNet",
                "typeddataset" => "TypedDataSet",
                "rawsql" => "RawSql",
                "storedprocedure" => "StoredProcedure",
                "entity" => "Entity",
                "table" => "Table",
                _ => family?.Trim() ?? string.Empty
            };
        }

        /// <summary>
        /// Normalizes a data-access node into the canonical public family name.
        /// </summary>
        /// <param name="node">The data-access node to classify.</param>
        /// <returns>The canonical public data-access family name.</returns>
        private static string NormalizeDataAccessFamily(ArchitectureNode node)
        {
            // Extractor metadata wins when it distinguishes EF Core from EF6 or ADO.NET from typed DataSet.
            string? metadataFamily = MetadataString(node.Metadata, "dataAccessFamily") ?? MetadataString(node.Metadata, "family") ?? MetadataString(node.Metadata, "orm");
            if (!string.IsNullOrWhiteSpace(metadataFamily))
            {
                return NormalizeDataAccessFamilyFilter(metadataFamily);
            }

            if (node.NodeKind == NodeKind.LinqToSqlDataContext)
            {
                return "LinqToSql";
            }

            if (node.NodeKind == NodeKind.DbContext)
            {
                return HasMetadataValue(node.Metadata, "framework", "EF6") || HasMetadataValue(node.Metadata, "framework", "EntityFramework") ? "EF6" : "EFCore";
            }

            if (node.NodeKind == NodeKind.StoredProcedure)
            {
                return "StoredProcedure";
            }

            if (node.NodeKind == NodeKind.Entity)
            {
                return "Entity";
            }

            if (node.NodeKind == NodeKind.DatabaseTable)
            {
                return "Table";
            }

            return node.NodeKind == NodeKind.SqlScript ? "RawSql" : node.NodeKind.Value;
        }

        /// <summary>
        /// Infers a safe operation name from an edge kind and metadata.
        /// </summary>
        /// <param name="edgeKind">The relationship kind connected to a data-access fact.</param>
        /// <param name="metadata">The edge metadata that may contain extractor operation hints.</param>
        /// <returns>The safe operation name, or null when the edge does not imply one.</returns>
        private static string? DataAccessOperation(EdgeKind edgeKind, GraphMetadata metadata)
        {
            // Operation names are controlled hints rather than source text; metadata allows extractor-specific method names to flow safely.
            return MetadataString(metadata, "operation") ?? MetadataString(metadata, "method") ?? edgeKind.Value switch
            {
                "USES_DB_CONTEXT" => "UsesDbContext",
                "USES_LINQ_TO_SQL_CONTEXT" => "UsesLinqToSqlContext",
                "MAPS_ENTITY" => "MapsEntity",
                "MAPS_TABLE" => "MapsTable",
                "READS_TABLE" => "Read",
                "WRITES_TABLE" => "Write",
                "CALLS_STORED_PROCEDURE" => "StoredProcedure",
                "EXECUTES_RAW_SQL" => "RawSql",
                _ => null
            };
        }

        /// <summary>
        /// Normalizes an integration node into a canonical public integration kind.
        /// </summary>
        /// <param name="node">The integration node to classify.</param>
        /// <returns>The canonical public integration kind.</returns>
        private static string NormalizeIntegrationKind(ArchitectureNode node)
        {
            // Node kinds provide safe fallback categories when extractor metadata is absent.
            if (node.NodeKind == NodeKind.Queue)
            {
                return "Queue";
            }

            if (node.NodeKind == NodeKind.Topic)
            {
                return "Topic";
            }

            if (node.NodeKind == NodeKind.OpenApiDocument)
            {
                return "OpenApi";
            }

            return node.NodeKind == NodeKind.ExternalService ? "ExternalService" : node.NodeKind.Value;
        }

        /// <summary>
        /// Infers a protocol hint from integration metadata and node kind.
        /// </summary>
        /// <param name="node">The integration node to inspect.</param>
        /// <returns>The safe protocol hint when one can be inferred.</returns>
        private static string? InferProtocol(ArchitectureNode node)
        {
            // Protocol inference is intentionally conservative so responses do not invent endpoint details.
            if (node.NodeKind == NodeKind.Queue || node.NodeKind == NodeKind.Topic)
            {
                return MetadataString(node.Metadata, "transportKind") ?? MetadataString(node.Metadata, "transport");
            }

            return node.NodeKind == NodeKind.OpenApiDocument || node.NodeKind == NodeKind.ExternalService ? "HTTP" : null;
        }

        /// <summary>
        /// Infers a UI technology name from node and project metadata.
        /// </summary>
        /// <param name="node">The UI node to classify.</param>
        /// <param name="project">The optional owning project node.</param>
        /// <returns>The inferred public UI technology name.</returns>
        private static string InferUiTechnology(ArchitectureNode node, ArchitectureNode? project)
        {
            // Project and qualified-name hints cover Blazor, Razor, WinForms, WPF, WinUI, MAUI, and Avalonia without adding new domain kinds.
            string haystack = string.Join(' ', node.StableKey.Value, node.QualifiedName, node.DisplayName, project?.StableKey.Value, project?.DisplayName, MetadataString(project?.Metadata, "application.type"));
            if (haystack.Contains("blazor", StringComparison.OrdinalIgnoreCase))
            {
                return "Blazor";
            }

            if (haystack.Contains("razor", StringComparison.OrdinalIgnoreCase))
            {
                return "Razor";
            }

            if (haystack.Contains("winforms", StringComparison.OrdinalIgnoreCase) || haystack.Contains("windows forms", StringComparison.OrdinalIgnoreCase))
            {
                return "Windows Forms";
            }

            if (haystack.Contains("winui", StringComparison.OrdinalIgnoreCase))
            {
                return "WinUI";
            }

            if (haystack.Contains("maui", StringComparison.OrdinalIgnoreCase))
            {
                return ".NET MAUI";
            }

            if (haystack.Contains("avalonia", StringComparison.OrdinalIgnoreCase))
            {
                return "Avalonia";
            }

            return haystack.Contains("wpf", StringComparison.OrdinalIgnoreCase) ? "WPF" : "Unknown UI Technology";
        }

        /// <summary>
        /// Reduces a potentially unsafe endpoint value to a host or service name.
        /// </summary>
        /// <param name="value">The candidate endpoint, URL, host, or service value.</param>
        /// <returns>A safe host or service value with credentials, paths, and query strings removed.</returns>
        private static string? SafeHost(string? value)
        {
            // URL parsing strips credentials and query strings before any integration target appears in public JSON.
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmed = value.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
            {
                return uri.Host;
            }

            int separatorIndex = trimmed.IndexOfAny(['/', '?', '#']);
            string host = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
            if (host.Contains('@', StringComparison.Ordinal))
            {
                return null;
            }

            return IsSecretLike(host) ? null : host;
        }

        /// <summary>
        /// Determines whether a configuration key or value appears secret-like.
        /// </summary>
        /// <param name="value">The candidate key, host, or metadata value.</param>
        /// <returns><see langword="true"/> when the value should be treated as secret-like; otherwise, <see langword="false"/>.</returns>
        private static bool IsSecretLike(string? value)
        {
            // Secret-like values are never echoed from metadata into public responses.
            return !string.IsNullOrWhiteSpace(value)
                && (value.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("password", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("credential", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("connectionstring", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("connection string", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Bounds a snippet preview to the common fact-query display limit.
        /// </summary>
        /// <param name="snippet">The optional snippet text to bound.</param>
        /// <returns>The bounded snippet preview, or null when no snippet is available.</returns>
        private static string? BoundSnippet(string? snippet)
        {
            // Snippet previews are untrusted display text and are kept small until the evidence drill-down slice owns richer source expansion.
            if (string.IsNullOrEmpty(snippet))
            {
                return snippet;
            }

            const int maximumSnippetPreviewLength = 160;
            return snippet.Length <= maximumSnippetPreviewLength ? snippet : snippet[..maximumSnippetPreviewLength];
        }

        /// <summary>
        /// Determines whether nullable text contains a caller-supplied filter using ordinal-ignore-case comparison.
        /// </summary>
        /// <param name="value">The nullable source text.</param>
        /// <param name="filter">The nullable filter text.</param>
        /// <returns><see langword="true"/> when the filter is blank or the value contains it; otherwise, <see langword="false"/>.</returns>
        private static bool Contains(string? value, string? filter)
        {
            // Contains matching is reserved for explicitly searchable display fields and never interprets raw query syntax.
            return string.IsNullOrWhiteSpace(filter) || (!string.IsNullOrWhiteSpace(value) && value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Determines whether metadata contains a property with a matching scalar value.
        /// </summary>
        /// <param name="metadata">The metadata to inspect.</param>
        /// <param name="name">The metadata property name.</param>
        /// <param name="expectedValue">The expected scalar value.</param>
        /// <returns><see langword="true"/> when the property value matches; otherwise, <see langword="false"/>.</returns>
        private static bool HasMetadataValue(GraphMetadata metadata, string name, string expectedValue)
        {
            // Metadata value checks tolerate scalar and array metadata because extractors may emit either shape.
            return MetadataStrings(metadata, name).Any(value => string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Determines whether metadata contains any of the supplied property names.
        /// </summary>
        /// <param name="metadata">The metadata to inspect.</param>
        /// <param name="names">The metadata property names.</param>
        /// <returns><see langword="true"/> when at least one property exists; otherwise, <see langword="false"/>.</returns>
        private static bool HasAnyMetadataName(GraphMetadata metadata, params string[] names)
        {
            // Presence checks let the service report value availability without exposing the value itself.
            return names.Any(name => HasMetadataName(metadata, name));
        }

        /// <summary>
        /// Determines whether metadata contains a property with the supplied name.
        /// </summary>
        /// <param name="metadata">The metadata to inspect.</param>
        /// <param name="name">The metadata property name.</param>
        /// <returns><see langword="true"/> when the property exists; otherwise, <see langword="false"/>.</returns>
        private static bool HasMetadataName(GraphMetadata metadata, string name)
        {
            // Metadata is parsed only within bounded in-memory rows, keeping the helper simple and deterministic.
            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(name, out _);
        }

        /// <summary>
        /// Reads the first scalar metadata value associated with any of the supplied names.
        /// </summary>
        /// <param name="metadata">The optional metadata to inspect.</param>
        /// <param name="names">The candidate metadata property names.</param>
        /// <returns>The first scalar value, or null when no scalar value exists.</returns>
        private static string? MetadataString(GraphMetadata? metadata, params string[] names)
        {
            // Metadata parsing is centralized so callers can support equivalent extractor key names without duplicating JSON handling.
            if (metadata is null)
            {
                return null;
            }

            foreach (string name in names)
            {
                string? value = MetadataStrings(metadata, name).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads scalar metadata values associated with any of the supplied names.
        /// </summary>
        /// <param name="metadata">The metadata to inspect.</param>
        /// <param name="names">The candidate metadata property names.</param>
        /// <returns>The deterministic scalar metadata values.</returns>
        private static string[] MetadataStrings(GraphMetadata metadata, params string[] names)
        {
            // Arrays and scalars are normalized into one sequence so filters do not depend on extractor serialization choices.
            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            List<string> values = [];
            foreach (string name in names)
            {
                if (!document.RootElement.TryGetProperty(name, out JsonElement element))
                {
                    continue;
                }

                AddMetadataElementValues(element, values);
            }

            return values.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// Adds scalar JSON element values to a caller-owned collection.
        /// </summary>
        /// <param name="element">The JSON element to normalize.</param>
        /// <param name="values">The collection receiving scalar values.</param>
        private static void AddMetadataElementValues(JsonElement element, ICollection<string> values)
        {
            // Only primitive values are surfaced; nested objects are ignored because their public shape is not part of the controlled contract.
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    values.Add(element.GetString() ?? string.Empty);
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    values.Add(element.ToString());
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        AddMetadataElementValues(item, values);
                    }

                    break;
            }
        }

        /// <summary>
        /// Stores the result of snapshot selector resolution.
        /// </summary>
        /// <param name="Snapshot">The selected snapshot when resolution succeeded.</param>
        /// <param name="ValidationErrors">The validation errors emitted when resolution failed.</param>
        private sealed record SnapshotResolution(ExtractedArchitectureSnapshot? Snapshot, IReadOnlyList<FactQueryValidationError> ValidationErrors)
        {
            /// <summary>
            /// Gets a value indicating whether snapshot resolution succeeded.
            /// </summary>
            public bool Succeeded => ValidationErrors.Count == 0;

            /// <summary>
            /// Creates a successful snapshot resolution.
            /// </summary>
            /// <param name="snapshot">The selected snapshot.</param>
            /// <param name="scopedSnapshots">The repository and solution scoped snapshots retained for signature consistency with earlier slices.</param>
            /// <returns>A successful snapshot resolution.</returns>
            public static SnapshotResolution Success(ExtractedArchitectureSnapshot snapshot, IReadOnlyList<ExtractedArchitectureSnapshot> scopedSnapshots)
            {
                // The scoped snapshot argument keeps the factory aligned with earlier WP014 services even though this slice only needs the selected snapshot.
                _ = scopedSnapshots;
                return new SnapshotResolution(snapshot, []);
            }

            /// <summary>
            /// Creates a failed snapshot resolution.
            /// </summary>
            /// <param name="validationErrors">The validation errors that explain the failure.</param>
            /// <returns>A failed snapshot resolution.</returns>
            public static SnapshotResolution Failed(IEnumerable<FactQueryValidationError> validationErrors)
            {
                // Failed resolution carries deterministic validation errors and no selected snapshot.
                return new SnapshotResolution(null, validationErrors.ToArray());
            }
        }
    }
}
