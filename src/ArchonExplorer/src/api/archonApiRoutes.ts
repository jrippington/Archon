/**
 * Describes a path-only ArchonApi route that may be combined with typed query
 * objects by later API-client slices.
 */
export type ArchonApiPath = `/${string}`;

/**
 * Encodes one route path segment so stable keys containing slash-like or special
 * characters stay inside a single ASP.NET Core route value.
 *
 * @param value - The raw route value supplied by a caller, such as a run ID or stable key.
 * @returns The encoded route segment that is safe to interpolate into a path builder.
 */
function encodeRouteSegment(value: string): string {
  // encodeURIComponent is intentionally used instead of encodeURI because route
  // parameters must not preserve `/`, `?`, `#`, or other path/query delimiters.
  return encodeURIComponent(value);
}

/**
 * Builds the no-common-prefix ArchonApi route catalog consumed by ArchonExplorer.
 *
 * @returns A grouped set of constants and builders that mirror current ArchonApi endpoint mappings.
 */
function createArchonApiRoutes() {
  // ArchonApi maps public routes directly at the application root. The frontend
  // catalog therefore stores `/health`, `/extractions`, and similar paths rather
  // than inventing a browser-only `/api` prefix that the server does not expose.
  return {
    /**
     * Operational health and readiness routes used by connectivity checks.
     */
    operations: {
      /**
       * Health route exposed by the management module for safe local status checks.
       */
      health: '/health',

      /**
       * Readiness route exposed by the management module for dependency availability checks.
       */
      ready: '/ready',
    },

    /**
     * Extraction workflow routes for starting, listing, and polling extraction runs.
     */
    extraction: {
      /**
       * Recent extraction run history route.
       */
      runs: '/extractions',

      /**
       * Start-extraction route; the path matches the run-history collection with POST semantics.
       */
      start: '/extractions',

      /**
       * Builds a route for a single extraction run status lookup.
       *
       * @param runId - The public extraction run identifier returned by `POST /extractions`.
       * @returns The encoded extraction status path for the supplied run identifier.
       */
      byRunId(runId: string): ArchonApiPath {
        // The run identifier is encoded defensively even when it is normally a GUID.
        return `/extractions/${encodeRouteSegment(runId)}`;
      },
    },

    /**
     * Management routes for repository metadata, snapshot lifecycle, run history, and maintenance.
     */
    management: {
      /**
       * Repository registration route used by controlled management workflows.
       */
      repositories: '/management/repositories',

      /**
       * Solution registration route used by controlled management workflows.
       */
      solutions: '/management/solutions',

      /**
       * Controlled metadata update route.
       */
      metadata: '/management/metadata',

      /**
       * Snapshot lifecycle list route.
       */
      snapshots: '/management/snapshots',

      /**
       * Builds the destructive single-snapshot deletion route.
       *
       * @param snapshotStableKey - The public stable key identifying one persisted snapshot.
       * @returns The encoded management deletion path for the supplied snapshot stable key.
       */
      snapshotByStableKey(snapshotStableKey: string): ArchonApiPath {
        // Snapshot stable keys can contain URI-like slashes, so the whole key is
        // encoded as one route segment before interpolation.
        return `/management/snapshots/${encodeRouteSegment(snapshotStableKey)}`;
      },

      /**
       * Confirmed delete-all snapshot route.
       */
      deleteAllSnapshots: '/management/snapshots/delete-all',

      /**
       * Snapshot retention route.
       */
      retention: '/management/retention',

      /**
       * Extraction run-history management route.
       */
      runs: '/management/runs',

      /**
       * Rule enablement management route.
       */
      ruleEnablement: '/management/rules/enablement',

      /**
       * Allowlisted maintenance operation route.
       */
      maintenance: '/management/maintenance',
    },

    /**
     * Dashboard query routes for high-level snapshot summaries.
     */
    dashboard: {
      /**
       * Dashboard summary query route.
       */
      summary: '/dashboard-summary',
    },

    /**
     * Project catalogue and project-detail query routes.
     */
    projects: {
      /**
       * Project catalogue route.
       */
      list: '/projects',

      /**
       * Project detail query route for stable keys that are safer in query parameters.
       */
      detail: '/projects/detail',

      /**
       * Builds a catch-all project stable-key detail route.
       *
       * @param projectStableKey - The public project stable key to resolve.
       * @returns The encoded project detail path for the supplied stable key.
       */
      byStableKey(projectStableKey: string): ArchonApiPath {
        // The backend route is catch-all, but frontend callers still encode slash
        // characters so a stable key remains an indivisible logical value.
        return `/projects/${encodeRouteSegment(projectStableKey)}`;
      },
    },

    /**
     * Bounded graph traversal routes for dependency and neighbourhood exploration.
     */
    graphTraversal: {
      /**
       * Direct outgoing dependency route.
       */
      directDependencies: '/dependencies/direct',

      /**
       * Direct incoming dependent route.
       */
      directDependents: '/dependents/direct',

      /**
       * Bounded transitive outgoing dependency route.
       */
      transitiveDependencies: '/dependencies/transitive',

      /**
       * Bounded transitive incoming dependent route.
       */
      transitiveDependents: '/dependents/transitive',

      /**
       * Bounded dependency-path route between graph nodes.
       */
      dependencyPath: '/dependency-path',

      /**
       * Bounded graph-neighbourhood route around one graph node.
       */
      neighbourhood: '/graph-neighbourhood',
    },

    /**
     * Symbol search, detail, and usage query routes.
     */
    symbols: {
      /**
       * Symbol search route.
       */
      search: '/symbols',

      /**
       * Symbol detail query route.
       */
      detail: '/symbols/detail',

      /**
       * Symbol usage route.
       */
      usages: '/symbols/usages',
    },

    /**
     * Runtime fact query routes for endpoints, controllers, entry points, and workers.
     */
    runtime: {
      /**
       * Runtime endpoint catalogue route.
       */
      endpoints: '/runtime/endpoints',

      /**
       * Controller or handler detail route.
       */
      controllers: '/runtime/controllers',

      /**
       * Runtime entry-point catalogue route.
       */
      entryPoints: '/runtime/entry-points',

      /**
       * Worker runtime fact route.
       */
      workers: '/runtime/workers',
    },

    /**
     * Architecture fact routes that expose bounded, secret-safe fact catalogues.
     */
    facts: {
      /**
       * Data-access fact route.
       */
      dataAccess: '/data-access',

      /**
       * Configuration usage fact route.
       */
      configuration: '/configuration',

      /**
       * External integration fact route.
       */
      integrations: '/integrations',

      /**
       * UI technology fact route.
       */
      uiTechnologies: '/ui-technologies',
    },

    /**
     * Evidence routes that expose bounded source context and related evidence.
     */
    evidence: {
      /**
       * Evidence detail route.
       */
      detail: '/evidence/detail',

      /**
       * Related evidence list route.
       */
      related: '/evidence/related',
    },

    /**
     * Rule catalog routes for persisted architecture and modernization rules.
     */
    rules: {
      /**
       * Rule catalogue list route.
       */
      list: '/rules',

      /**
       * Builds a route for one exact rule identity.
       *
       * @param ruleCode - The stable rule code assigned to the persisted rule.
       * @param version - The version string for the requested rule definition.
       * @returns The encoded rule detail path for the supplied code and version.
       */
      byIdentity(ruleCode: string, version: string): ArchonApiPath {
        // Rule code and version are separate backend route values, so each value
        // is encoded independently before the final path is assembled.
        return `/rules/${encodeRouteSegment(ruleCode)}/${encodeRouteSegment(version)}`;
      },
    },

    /**
     * Finding, hotlist, history, and suppression routes.
     */
    findings: {
      /**
       * Hotlist route for persisted finding summaries.
       */
      hotlist: '/hotlist',

      /**
       * Finding detail query route for slash-containing stable keys.
       */
      detail: '/findings/detail',

      /**
       * Builds a route for one finding inside one snapshot.
       *
       * @param snapshotStableKey - The public snapshot stable key that scopes the finding.
       * @param findingStableKey - The public finding stable key to resolve inside the snapshot.
       * @returns The encoded finding detail path for the supplied snapshot and finding keys.
       */
      byStableKey(snapshotStableKey: string, findingStableKey: string): ArchonApiPath {
        // Both stable keys may contain URI-like separators, so each backend path
        // value is encoded before composing the two-segment route.
        return `/findings/${encodeRouteSegment(snapshotStableKey)}/${encodeRouteSegment(findingStableKey)}`;
      },

      /**
       * Finding history query route for slash-containing history keys.
       */
      history: '/finding-history',

      /**
       * Builds a route for one deterministic finding history key.
       *
       * @param historyKey - The cross-snapshot finding history key to resolve.
       * @returns The encoded finding-history path for the supplied history key.
       */
      historyByKey(historyKey: string): ArchonApiPath {
        // History keys behave like stable keys and are encoded as one logical value.
        return `/findings/history/${encodeRouteSegment(historyKey)}`;
      },

      /**
       * Finding suppression mutation route.
       */
      suppressions: '/findings/suppressions',
    },

    /**
     * Snapshot metric query routes.
     */
    metrics: {
      /**
       * Builds a route for metrics owned by a single snapshot stable key.
       *
       * @param snapshotStableKey - The public stable key of the snapshot whose metrics are requested.
       * @returns The encoded snapshot metrics path for the supplied stable key.
       */
      snapshotByStableKey(snapshotStableKey: string): ArchonApiPath {
        // The snapshot key is encoded as one segment before the `/metrics` suffix.
        return `/snapshots/${encodeRouteSegment(snapshotStableKey)}/metrics`;
      },

      /**
       * Query-parameter based snapshot metrics route for slash-containing stable keys.
       */
      snapshotByQuery: '/snapshot-metrics',
    },

    /**
     * Dependency-cycle query routes.
     */
    cycles: {
      /**
       * Query-parameter based snapshot dependency cycle route.
       */
      snapshotCycles: '/snapshot-cycles',
    },

    /**
     * Architecture hotspot query routes.
     */
    hotspots: {
      /**
       * Query-parameter based snapshot hotspot route.
       */
      snapshotHotspots: '/snapshot-hotspots',
    },

    /**
     * Architecture-rule result query routes.
     */
    architectureRules: {
      /**
       * Query-parameter based evaluated architecture-rule route.
       */
      snapshotArchitectureRules: '/snapshot-architecture-rules',
    },

    /**
     * Snapshot diff query routes.
     */
    diff: {
      /**
       * Explicit snapshot diff route.
       */
      snapshot: '/snapshot-diff',

      /**
       * Latest-to-previous snapshot diff route.
       */
      latest: '/snapshot-diff/latest',
    },

    /**
     * Cross-domain architecture search routes.
     */
    search: {
      /**
       * Cross-domain search route.
       */
      all: '/search',
    },
  } as const;
}

/**
 * Centralized ArchonApi route catalog for browser-side code.
 *
 * The catalog is grouped by API area so feature packages can import paths from a
 * single source of truth instead of duplicating literals. Constants intentionally
 * keep query-string construction out of base paths; later typed query objects can
 * be serialized by the request layer without changing these route identities.
 */
export const archonApiRoutes = createArchonApiRoutes();

/**
 * Collects representative route strings and builder outputs for catalog-level validation.
 *
 * @returns Route samples that exercise every route group and parameterized path builder.
 */
export function collectArchonApiRouteSamples(): readonly ArchonApiPath[] {
  // This helper is used by tests to enforce the no-common-`/api` convention
  // across constants and representative builder outputs without exposing a
  // reflection-heavy traversal utility as part of the production request layer.
  return [
    archonApiRoutes.operations.health,
    archonApiRoutes.operations.ready,
    archonApiRoutes.extraction.runs,
    archonApiRoutes.extraction.start,
    archonApiRoutes.extraction.byRunId('00000000-0000-0000-0000-000000000000'),
    archonApiRoutes.management.repositories,
    archonApiRoutes.management.solutions,
    archonApiRoutes.management.metadata,
    archonApiRoutes.management.snapshots,
    archonApiRoutes.management.snapshotByStableKey('snapshot://repository/solution/current'),
    archonApiRoutes.management.deleteAllSnapshots,
    archonApiRoutes.management.retention,
    archonApiRoutes.management.runs,
    archonApiRoutes.management.ruleEnablement,
    archonApiRoutes.management.maintenance,
    archonApiRoutes.dashboard.summary,
    archonApiRoutes.projects.list,
    archonApiRoutes.projects.detail,
    archonApiRoutes.projects.byStableKey('project://repository/src/App.csproj'),
    archonApiRoutes.graphTraversal.directDependencies,
    archonApiRoutes.graphTraversal.directDependents,
    archonApiRoutes.graphTraversal.transitiveDependencies,
    archonApiRoutes.graphTraversal.transitiveDependents,
    archonApiRoutes.graphTraversal.dependencyPath,
    archonApiRoutes.graphTraversal.neighbourhood,
    archonApiRoutes.symbols.search,
    archonApiRoutes.symbols.detail,
    archonApiRoutes.symbols.usages,
    archonApiRoutes.runtime.endpoints,
    archonApiRoutes.runtime.controllers,
    archonApiRoutes.runtime.entryPoints,
    archonApiRoutes.runtime.workers,
    archonApiRoutes.facts.dataAccess,
    archonApiRoutes.facts.configuration,
    archonApiRoutes.facts.integrations,
    archonApiRoutes.facts.uiTechnologies,
    archonApiRoutes.evidence.detail,
    archonApiRoutes.evidence.related,
    archonApiRoutes.rules.list,
    archonApiRoutes.rules.byIdentity('ARCH001', '1.0.0'),
    archonApiRoutes.findings.hotlist,
    archonApiRoutes.findings.detail,
    archonApiRoutes.findings.byStableKey('snapshot://repository/current', 'finding://rule/target'),
    archonApiRoutes.findings.history,
    archonApiRoutes.findings.historyByKey('history://rule/target'),
    archonApiRoutes.findings.suppressions,
    archonApiRoutes.metrics.snapshotByStableKey('snapshot://repository/current'),
    archonApiRoutes.metrics.snapshotByQuery,
    archonApiRoutes.cycles.snapshotCycles,
    archonApiRoutes.hotspots.snapshotHotspots,
    archonApiRoutes.architectureRules.snapshotArchitectureRules,
    archonApiRoutes.diff.snapshot,
    archonApiRoutes.diff.latest,
    archonApiRoutes.search.all,
  ];
}
