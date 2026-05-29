import { describe, expect, it } from 'vitest';
import { archonApiRoutes, collectArchonApiRouteSamples } from '@/api/archonApiRoutes';

/**
 * Verifies that the route catalog preserves the operational endpoint paths that
 * ArchonExplorer uses before feature-specific screens exist.
 */
describe('archonApiRoutes operations and operational workflows', () => {
  /**
   * Confirms liveness-style and readiness-style frontend probes target the exact
   * public ArchonApi routes rather than inventing a prefixed browser contract.
   */
  it('exposes health and readiness routes without an api prefix', () => {
    expect(archonApiRoutes.operations.health).toBe('/health');
    expect(archonApiRoutes.operations.ready).toBe('/ready');
  });

  /**
   * Confirms extraction route constants and builders match the API module routes
   * and encode the run identifier path value defensively.
   */
  it('exposes extraction routes and encodes run identifiers', () => {
    expect(archonApiRoutes.extraction.runs).toBe('/extractions');
    expect(archonApiRoutes.extraction.start).toBe('/extractions');
    expect(archonApiRoutes.extraction.byRunId('run id/with spaces')).toBe('/extractions/run%20id%2Fwith%20spaces');
  });

  /**
   * Confirms management constants and builders cover snapshot lifecycle and run
   * history operations while preserving stable-key characters through encoding.
   */
  it('exposes management routes and encodes snapshot stable keys', () => {
    expect(archonApiRoutes.management.snapshots).toBe('/management/snapshots');
    expect(archonApiRoutes.management.deleteAllSnapshots).toBe('/management/snapshots/delete-all');
    expect(archonApiRoutes.management.runs).toBe('/management/runs');
    expect(archonApiRoutes.management.snapshotByStableKey('snapshot://repo/solution#1')).toBe('/management/snapshots/snapshot%3A%2F%2Frepo%2Fsolution%231');
  });
});

/**
 * Verifies that query route groups remain represented as constants or builders
 * so later work packages do not duplicate string literals inside feature code.
 */
describe('archonApiRoutes query groups', () => {
  /**
   * Checks representative constants from every implemented query route group and
   * confirms catch-all builders encode slash-like stable keys as single values.
   */
  it('exposes representative routes for each query area', () => {
    expect(archonApiRoutes.dashboard.summary).toBe('/dashboard-summary');
    expect(archonApiRoutes.projects.list).toBe('/projects');
    expect(archonApiRoutes.projects.detail).toBe('/projects/detail');
    expect(archonApiRoutes.projects.byStableKey('project://repo/src/App.csproj')).toBe('/projects/project%3A%2F%2Frepo%2Fsrc%2FApp.csproj');
    expect(archonApiRoutes.graphTraversal.directDependencies).toBe('/dependencies/direct');
    expect(archonApiRoutes.symbols.search).toBe('/symbols');
    expect(archonApiRoutes.runtime.endpoints).toBe('/runtime/endpoints');
    expect(archonApiRoutes.facts.dataAccess).toBe('/data-access');
    expect(archonApiRoutes.evidence.detail).toBe('/evidence/detail');
    expect(archonApiRoutes.rules.list).toBe('/rules');
    expect(archonApiRoutes.rules.byIdentity('ARCH001', '1.0/preview')).toBe('/rules/ARCH001/1.0%2Fpreview');
    expect(archonApiRoutes.findings.hotlist).toBe('/hotlist');
    expect(archonApiRoutes.findings.byStableKey('snapshot://repo/current', 'finding://rule/target')).toBe('/findings/snapshot%3A%2F%2Frepo%2Fcurrent/finding%3A%2F%2Frule%2Ftarget');
    expect(archonApiRoutes.findings.historyByKey('history://rule/target')).toBe('/findings/history/history%3A%2F%2Frule%2Ftarget');
    expect(archonApiRoutes.metrics.snapshotByStableKey('snapshot://repo/current')).toBe('/snapshots/snapshot%3A%2F%2Frepo%2Fcurrent/metrics');
    expect(archonApiRoutes.cycles.snapshotCycles).toBe('/snapshot-cycles');
    expect(archonApiRoutes.hotspots.snapshotHotspots).toBe('/snapshot-hotspots');
    expect(archonApiRoutes.architectureRules.snapshotArchitectureRules).toBe('/snapshot-architecture-rules');
    expect(archonApiRoutes.diff.snapshot).toBe('/snapshot-diff');
    expect(archonApiRoutes.search.all).toBe('/search');
  });
});

/**
 * Verifies the repository-wide no-common-`/api` convention across exported route
 * constants and route builders by sampling every catalog branch.
 */
describe('collectArchonApiRouteSamples', () => {
  /**
   * Ensures every collected route sample starts at the public ArchonApi root and
   * never at a duplicated `/api` prefix.
   */
  it('collects only routes that avoid the common api prefix', () => {
    const routes = collectArchonApiRouteSamples();

    expect(routes.length).toBeGreaterThan(0);
    expect(routes).not.toContain('/api');
    expect(routes.filter((route) => route === '/api' || route.startsWith('/api/'))).toEqual([]);
  });
});
