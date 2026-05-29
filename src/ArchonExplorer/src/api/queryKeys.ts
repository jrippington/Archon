import type { SnapshotSelector } from './archonApiTypes';

/**
 * Represents a TanStack Query key segment that can be compared and serialized deterministically.
 */
export type ArchonQueryKeySegment = string | number | boolean | null | Readonly<Record<string, unknown>>;

/**
 * Represents one readonly TanStack Query key produced by the ArchonExplorer runtime.
 */
export type ArchonQueryKey = readonly ArchonQueryKeySegment[];

/**
 * Represents a typed filter object that can be normalized into one query-key segment.
 */
export type QueryKeyObjectInput = Readonly<object>;

/**
 * Describes common repository and solution scope values carried by query keys.
 */
export interface QueryScopeInput {
  /**
   * Identifies the repository whose server state is being cached or invalidated.
   */
  readonly repositoryStableKey?: string;

  /**
   * Identifies the solution inside a repository when the query is solution-scoped.
   */
  readonly solutionStableKey?: string;
}

/**
 * Describes paging values that are part of the server-state identity for list queries.
 */
export interface QueryPaginationInput {
  /**
   * Selects the one-based page number or equivalent page cursor ordinal when a later endpoint supports paging.
   */
  readonly page?: number;

  /**
   * Selects the number of rows requested for a list query.
   */
  readonly pageSize?: number;
}

/**
 * Describes filters that identify one extraction run status query.
 */
export interface ExtractionRunStatusQueryKeyInput {
  /**
   * Contains the public run identifier returned by the extraction API.
   */
  readonly runId: string;
}

/**
 * Describes filters that identify an extraction history list query.
 */
export interface ExtractionHistoryQueryKeyInput extends QueryScopeInput {
  /**
   * Selects the maximum number of recent runs requested from the API.
   */
  readonly take?: number;
}

/**
 * Describes filters that identify a snapshot lifecycle query.
 */
export interface SnapshotLifecycleQueryKeyInput extends QueryScopeInput {
  /**
   * Selects a specific lifecycle status when the list is filtered by state.
   */
  readonly status?: string;

  /**
   * Selects the inclusive lower UTC time bound for the lifecycle list.
   */
  readonly fromUtc?: string;

  /**
   * Selects the inclusive upper UTC time bound for the lifecycle list.
   */
  readonly toUtc?: string;

  /**
   * Selects snapshots associated with one source-control commit when supplied.
   */
  readonly commitSha?: string;

  /**
   * Selects the maximum number of lifecycle rows requested from the API.
   */
  readonly take?: number;
}

/**
 * Describes filters that identify a dashboard summary query.
 */
export interface DashboardSummaryQueryKeyInput extends QueryScopeInput {
  /**
   * Selects either the current snapshot or an explicit persisted snapshot.
   */
  readonly snapshotSelector?: SnapshotSelector;
}

/**
 * Describes filters that identify cross-domain search server state.
 */
export interface SearchQueryKeyInput extends QueryScopeInput, QueryPaginationInput {
  /**
   * Contains the user-entered search text that changes the returned server state.
   */
  readonly searchText: string;

  /**
   * Selects the snapshot scope for search when a caller narrows results to a snapshot.
   */
  readonly snapshotSelector?: SnapshotSelector;

  /**
   * Selects a representative result type bucket such as symbols, findings, or projects.
   */
  readonly resultType?: string;
}

/**
 * Describes filters that identify representative project catalogue state.
 */
export interface ProjectCatalogueQueryKeyInput extends QueryScopeInput, QueryPaginationInput {
  /**
   * Selects the snapshot scope used by project catalogue queries.
   */
  readonly snapshotSelector?: SnapshotSelector;
}

/**
 * Describes filters that identify representative finding list state.
 */
export interface FindingsQueryKeyInput extends QueryScopeInput, QueryPaginationInput {
  /**
   * Selects the snapshot scope used by finding summary queries.
   */
  readonly snapshotSelector?: SnapshotSelector;

  /**
   * Selects a rule code when the finding list is narrowed to one rule.
   */
  readonly ruleCode?: string;

  /**
   * Selects a severity or priority bucket when the endpoint supports such filtering.
   */
  readonly severity?: string;
}

/**
 * Describes filters that identify representative graph-neighbourhood state.
 */
export interface GraphNeighbourhoodQueryKeyInput extends QueryScopeInput {
  /**
   * Selects the snapshot scope used by graph-neighbourhood queries.
   */
  readonly snapshotSelector?: SnapshotSelector;

  /**
   * Identifies the graph node at the center of the neighbourhood query.
   */
  readonly nodeStableKey: string;

  /**
   * Bounds traversal depth so cache identity reflects the amount of graph data requested.
   */
  readonly depth?: number;
}

/**
 * Describes selectors returned by invalidation helpers for extraction-related cache updates.
 */
export interface ExtractionInvalidationKeys {
  /**
   * Targets the complete extraction query family for broad invalidation.
   */
  readonly all: ArchonQueryKey;

  /**
   * Targets extraction history queries that may include the run after a start or completion event.
   */
  readonly histories: ArchonQueryKey;

  /**
   * Targets one extraction run status query when a run identifier is available.
   */
  readonly run?: ArchonQueryKey;
}

/**
 * Describes selectors returned by invalidation helpers for snapshot lifecycle cache updates.
 */
export interface SnapshotInvalidationKeys {
  /**
   * Targets the complete snapshot lifecycle query family.
   */
  readonly all: ArchonQueryKey;

  /**
   * Targets lifecycle list queries that may include deleted or newly completed snapshots.
   */
  readonly lists: ArchonQueryKey;

  /**
   * Targets an explicit snapshot scope when a stable key is known.
   */
  readonly snapshot?: ArchonQueryKey;
}

/**
 * Provides stable TanStack Query keys for every ArchonApi server-state family known to WP002.
 */
export const archonQueryKeys = {
  /**
   * Root key for every ArchonApi-backed server-state query.
   */
  all: ['archonApi'] as const,

  /**
   * Keys for health, readiness, and other operational API state.
   */
  operations: {
    /** Root key for operational state. */
    all: ['archonApi', 'operations'] as const,
    /** Key for the health endpoint response. */
    health: ['archonApi', 'operations', 'health'] as const,
    /** Key for the readiness endpoint response. */
    readiness: ['archonApi', 'operations', 'readiness'] as const,
    /** Key for global connectivity state derived from health and readiness. */
    connectivity: ['archonApi', 'operations', 'connectivity'] as const,
  },

  /**
   * Keys for extraction run history and run-status server state.
   */
  extraction: {
    /** Root key for extraction state. */
    all: ['archonApi', 'extraction'] as const,
    /** Root key for extraction history list state. */
    histories: ['archonApi', 'extraction', 'histories'] as const,
    /**
     * Builds a key for extraction history with optional scope and result bounds.
     *
     * @param input - Optional scope and result bound values that affect the returned history list.
     * @returns A stable query key for the requested extraction history list.
     */
    history(input: ExtractionHistoryQueryKeyInput = {}): ArchonQueryKey {
      // History keys include an object segment so future filters can be added without
      // changing the family prefix used by invalidation helpers.
      return [...archonQueryKeys.extraction.histories, stableObject(input)] as const;
    },
    /** Root key for individual extraction run state. */
    runs: ['archonApi', 'extraction', 'runs'] as const,
    /**
     * Builds a key for one extraction run status response.
     *
     * @param input - The run identifier that scopes the status query.
     * @returns A stable query key for the requested extraction run.
     */
    run(input: ExtractionRunStatusQueryKeyInput): ArchonQueryKey {
      // The run identifier remains a distinct segment so exact invalidation can target
      // one in-flight polling workflow without refreshing unrelated run histories.
      return [...archonQueryKeys.extraction.runs, input.runId] as const;
    },
  },

  /**
   * Keys for snapshot lifecycle server state and explicit snapshot scopes.
   */
  snapshots: {
    /** Root key for snapshot lifecycle state. */
    all: ['archonApi', 'snapshots'] as const,
    /** Root key for snapshot lifecycle list state. */
    lists: ['archonApi', 'snapshots', 'lists'] as const,
    /**
     * Builds a key for a snapshot lifecycle list query.
     *
     * @param input - Scope, status, time, commit, and result bound values for lifecycle state.
     * @returns A stable query key for the requested snapshot lifecycle list.
     */
    lifecycle(input: SnapshotLifecycleQueryKeyInput = {}): ArchonQueryKey {
      // Lifecycle lists can be invalidated broadly by the `lists` prefix while still
      // distinguishing repository, solution, status, date, commit, and take filters.
      return [...archonQueryKeys.snapshots.lists, stableObject(input)] as const;
    },
    /**
     * Builds a key for server state scoped to one explicit snapshot stable key.
     *
     * @param snapshotStableKey - The persisted snapshot identity used by detail-style queries.
     * @returns A stable query key for the explicit snapshot scope.
     */
    byStableKey(snapshotStableKey: string): ArchonQueryKey {
      // The stable key is left unencoded because query keys are in-memory cache identity,
      // not URL paths; route builders handle path encoding separately.
      return [...archonQueryKeys.snapshots.all, 'byStableKey', snapshotStableKey] as const;
    },
  },

  /**
   * Keys for dashboard summary server state.
   */
  dashboard: {
    /** Root key for dashboard query state. */
    all: ['archonApi', 'dashboard'] as const,
    /**
     * Builds a key for a dashboard summary query.
     *
     * @param input - Optional repository, solution, and snapshot scope for the summary.
     * @returns A stable query key for the requested dashboard summary.
     */
    summary(input: DashboardSummaryQueryKeyInput = {}): ArchonQueryKey {
      // Dashboard state is snapshot-scoped so later pages do not accidentally reuse a
      // current-snapshot summary for an explicit historical snapshot.
      return [...archonQueryKeys.dashboard.all, 'summary', stableObject(input)] as const;
    },
  },

  /**
   * Keys for cross-domain search state.
   */
  search: {
    /** Root key for search query state. */
    all: ['archonApi', 'search'] as const,
    /**
     * Builds a key for a cross-domain search query.
     *
     * @param input - Search text, scope, snapshot, paging, and optional result type values.
     * @returns A stable query key for the requested search result set.
     */
    results(input: SearchQueryKeyInput): ArchonQueryKey {
      // Search text is part of the key because different text represents different
      // server state even when repository and snapshot scope are unchanged.
      return [...archonQueryKeys.search.all, 'results', stableObject(input)] as const;
    },
  },

  /**
   * Keys for representative later project catalogue queries.
   */
  projects: {
    /** Root key for project catalogue state. */
    all: ['archonApi', 'projects'] as const,
    /**
     * Builds a key for a project catalogue query.
     *
     * @param input - Repository, solution, snapshot, and paging values for the catalogue.
     * @returns A stable query key for the requested project catalogue page.
     */
    catalogue(input: ProjectCatalogueQueryKeyInput = {}): ArchonQueryKey {
      // Project catalogue keys are provided now so later feature packages share the
      // same scope vocabulary instead of introducing incompatible key shapes.
      return [...archonQueryKeys.projects.all, 'catalogue', stableObject(input)] as const;
    },
  },

  /**
   * Keys for representative graph query state.
   */
  graph: {
    /** Root key for graph traversal and neighbourhood state. */
    all: ['archonApi', 'graph'] as const,
    /**
     * Builds a key for a graph-neighbourhood query.
     *
     * @param input - Node identity, scope, snapshot, and traversal depth values.
     * @returns A stable query key for the requested graph neighbourhood.
     */
    neighbourhood(input: GraphNeighbourhoodQueryKeyInput): ArchonQueryKey {
      // The node stable key and depth are part of cache identity because both change
      // the graph shape returned by the server.
      return [...archonQueryKeys.graph.all, 'neighbourhood', stableObject(input)] as const;
    },
  },

  /**
   * Keys for representative finding summary and detail state.
   */
  findings: {
    /** Root key for finding state. */
    all: ['archonApi', 'findings'] as const,
    /**
     * Builds a key for a finding list or hotlist query.
     *
     * @param input - Snapshot, scope, paging, rule, and severity filters for findings.
     * @returns A stable query key for the requested finding list.
     */
    list(input: FindingsQueryKeyInput = {}): ArchonQueryKey {
      // Finding keys include rule and severity filters so later workbench pages can
      // invalidate or reuse lists predictably.
      return [...archonQueryKeys.findings.all, 'list', stableObject(input)] as const;
    },
  },
} as const;

/**
 * Builds cache invalidation selectors for extraction workflows.
 *
 * @param runId - Optional run identifier used to target one status query in addition to family keys.
 * @returns The extraction cache selectors a caller should invalidate after run state changes.
 */
export function getExtractionInvalidationKeys(runId?: string): ExtractionInvalidationKeys {
  // Starting or completing extraction can affect both the status query and history
  // lists, so callers receive broad family keys plus an exact run key when possible.
  return {
    all: archonQueryKeys.extraction.all,
    histories: archonQueryKeys.extraction.histories,
    run: runId === undefined ? undefined : archonQueryKeys.extraction.run({ runId }),
  };
}

/**
 * Builds cache invalidation selectors for snapshot lifecycle workflows.
 *
 * @param snapshotStableKey - Optional snapshot identity used to target explicit snapshot state.
 * @returns The snapshot cache selectors a caller should invalidate after lifecycle changes.
 */
export function getSnapshotInvalidationKeys(snapshotStableKey?: string): SnapshotInvalidationKeys {
  // Snapshot creation and deletion affect lifecycle lists broadly; explicit snapshot
  // state is included only when the caller knows the stable key that changed.
  return {
    all: archonQueryKeys.snapshots.all,
    lists: archonQueryKeys.snapshots.lists,
    snapshot: snapshotStableKey === undefined ? undefined : archonQueryKeys.snapshots.byStableKey(snapshotStableKey),
  };
}

/**
 * Creates a deterministic object segment for TanStack Query keys.
 *
 * @param value - The filter, scope, paging, or selector object that affects server-state identity.
 * @returns A sanitized object with undefined values removed and nested keys sorted predictably.
 */
export function stableObject<TValue extends QueryKeyObjectInput>(value: TValue): Readonly<Record<string, unknown>> {
  // TanStack Query hashes object segments deterministically, but normalizing here makes
  // tests and developer inspection easier while removing undefined optional filters.
  return normalizeObject(value) as Readonly<Record<string, unknown>>;
}

/**
 * Recursively normalizes query-key object segments.
 *
 * @param value - The value being normalized for use inside a query key.
 * @returns A value with object keys sorted and undefined properties omitted.
 */
function normalizeObject(value: unknown): unknown {
  // Arrays preserve order because caller order can be meaningful for filters, while
  // object keys are sorted so insertion order does not make equivalent filters look different.
  if (Array.isArray(value)) {
    return value.map((item) => normalizeObject(item));
  }

  if (value !== null && typeof value === 'object') {
    const normalizedEntries = Object.entries(value)
      .filter(([, item]) => item !== undefined)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, item]) => [key, normalizeObject(item)] as const);

    return Object.fromEntries(normalizedEntries);
  }

  return value;
}