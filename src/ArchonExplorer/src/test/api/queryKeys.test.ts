import { describe, expect, it } from 'vitest';
import { archonQueryKeys, getExtractionInvalidationKeys, getSnapshotInvalidationKeys, stableObject } from '@/api/queryKeys';

/**
 * Verifies stable TanStack Query key conventions for ArchonApi server state.
 */
describe('archonQueryKeys', () => {
  /**
   * Confirms operational keys use a shared ArchonApi root and distinct operation names.
   */
  it('creates stable operation keys', () => {
    expect(archonQueryKeys.operations.health).toEqual(['archonApi', 'operations', 'health']);
    expect(archonQueryKeys.operations.readiness).toEqual(['archonApi', 'operations', 'readiness']);
    expect(archonQueryKeys.operations.connectivity).toEqual(['archonApi', 'operations', 'connectivity']);
  });

  /**
   * Confirms extraction keys separate run status from history list state.
   */
  it('creates stable extraction run and history keys', () => {
    expect(archonQueryKeys.extraction.run({ runId: 'run-1' })).toEqual(['archonApi', 'extraction', 'runs', 'run-1']);
    expect(archonQueryKeys.extraction.history({ take: 10, repositoryStableKey: 'repository://one' })).toEqual([
      'archonApi',
      'extraction',
      'histories',
      { repositoryStableKey: 'repository://one', take: 10 },
    ]);
  });

  /**
   * Confirms snapshot lifecycle keys carry scope and filter values without encoding
   * stable keys that are only used as cache identity.
   */
  it('creates stable snapshot lifecycle keys', () => {
    expect(archonQueryKeys.snapshots.lifecycle({ solutionStableKey: 'solution://one', status: 'Completed', take: 25 })).toEqual([
      'archonApi',
      'snapshots',
      'lists',
      { solutionStableKey: 'solution://one', status: 'Completed', take: 25 },
    ]);
    expect(archonQueryKeys.snapshots.byStableKey('snapshot://repo/current')).toEqual(['archonApi', 'snapshots', 'byStableKey', 'snapshot://repo/current']);
  });

  /**
   * Confirms representative later query areas include their scope, snapshot selector,
   * paging, and search/filter values in cache identity.
   */
  it('creates stable keys for dashboard, search, project, graph, and finding state', () => {
    expect(archonQueryKeys.dashboard.summary({ repositoryStableKey: 'repository://one', snapshotSelector: 'current' })).toEqual([
      'archonApi',
      'dashboard',
      'summary',
      { repositoryStableKey: 'repository://one', snapshotSelector: 'current' },
    ]);
    expect(archonQueryKeys.search.results({ searchText: 'controller', page: 2, pageSize: 20, snapshotSelector: { snapshotStableKey: 'snapshot://one' } })).toEqual([
      'archonApi',
      'search',
      'results',
      { page: 2, pageSize: 20, searchText: 'controller', snapshotSelector: { snapshotStableKey: 'snapshot://one' } },
    ]);
    expect(archonQueryKeys.projects.catalogue({ repositoryStableKey: 'repository://one', pageSize: 50 })).toEqual([
      'archonApi',
      'projects',
      'catalogue',
      { pageSize: 50, repositoryStableKey: 'repository://one' },
    ]);
    expect(archonQueryKeys.graph.neighbourhood({ nodeStableKey: 'node://one', depth: 2 })).toEqual(['archonApi', 'graph', 'neighbourhood', { depth: 2, nodeStableKey: 'node://one' }]);
    expect(archonQueryKeys.findings.list({ ruleCode: 'ARCH001', severity: 'High' })).toEqual(['archonApi', 'findings', 'list', { ruleCode: 'ARCH001', severity: 'High' }]);
  });
});

/**
 * Verifies query-key object normalization and cache invalidation selectors.
 */
describe('query-key normalization and invalidation helpers', () => {
  /**
   * Confirms undefined filters are removed and nested object keys are sorted.
   */
  it('normalizes object key segments deterministically', () => {
    const normalized = stableObject({ z: undefined, b: 2, a: { d: undefined, c: 'value' } });

    expect(normalized).toEqual({ a: { c: 'value' }, b: 2 });
  });

  /**
   * Confirms extraction invalidation selectors target family, history, and exact run state.
   */
  it('creates extraction invalidation selectors', () => {
    expect(getExtractionInvalidationKeys('run-1')).toEqual({
      all: ['archonApi', 'extraction'],
      histories: ['archonApi', 'extraction', 'histories'],
      run: ['archonApi', 'extraction', 'runs', 'run-1'],
    });
  });

  /**
   * Confirms snapshot invalidation selectors target family, list, and exact snapshot state.
   */
  it('creates snapshot invalidation selectors', () => {
    expect(getSnapshotInvalidationKeys('snapshot://repo/current')).toEqual({
      all: ['archonApi', 'snapshots'],
      lists: ['archonApi', 'snapshots', 'lists'],
      snapshot: ['archonApi', 'snapshots', 'byStableKey', 'snapshot://repo/current'],
    });
  });
});