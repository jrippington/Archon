using System.Text.Json;
using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Rules;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Projects
{
    /// <summary>
    /// Implements controlled project catalogue and project detail query behavior over persisted architecture snapshots.
    /// </summary>
    public sealed class ProjectQueryService : IProjectQueryService
    {
        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public ProjectQueryService(IArchitectureSnapshotWriter snapshotWriter)
        {
            // The service reads application snapshot contracts and intentionally avoids Neo4j IDs or database-local labels.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <inheritdoc />
        public Task<ProjectCatalogueResult> ListProjectsAsync(ProjectCatalogueQuery query, CancellationToken cancellationToken)
        {
            // Catalogue query flow validates scope, resolves one snapshot, derives project rows, then applies controlled filters and ordering.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new ProjectCatalogueResult(resolution.ValidationErrors));
            }

            ProjectQueryContext context = BuildContext(query.Selector, resolution);
            ProjectCatalogueItemDto[] allItems = BuildCatalogueItems(resolution.Snapshot!, context.Unknowns);
            ProjectCatalogueItemDto[] filtered = ApplyCatalogueFilters(allItems, query).ToArray();
            ProjectCatalogueItemDto[] ordered = ApplyCatalogueOrdering(filtered, query).ToArray();
            ProjectCatalogueItemDto[] pageItems = ordered.Skip(query.Skip).Take(query.Take).ToArray();
            PagedQueryResult<ProjectCatalogueItemDto> page = new(pageItems, ordered.Length, query.Skip, query.Take);
            return Task.FromResult(new ProjectCatalogueResult(page, context));
        }

        /// <inheritdoc />
        public Task<ProjectDetailResult> GetProjectAsync(ProjectDetailQuery query, CancellationToken cancellationToken)
        {
            // Detail lookup shares snapshot resolution with catalogue queries and then enforces stable-key or unambiguous-name selection.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            List<ProjectQueryValidationError> identityErrors = ValidateProjectIdentity(query);
            if (identityErrors.Count > 0)
            {
                return Task.FromResult(new ProjectDetailResult(identityErrors));
            }

            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new ProjectDetailResult(resolution.ValidationErrors));
            }

            ProjectQueryContext context = BuildContext(query.Selector, resolution);
            ProjectCatalogueItemDto[] allItems = BuildCatalogueItems(resolution.Snapshot!, context.Unknowns);
            ProjectCatalogueItemDto[] matches = ResolveProjectMatches(allItems, query);
            if (matches.Length == 0)
            {
                ProjectQueryValidationError error = new(ProjectQueryValidationCodes.ProjectNotFound, "The requested project was not found in the selected snapshot scope.");
                return Task.FromResult(new ProjectDetailResult([error]));
            }

            if (matches.Length > 1)
            {
                ProjectQueryValidationError error = new(ProjectQueryValidationCodes.ProjectNameAmbiguous, "The requested project name matches multiple projects; use a project stable key to disambiguate.");
                return Task.FromResult(new ProjectDetailResult([error], matches));
            }

            ProjectDetailDto detail = BuildProjectDetail(resolution.Snapshot!, matches[0], context);
            return Task.FromResult(new ProjectDetailResult(detail, context));
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
        /// Resolves and validates the selected snapshot scope.
        /// </summary>
        /// <param name="selector">The repository, solution, and snapshot selector supplied by the query.</param>
        /// <returns>A successful snapshot resolution or deterministic validation errors.</returns>
        private SnapshotResolution ResolveSnapshot(ProjectSnapshotSelector selector)
        {
            // Scope validation runs before graph matching so missing or malformed selectors produce client-correctable problem details.
            List<ProjectQueryValidationError> validationErrors = ValidateSelector(selector);
            if (validationErrors.Count > 0)
            {
                return SnapshotResolution.Failed(validationErrors);
            }

            ExtractedArchitectureSnapshot[] repositorySnapshots = GetSnapshots()
                .Where(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.RepositoryStableKey.Value, selector.RepositoryStableKey))
                .ToArray();
            if (repositorySnapshots.Length == 0)
            {
                ProjectQueryValidationError error = new(ProjectQueryValidationCodes.RepositoryNotFound, "The requested repository scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot[] scopedSnapshots = ApplySolutionScope(repositorySnapshots, selector);
            if (selector.SolutionStableKey is not null && scopedSnapshots.Length == 0)
            {
                ProjectQueryValidationError error = new(ProjectQueryValidationCodes.SolutionNotFound, "The requested solution scope was not found for the repository.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot? selectedSnapshot = ResolveSelectedSnapshot(scopedSnapshots, selector);
            if (selectedSnapshot?.SnapshotHeader is null)
            {
                ProjectQueryValidationError error = new(ProjectQueryValidationCodes.SnapshotNotFound, "The requested snapshot scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            return SnapshotResolution.Success(selectedSnapshot, scopedSnapshots);
        }

        /// <summary>
        /// Validates selector syntax before any snapshot matching occurs.
        /// </summary>
        /// <param name="selector">The caller-supplied project snapshot selector.</param>
        /// <returns>A deterministic list of syntax validation errors.</returns>
        private static List<ProjectQueryValidationError> ValidateSelector(ProjectSnapshotSelector selector)
        {
            // Repository scope is required because latest resolution must be bounded to one repository.
            List<ProjectQueryValidationError> errors = [];
            if (selector.RepositoryStableKey is null)
            {
                errors.Add(new ProjectQueryValidationError(ProjectQueryValidationCodes.RepositoryStableKeyRequired, "A repository stable key is required for project queries."));
            }

            if (!selector.RequestsLatestSnapshot && !selector.SnapshotStableKey.StartsWith("snapshot://", StringComparison.Ordinal))
            {
                errors.Add(new ProjectQueryValidationError(ProjectQueryValidationCodes.SnapshotSelectorInvalid, "Snapshot selector must be 'latest', 'current', or a snapshot:// stable key."));
            }

            return errors;
        }

        /// <summary>
        /// Validates detail lookup identity fields before snapshot work starts.
        /// </summary>
        /// <param name="query">The project detail query supplied by the caller.</param>
        /// <returns>A deterministic list of identity validation errors.</returns>
        private static List<ProjectQueryValidationError> ValidateProjectIdentity(ProjectDetailQuery query)
        {
            // Detail lookup must be explicit because choosing between stable key and display name implicitly could return the wrong project.
            List<ProjectQueryValidationError> errors = [];
            if (query.ProjectStableKey is null && query.ProjectName is null)
            {
                errors.Add(new ProjectQueryValidationError(ProjectQueryValidationCodes.ProjectIdentityRequired, "A project stable key or project name is required for project detail."));
            }

            if (query.ProjectStableKey is not null && query.ProjectName is not null)
            {
                errors.Add(new ProjectQueryValidationError(ProjectQueryValidationCodes.ProjectIdentityAmbiguous, "Use either project stable key or project name for project detail, not both."));
            }

            return errors;
        }

        /// <summary>
        /// Applies the optional solution scope to repository snapshots.
        /// </summary>
        /// <param name="repositorySnapshots">The snapshots already matched to the requested repository.</param>
        /// <param name="selector">The caller-supplied project snapshot selector.</param>
        /// <returns>The snapshots matching the optional solution scope.</returns>
        private static ExtractedArchitectureSnapshot[] ApplySolutionScope(IEnumerable<ExtractedArchitectureSnapshot> repositorySnapshots, ProjectSnapshotSelector selector)
        {
            // Project nodes may not always carry direct solution membership, so solution scope is resolved through snapshot-level solution facts.
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
        /// <param name="selector">The caller-supplied project snapshot selector.</param>
        /// <returns>The selected snapshot, or null when none matches.</returns>
        private static ExtractedArchitectureSnapshot? ResolveSelectedSnapshot(IEnumerable<ExtractedArchitectureSnapshot> scopedSnapshots, ProjectSnapshotSelector selector)
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
        /// Builds the project query context shared by catalogue and detail responses.
        /// </summary>
        /// <param name="selector">The caller-supplied project snapshot selector.</param>
        /// <param name="resolution">The successful snapshot resolution.</param>
        /// <returns>The project query context for response mapping.</returns>
        private static ProjectQueryContext BuildContext(ProjectSnapshotSelector selector, SnapshotResolution resolution)
        {
            // Context construction centralizes envelope metadata so catalogue and detail endpoints report scope consistently.
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
            List<ProjectWarningDto> warnings = [];
            List<ProjectUnknownDto> unknowns = [];
            AddSnapshotDiagnostics(snapshot, warnings);
            AddProjectSectionUnknowns(snapshot, unknowns);
            return new ProjectQueryContext(scope, snapshotMetadata, warnings, unknowns);
        }

        /// <summary>
        /// Builds project catalogue rows from the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="contextUnknowns">The query-level unknowns used to include missing optional project sections.</param>
        /// <returns>The complete unfiltered catalogue rows for the snapshot.</returns>
        private static ProjectCatalogueItemDto[] BuildCatalogueItems(ExtractedArchitectureSnapshot snapshot, IReadOnlyList<ProjectUnknownDto> contextUnknowns)
        {
            // Project nodes are the authoritative catalogue source; other graph facts provide aggregate counts and risk indicators.
            ArchitectureNode[] projectNodes = snapshot.Nodes
                .Where(static node => node.NodeKind == NodeKind.Project)
                .OrderBy(static node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static node => node.StableKey.Value, StringComparer.Ordinal)
                .ToArray();
            return projectNodes.Select(project => BuildCatalogueItem(snapshot, project, contextUnknowns)).ToArray();
        }

        /// <summary>
        /// Builds one project catalogue row from a project node and related snapshot facts.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="project">The project node being mapped.</param>
        /// <param name="contextUnknowns">The query-level unknowns used to include missing optional project sections.</param>
        /// <returns>The mapped catalogue item.</returns>
        private static ProjectCatalogueItemDto BuildCatalogueItem(ExtractedArchitectureSnapshot snapshot, ArchitectureNode project, IReadOnlyList<ProjectUnknownDto> contextUnknowns)
        {
            // Counts are derived from stable node and edge keys so public responses remain independent from graph database implementation details.
            string projectStableKey = project.StableKey.Value;
            int dependencyCount = snapshot.Edges.Count(edge => IsProjectDependency(edge) && StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, projectStableKey));
            int dependentCount = snapshot.Edges.Count(edge => IsProjectDependency(edge) && StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, projectStableKey));
            int packageCount = snapshot.Edges.Count(edge => edge.EdgeKind == EdgeKind.UsesPackage && StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, projectStableKey));
            int endpointCount = snapshot.Nodes.Count(node => node.NodeKind == NodeKind.Endpoint && StringComparer.Ordinal.Equals(node.ProjectStableKey?.Value, projectStableKey));
            string[] dataAccessIndicators = BuildDataAccessIndicators(snapshot, projectStableKey);
            FindingRecord[] findings = FindProjectFindings(snapshot, projectStableKey);
            ProjectRiskIndicatorsDto risk = BuildRiskIndicators(project, findings);
            List<string> evidenceStableKeys = [];
            AddIfPresent(evidenceStableKeys, project.PrimaryEvidenceStableKey?.Value);
            evidenceStableKeys.AddRange(snapshot.Edges.Where(edge => IsProjectRelated(edge, projectStableKey)).Select(edge => edge.PrimaryEvidenceStableKey?.Value).Where(static value => value is not null)!);
            evidenceStableKeys.AddRange(findings.SelectMany(static finding => finding.EvidenceStableKeys.Select(static key => key.Value)));
            bool hasUnknownData = project.UnknownState.HasUnknownData || contextUnknowns.Count > 0;
            string? unknownReason = project.UnknownState.UnknownReason ?? contextUnknowns.FirstOrDefault()?.Reason;
            return new ProjectCatalogueItemDto(
                projectStableKey,
                project.DisplayName,
                MetadataString(project.Metadata, "path") ?? MetadataString(project.Metadata, "project.path"),
                project.Language,
                MetadataString(project.Metadata, "project.type") ?? MetadataString(project.Metadata, "architecture.layer"),
                MetadataString(project.Metadata, "targetFramework"),
                MetadataBool(project.Metadata, "isSdkStyle"),
                dependencyCount,
                dependentCount,
                packageCount,
                endpointCount,
                dataAccessIndicators,
                findings.Length,
                risk,
                evidenceStableKeys,
                project.Confidence.Value,
                hasUnknownData,
                unknownReason);
        }

        /// <summary>
        /// Applies controlled catalogue filters to project rows.
        /// </summary>
        /// <param name="items">The complete catalogue rows.</param>
        /// <param name="query">The normalized catalogue query.</param>
        /// <returns>The filtered project rows.</returns>
        private static IEnumerable<ProjectCatalogueItemDto> ApplyCatalogueFilters(IEnumerable<ProjectCatalogueItemDto> items, ProjectCatalogueQuery query)
        {
            // Filters are fixed and exact, except search, which is a bounded contains match over public identity fields.
            return items
                .Where(item => query.Search is null || Contains(item.Name, query.Search) || Contains(item.Path, query.Search) || Contains(item.StableKey, query.Search))
                .Where(item => query.Language is null || string.Equals(item.Language, query.Language, StringComparison.OrdinalIgnoreCase))
                .Where(item => query.ProjectType is null || string.Equals(item.ProjectType, query.ProjectType, StringComparison.OrdinalIgnoreCase))
                .Where(item => query.TargetFramework is null || string.Equals(item.TargetFramework, query.TargetFramework, StringComparison.OrdinalIgnoreCase))
                .Where(item => query.ApplicationType is null || string.Equals(MetadataApplicationType(item), query.ApplicationType, StringComparison.OrdinalIgnoreCase))
                .Where(item => query.HasDataAccess is null || (item.DataAccessIndicators.Count > 0) == query.HasDataAccess.Value)
                .Where(item => query.HasRisk is null || item.RiskIndicators.HasHotlistFindings == query.HasRisk.Value || item.RiskIndicators.HasUnknownData == query.HasRisk.Value);
        }

        /// <summary>
        /// Applies deterministic catalogue ordering and stable tie-breakers.
        /// </summary>
        /// <param name="items">The filtered catalogue rows.</param>
        /// <param name="query">The normalized catalogue query.</param>
        /// <returns>The deterministically ordered project rows.</returns>
        private static IEnumerable<ProjectCatalogueItemDto> ApplyCatalogueOrdering(IEnumerable<ProjectCatalogueItemDto> items, ProjectCatalogueQuery query)
        {
            // The first ordering expression honors the requested field, and stable key tie-breakers prevent paging drift.
            IOrderedEnumerable<ProjectCatalogueItemDto> ordered = query.Sort switch
            {
                "path" => Order(items, query.Descending, static item => item.Path ?? string.Empty),
                "language" => Order(items, query.Descending, static item => item.Language ?? string.Empty),
                "projecttype" => Order(items, query.Descending, static item => item.ProjectType ?? string.Empty),
                "targetframework" => Order(items, query.Descending, static item => item.TargetFramework ?? string.Empty),
                "dependencycount" => Order(items, query.Descending, static item => item.DependencyCount),
                "packagecount" => Order(items, query.Descending, static item => item.PackageCount),
                "endpointcount" => Order(items, query.Descending, static item => item.EndpointCount),
                "hotlistcount" => Order(items, query.Descending, static item => item.HotlistCount),
                "risk" => Order(items, query.Descending, static item => item.RiskIndicators.HotlistCount),
                _ => Order(items, query.Descending, static item => item.Name)
            };
            return ordered.ThenBy(static item => item.StableKey, StringComparer.Ordinal);
        }

        /// <summary>
        /// Orders project rows by a string key with requested direction.
        /// </summary>
        /// <param name="items">The project rows to order.</param>
        /// <param name="descending">A value indicating whether the first sort should be descending.</param>
        /// <param name="selector">The string key selector.</param>
        /// <returns>The ordered project rows.</returns>
        private static IOrderedEnumerable<ProjectCatalogueItemDto> Order(IEnumerable<ProjectCatalogueItemDto> items, bool descending, Func<ProjectCatalogueItemDto, string> selector)
        {
            // String ordering is ordinal-ignore-case for user-facing fields, with stable key tie-breakers added by the caller.
            return descending
                ? items.OrderByDescending(selector, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(selector, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Orders project rows by an integer key with requested direction.
        /// </summary>
        /// <param name="items">The project rows to order.</param>
        /// <param name="descending">A value indicating whether the first sort should be descending.</param>
        /// <param name="selector">The integer key selector.</param>
        /// <returns>The ordered project rows.</returns>
        private static IOrderedEnumerable<ProjectCatalogueItemDto> Order(IEnumerable<ProjectCatalogueItemDto> items, bool descending, Func<ProjectCatalogueItemDto, int> selector)
        {
            // Numeric ordering is used for aggregate counts while stable key tie-breakers preserve deterministic paging.
            return descending
                ? items.OrderByDescending(selector)
                : items.OrderBy(selector);
        }

        /// <summary>
        /// Resolves detail lookup matches by stable key or unambiguous display name.
        /// </summary>
        /// <param name="items">The project catalogue rows available in the selected snapshot.</param>
        /// <param name="query">The normalized detail query.</param>
        /// <returns>The matched project rows.</returns>
        private static ProjectCatalogueItemDto[] ResolveProjectMatches(IEnumerable<ProjectCatalogueItemDto> items, ProjectDetailQuery query)
        {
            // Stable-key lookup is exact; name lookup intentionally returns all exact name matches for conflict handling.
            return query.ProjectStableKey is not null
                ? items.Where(item => StringComparer.Ordinal.Equals(item.StableKey, query.ProjectStableKey)).ToArray()
                : items.Where(item => string.Equals(item.Name, query.ProjectName, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        /// <summary>
        /// Builds one project detail response from the selected project and related graph facts.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="summary">The selected project catalogue summary.</param>
        /// <param name="context">The query context shared with the API envelope.</param>
        /// <returns>The detailed project response payload.</returns>
        private static ProjectDetailDto BuildProjectDetail(ExtractedArchitectureSnapshot snapshot, ProjectCatalogueItemDto summary, ProjectQueryContext context)
        {
            // Detail sections are derived by stable project ownership and direct relationships only, leaving deeper traversal to future bounded graph endpoints.
            string projectStableKey = summary.StableKey;
            ArchitectureNode projectNode = snapshot.Nodes.First(node => StringComparer.Ordinal.Equals(node.StableKey.Value, projectStableKey));
            ArchitectureNode[] ownedNodes = snapshot.Nodes.Where(node => StringComparer.Ordinal.Equals(node.ProjectStableKey?.Value, projectStableKey)).ToArray();
            ArchitectureEdge[] outgoingEdges = snapshot.Edges.Where(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, projectStableKey)).ToArray();
            ArchitectureEdge[] incomingEdges = snapshot.Edges.Where(edge => StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, projectStableKey)).ToArray();
            FindingRecord[] findings = FindProjectFindings(snapshot, projectStableKey);
            EvidenceReferenceDto[] evidence = BuildEvidenceReferences(snapshot, projectNode, outgoingEdges, incomingEdges, findings);
            ResponsibilitySummaryDto[] responsibilities = BuildResponsibilities(summary, evidence);
            ScopedGraphSummaryDto graphSummary = new(
                ownedNodes.Length + 1,
                outgoingEdges.Count(IsProjectDependency),
                incomingEdges.Count(IsProjectDependency),
                summary.EndpointCount,
                summary.DataAccessIndicators.Count,
                CountIntegrationFacts(snapshot, projectStableKey));
            return new ProjectDetailDto(
                summary,
                responsibilities,
                evidence,
                BuildEntryPoints(ownedNodes),
                outgoingEdges.Where(IsProjectDependency).Select(static edge => edge.TargetNodeStableKey.Value),
                incomingEdges.Where(IsProjectDependency).Select(static edge => edge.SourceNodeStableKey.Value),
                BuildPackages(snapshot, projectStableKey),
                MetadataString(projectNode.Metadata, "application.type"),
                ownedNodes.Where(static node => node.NodeKind == NodeKind.Endpoint).Select(static node => node.DisplayName),
                ownedNodes.Where(static node => node.NodeKind == NodeKind.HostedService).Select(static node => node.DisplayName),
                BuildDataAccessIndicators(snapshot, projectStableKey),
                ownedNodes.Where(static node => node.NodeKind == NodeKind.ConfigurationKey).Select(static node => node.DisplayName),
                BuildIntegrations(snapshot, projectStableKey),
                findings.Select(static finding => finding.StableKey.Value),
                graphSummary,
                context.Unknowns.Concat(projectNode.UnknownState.HasUnknownData ? [new ProjectUnknownDto("summary", projectNode.UnknownState.UnknownReason!)] : []),
                context.Warnings,
                PublicMetadataSanitizer.Sanitize(projectNode.Metadata));
        }

        /// <summary>
        /// Builds safe evidence references for a project detail response.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="projectNode">The selected project node.</param>
        /// <param name="outgoingEdges">The outgoing edges from the selected project.</param>
        /// <param name="incomingEdges">The incoming edges to the selected project.</param>
        /// <param name="findings">The findings associated with the selected project.</param>
        /// <returns>Safe evidence references without source snippet previews.</returns>
        private static EvidenceReferenceDto[] BuildEvidenceReferences(ExtractedArchitectureSnapshot snapshot, ArchitectureNode projectNode, IReadOnlyList<ArchitectureEdge> outgoingEdges, IReadOnlyList<ArchitectureEdge> incomingEdges, IReadOnlyList<FindingRecord> findings)
        {
            // Evidence keys are gathered from project, relationship, and finding facts and then resolved to safe evidence metadata when possible.
            HashSet<string> keys = new(StringComparer.Ordinal);
            AddIfPresent(keys, projectNode.PrimaryEvidenceStableKey?.Value);
            foreach (ArchitectureEdge edge in outgoingEdges.Concat(incomingEdges))
            {
                AddIfPresent(keys, edge.PrimaryEvidenceStableKey?.Value);
            }

            foreach (FindingRecord finding in findings)
            {
                foreach (StableKey evidenceStableKey in finding.EvidenceStableKeys)
                {
                    AddIfPresent(keys, evidenceStableKey.Value);
                }
            }

            return keys
                .OrderBy(static key => key, StringComparer.Ordinal)
                .Select(key => ToEvidenceReference(snapshot, key))
                .ToArray();
        }

        /// <summary>
        /// Converts a stable evidence key into a safe evidence reference DTO.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="stableKey">The evidence stable key to resolve.</param>
        /// <returns>A safe evidence reference with available metadata.</returns>
        private static EvidenceReferenceDto ToEvidenceReference(ExtractedArchitectureSnapshot snapshot, string stableKey)
        {
            // Missing evidence records still produce stable references, allowing callers to follow persisted keys when evidence becomes available later.
            EvidenceRecord? evidence = snapshot.Evidence.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.StableKey.Value, stableKey));
            return evidence is null
                ? new EvidenceReferenceDto(stableKey, null, null, null, null, null, null)
                : new EvidenceReferenceDto(stableKey, evidence.EvidenceKind.Value, evidence.FilePath.Value, evidence.StartLine, evidence.EndLine, evidence.SymbolName, evidence.SnippetHash);
        }

        /// <summary>
        /// Builds inferred responsibility summaries for a project detail response.
        /// </summary>
        /// <param name="summary">The selected project catalogue summary.</param>
        /// <param name="evidence">The evidence references available for responsibility support.</param>
        /// <returns>The inferred responsibility summaries.</returns>
        private static ResponsibilitySummaryDto[] BuildResponsibilities(ProjectCatalogueItemDto summary, IReadOnlyList<EvidenceReferenceDto> evidence)
        {
            // Responsibilities are conservative labels inferred from explicit project classifications and graph ownership counts.
            List<ResponsibilitySummaryDto> responsibilities = [];
            string[] evidenceKeys = evidence.Select(static item => item.StableKey).ToArray();
            if (!string.IsNullOrWhiteSpace(summary.ProjectType))
            {
                responsibilities.Add(new ResponsibilitySummaryDto(summary.ProjectType!, $"Project is classified as {summary.ProjectType}.", evidenceKeys));
            }

            if (summary.EndpointCount > 0)
            {
                responsibilities.Add(new ResponsibilitySummaryDto("HTTP surface", "Project declares endpoint nodes and participates in the runtime API surface.", evidenceKeys));
            }

            if (summary.DataAccessIndicators.Count > 0)
            {
                responsibilities.Add(new ResponsibilitySummaryDto("Data access", "Project owns data-access indicators such as DbContext or table relationships.", evidenceKeys));
            }

            return responsibilities.ToArray();
        }

        /// <summary>
        /// Builds entry-point names from project-owned graph nodes.
        /// </summary>
        /// <param name="ownedNodes">The nodes owned by the selected project.</param>
        /// <returns>The stable entry-point display names.</returns>
        private static string[] BuildEntryPoints(IEnumerable<ArchitectureNode> ownedNodes)
        {
            // Endpoints, controllers, hosted services, and UI applications are treated as project entry points for the detail slice.
            return ownedNodes
                .Where(static node => node.NodeKind == NodeKind.Endpoint || node.NodeKind == NodeKind.Controller || node.NodeKind == NodeKind.HostedService || node.NodeKind == NodeKind.UiApplication)
                .Select(static node => node.DisplayName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Builds package names or stable keys associated with the selected project.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="projectStableKey">The selected project stable key.</param>
        /// <returns>The package identifiers associated with the selected project.</returns>
        private static string[] BuildPackages(ExtractedArchitectureSnapshot snapshot, string projectStableKey)
        {
            // Package output prefers target package display names and falls back to target stable keys when no package node is available.
            return snapshot.Edges
                .Where(edge => edge.EdgeKind == EdgeKind.UsesPackage && StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, projectStableKey))
                .Select(edge => snapshot.Nodes.FirstOrDefault(node => StringComparer.Ordinal.Equals(node.StableKey.Value, edge.TargetNodeStableKey.Value))?.DisplayName ?? edge.TargetNodeStableKey.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Builds data-access indicators associated with the selected project.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="projectStableKey">The selected project stable key.</param>
        /// <returns>The stable data-access indicator names.</returns>
        private static string[] BuildDataAccessIndicators(ExtractedArchitectureSnapshot snapshot, string projectStableKey)
        {
            // Data-access indicators combine owned data-access nodes and direct data-access relationship kinds.
            List<string> indicators = [];
            indicators.AddRange(snapshot.Nodes
                .Where(node => StringComparer.Ordinal.Equals(node.ProjectStableKey?.Value, projectStableKey))
                .Where(static node => node.NodeKind == NodeKind.DbContext || node.NodeKind == NodeKind.LinqToSqlDataContext || node.NodeKind == NodeKind.DatabaseTable || node.NodeKind == NodeKind.StoredProcedure)
                .Select(static node => node.NodeKind.Value));
            indicators.AddRange(snapshot.Edges
                .Where(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, projectStableKey))
                .Where(static edge => edge.EdgeKind == EdgeKind.UsesDbContext || edge.EdgeKind == EdgeKind.UsesLinqToSqlContext || edge.EdgeKind == EdgeKind.ReadsTable || edge.EdgeKind == EdgeKind.WritesTable || edge.EdgeKind == EdgeKind.CallsStoredProcedure || edge.EdgeKind == EdgeKind.ExecutesRawSql)
                .Select(static edge => edge.EdgeKind.Value));
            return indicators.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Builds integration identifiers associated with the selected project.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="projectStableKey">The selected project stable key.</param>
        /// <returns>The stable integration identifiers.</returns>
        private static string[] BuildIntegrations(ExtractedArchitectureSnapshot snapshot, string projectStableKey)
        {
            // Integrations are represented by external service, queue, topic, and API-call relationships directly owned by the project.
            return snapshot.Edges
                .Where(edge => StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, projectStableKey))
                .Where(static edge => edge.EdgeKind == EdgeKind.CallsExternalService || edge.EdgeKind == EdgeKind.CallsApi || edge.EdgeKind == EdgeKind.Handles)
                .Select(edge => snapshot.Nodes.FirstOrDefault(node => StringComparer.Ordinal.Equals(node.StableKey.Value, edge.TargetNodeStableKey.Value))?.DisplayName ?? edge.TargetNodeStableKey.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Counts integration facts directly associated with the selected project.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="projectStableKey">The selected project stable key.</param>
        /// <returns>The count of integration facts.</returns>
        private static int CountIntegrationFacts(ExtractedArchitectureSnapshot snapshot, string projectStableKey)
        {
            // The scoped summary needs only a count; the detail section itself contains the stable integration identifiers.
            return BuildIntegrations(snapshot, projectStableKey).Length;
        }

        /// <summary>
        /// Builds risk indicators for one project from project unknown state and findings.
        /// </summary>
        /// <param name="project">The project node being summarized.</param>
        /// <param name="findings">The findings associated with the project.</param>
        /// <returns>The project risk indicator DTO.</returns>
        private static ProjectRiskIndicatorsDto BuildRiskIndicators(ArchitectureNode project, IReadOnlyList<FindingRecord> findings)
        {
            // Risk derives from explicit finding severity and unknown state rather than implicit scoring or database metadata.
            string? highestSeverity = findings
                .OrderByDescending(static finding => SeverityRank(finding.Severity.Value))
                .Select(static finding => finding.Severity.Value)
                .FirstOrDefault();
            return new ProjectRiskIndicatorsDto(findings.Count > 0, findings.Count, highestSeverity, project.UnknownState.HasUnknownData, project.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Finds findings that target the selected project.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="projectStableKey">The selected project stable key.</param>
        /// <returns>The matching findings.</returns>
        private static FindingRecord[] FindProjectFindings(ExtractedArchitectureSnapshot snapshot, string projectStableKey)
        {
            // Findings can target the primary node or any affected node collection entry.
            return snapshot.Findings
                .Where(finding => StringComparer.Ordinal.Equals(finding.PrimaryNodeStableKey?.Value, projectStableKey)
                    || finding.AffectedNodeStableKeys.Any(key => StringComparer.Ordinal.Equals(key.Value, projectStableKey)))
                .ToArray();
        }

        /// <summary>
        /// Determines whether an edge should count as a project dependency/reference edge.
        /// </summary>
        /// <param name="edge">The edge to inspect.</param>
        /// <returns><see langword="true"/> when the edge represents a project dependency; otherwise, <see langword="false"/>.</returns>
        private static bool IsProjectDependency(ArchitectureEdge edge)
        {
            // The project catalogue treats reference and dependency-like edges as broad dependency indicators.
            return edge.EdgeKind == EdgeKind.References || edge.EdgeKind == EdgeKind.DependsOn;
        }

        /// <summary>
        /// Determines whether an edge is directly related to the selected project.
        /// </summary>
        /// <param name="edge">The edge to inspect.</param>
        /// <param name="projectStableKey">The selected project stable key.</param>
        /// <returns><see langword="true"/> when the edge uses the project as source or target; otherwise, <see langword="false"/>.</returns>
        private static bool IsProjectRelated(ArchitectureEdge edge, string projectStableKey)
        {
            // Edge evidence contributes to project detail only when the selected project participates directly in the relationship.
            return StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, projectStableKey)
                || StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, projectStableKey);
        }

        /// <summary>
        /// Determines whether a string contains a search term using ordinal-ignore-case matching.
        /// </summary>
        /// <param name="value">The candidate value to inspect.</param>
        /// <param name="search">The search term to find.</param>
        /// <returns><see langword="true"/> when the candidate contains the search term; otherwise, <see langword="false"/>.</returns>
        private static bool Contains(string? value, string search)
        {
            // Search applies only to bounded public identity fields and does not execute arbitrary graph predicates.
            return value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Returns the application type field used by catalogue application-type filtering.
        /// </summary>
        /// <param name="item">The catalogue item to inspect.</param>
        /// <returns>The application type value when known.</returns>
        private static string? MetadataApplicationType(ProjectCatalogueItemDto item)
        {
            // Application type currently shares the public project type fallback when no explicit metadata projection is available in catalogue rows.
            return item.ProjectType;
        }

        /// <summary>
        /// Reads a string metadata value by key from canonical graph metadata.
        /// </summary>
        /// <param name="metadata">The graph metadata to read.</param>
        /// <param name="key">The metadata key to resolve.</param>
        /// <returns>The string metadata value, or null when unavailable.</returns>
        private static string? MetadataString(GraphMetadata metadata, string key)
        {
            // Metadata is canonical JSON, so reading values uses JsonDocument instead of exposing the raw JSON contract to callers.
            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(key, out JsonElement element) && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        }

        /// <summary>
        /// Reads a boolean metadata value by key from canonical graph metadata.
        /// </summary>
        /// <param name="metadata">The graph metadata to read.</param>
        /// <param name="key">The metadata key to resolve.</param>
        /// <returns>The boolean metadata value, or null when unavailable.</returns>
        private static bool? MetadataBool(GraphMetadata metadata, string key)
        {
            // Boolean metadata currently captures SDK-style status and similar project attributes.
            using JsonDocument document = JsonDocument.Parse(metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(key, out JsonElement element) && (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                ? element.GetBoolean()
                : null;
        }

        /// <summary>
        /// Adds snapshot diagnostics to response warnings.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="warnings">The warning list being built.</param>
        private static void AddSnapshotDiagnostics(ExtractedArchitectureSnapshot snapshot, ICollection<ProjectWarningDto> warnings)
        {
            // Snapshot warnings and errors become safe warnings because they explain why project data may be partial.
            foreach (string warning in snapshot.Warnings.Concat(snapshot.SnapshotHeader?.Warnings ?? []))
            {
                warnings.Add(new ProjectWarningDto("SnapshotWarning", warning));
            }

            foreach (string error in snapshot.Errors.Concat(snapshot.SnapshotHeader?.Errors ?? []))
            {
                warnings.Add(new ProjectWarningDto("SnapshotError", error));
            }
        }

        /// <summary>
        /// Adds unknown entries for optional project sections that are unavailable in the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="unknowns">The unknown list being built.</param>
        private static void AddProjectSectionUnknowns(ExtractedArchitectureSnapshot snapshot, ICollection<ProjectUnknownDto> unknowns)
        {
            // Empty optional sections are reported as unknown so clients can distinguish unavailable extraction data from confirmed absence.
            if (snapshot.Evidence.Count == 0)
            {
                unknowns.Add(new ProjectUnknownDto("evidence", "No evidence records were available for the selected snapshot."));
            }

            if (snapshot.Metrics.Count == 0)
            {
                unknowns.Add(new ProjectUnknownDto("metrics", "No project-level metric records were available for the selected snapshot."));
            }
        }

        /// <summary>
        /// Adds a non-empty value to a string collection.
        /// </summary>
        /// <param name="values">The collection receiving the value.</param>
        /// <param name="value">The optional value to add.</param>
        private static void AddIfPresent(ICollection<string> values, string? value)
        {
            // This helper keeps evidence-key gathering concise while rejecting blank optional values.
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        /// <summary>
        /// Maps finding severity values into deterministic risk order.
        /// </summary>
        /// <param name="severity">The severity value to rank.</param>
        /// <returns>The numeric severity rank.</returns>
        private static int SeverityRank(string severity)
        {
            // The rank list mirrors common risk ordering and falls back to zero for unknown custom severities.
            return severity switch
            {
                "Critical" => 4,
                "High" => 3,
                "Medium" => 2,
                "Low" => 1,
                _ => 0
            };
        }

        /// <summary>
        /// Stores the outcome of snapshot resolution for project queries.
        /// </summary>
        private sealed class SnapshotResolution
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SnapshotResolution"/> class.
            /// </summary>
            /// <param name="snapshot">The resolved selected snapshot when resolution succeeded.</param>
            /// <param name="scopedSnapshots">All snapshots in the repository and optional solution scope.</param>
            /// <param name="validationErrors">The validation errors that prevented resolution.</param>
            private SnapshotResolution(ExtractedArchitectureSnapshot? snapshot, IReadOnlyList<ExtractedArchitectureSnapshot> scopedSnapshots, IReadOnlyList<ProjectQueryValidationError> validationErrors)
            {
                // The resolution object keeps success and error paths explicit for both catalogue and detail flows.
                Snapshot = snapshot;
                ScopedSnapshots = scopedSnapshots;
                ValidationErrors = validationErrors;
            }

            /// <summary>
            /// Gets a value indicating whether snapshot resolution succeeded.
            /// </summary>
            public bool Succeeded
            {
                get
                {
                    // A resolution succeeds only when no validation errors were recorded.
                    return ValidationErrors.Count == 0;
                }
            }

            /// <summary>
            /// Gets the resolved selected snapshot when resolution succeeded.
            /// </summary>
            public ExtractedArchitectureSnapshot? Snapshot { get; }

            /// <summary>
            /// Gets all snapshots in the repository and optional solution scope.
            /// </summary>
            public IReadOnlyList<ExtractedArchitectureSnapshot> ScopedSnapshots { get; }

            /// <summary>
            /// Gets the validation errors that prevented resolution.
            /// </summary>
            public IReadOnlyList<ProjectQueryValidationError> ValidationErrors { get; }

            /// <summary>
            /// Creates a successful snapshot resolution.
            /// </summary>
            /// <param name="snapshot">The resolved selected snapshot.</param>
            /// <param name="scopedSnapshots">All snapshots in the repository and optional solution scope.</param>
            /// <returns>The successful snapshot resolution.</returns>
            public static SnapshotResolution Success(ExtractedArchitectureSnapshot snapshot, IReadOnlyList<ExtractedArchitectureSnapshot> scopedSnapshots)
            {
                // Factory use makes call sites self-documenting and prevents partially initialized resolution objects.
                return new SnapshotResolution(snapshot, scopedSnapshots, []);
            }

            /// <summary>
            /// Creates a failed snapshot resolution.
            /// </summary>
            /// <param name="validationErrors">The validation errors that prevented resolution.</param>
            /// <returns>The failed snapshot resolution.</returns>
            public static SnapshotResolution Failed(IReadOnlyList<ProjectQueryValidationError> validationErrors)
            {
                // Failed resolutions carry only safe validation details and no graph facts.
                return new SnapshotResolution(null, [], validationErrors);
            }
        }
    }
}
