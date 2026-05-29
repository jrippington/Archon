import { describe, expect, it } from 'vitest';
import { archonApiRoutes } from '@/api/archonApiRoutes';
import {
  ArchonApiClientTestDouble,
  createExtractionRunStatus,
  createSnapshotLifecycleItem,
  defaultRunId,
} from '@/api/testDoubles';

/**
 * Verifies deterministic runtime test doubles for API-client consumers.
 */
describe('ArchonApiClientTestDouble', () => {
  /**
   * Confirms health and readiness responses use typed contracts and recorded route-catalog paths.
   */
  it('provides health and readiness responses using route catalog paths', async () => {
    const client = new ArchonApiClientTestDouble();

    const health = await client.getHealth();
    const readiness = await client.getReadiness();

    expect(health.ok && health.data.status).toBe('Healthy');
    expect(readiness.ok && readiness.data.status).toBe('Ready');
    expect(client.requests).toEqual([
      { operation: 'getHealth', method: 'GET', path: archonApiRoutes.operations.health },
      { operation: 'getReadiness', method: 'GET', path: archonApiRoutes.operations.ready },
    ]);
  });

  /**
   * Confirms extraction status, history, and start behavior can drive future journey tests.
   */
  it('provides deterministic extraction run status and history', async () => {
    const client = new ArchonApiClientTestDouble({ extractionRuns: { [defaultRunId]: createExtractionRunStatus({ status: 'Completed' }) } });

    const status = await client.getExtractionStatus(defaultRunId);
    const history = await client.getExtractionHistory({ take: 1 });
    const started = await client.startExtraction({ repositoryRootDirectory: 'D:/repo', requestedBy: 'tester' });

    expect(status.ok && status.data.status).toBe('Completed');
    expect(history.ok && history.data.runs).toHaveLength(1);
    expect(started.ok && started.data.runId).toBe('run-tester');
    expect(client.requests.map((request) => request.path)).toEqual([
      archonApiRoutes.extraction.byRunId(defaultRunId),
      archonApiRoutes.extraction.runs,
      archonApiRoutes.extraction.start,
    ]);
  });

  /**
   * Confirms missing extraction runs return a safe not-found result without raw diagnostics.
   */
  it('returns safe not-found failures for missing extraction runs', async () => {
    const client = new ArchonApiClientTestDouble({ extractionRuns: {} });

    const result = await client.getExtractionStatus('missing-run');

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.category).toBe('notFound');
      expect(result.error.message).not.toContain('Password=');
    }
  });

  /**
   * Confirms snapshot lifecycle listing applies deterministic filters and route recording.
   */
  it('provides filtered snapshot lifecycle responses', async () => {
    const client = new ArchonApiClientTestDouble({
      snapshots: [
        createSnapshotLifecycleItem({ snapshotStableKey: 'snapshot://one', repositoryStableKey: 'repository://one' }),
        createSnapshotLifecycleItem({ snapshotStableKey: 'snapshot://two', repositoryStableKey: 'repository://two' }),
      ],
    });

    const result = await client.listSnapshots({ repositoryStableKey: 'repository://one', take: 5 });

    expect(result.ok && result.data.items.map((item) => item.snapshotStableKey)).toEqual(['snapshot://one']);
    expect(client.requests[0]).toMatchObject({ operation: 'listSnapshots', method: 'GET', path: archonApiRoutes.management.snapshots });
  });

  /**
   * Confirms delete-one snapshot behavior mutates deterministic state and records encoded routes.
   */
  it('deletes one snapshot deterministically', async () => {
    const client = new ArchonApiClientTestDouble({ snapshots: [createSnapshotLifecycleItem({ snapshotStableKey: 'snapshot://one' })] });

    const deletion = await client.deleteSnapshot('snapshot://one');
    const list = await client.listSnapshots();

    expect(deletion.ok && deletion.data.deleted).toBe(true);
    expect(list.ok && list.data.items).toHaveLength(0);
    expect(client.requests[0]).toMatchObject({ operation: 'deleteSnapshot', method: 'DELETE', path: archonApiRoutes.management.snapshotByStableKey('snapshot://one') });
  });

  /**
   * Confirms delete-all snapshot behavior enforces the confirmation phrase and removes seeded rows.
   */
  it('requires confirmation before deleting all snapshots', async () => {
    const client = new ArchonApiClientTestDouble({ snapshots: [createSnapshotLifecycleItem({ snapshotStableKey: 'snapshot://one' })] });

    const rejected = await client.deleteAllSnapshots({ confirmation: 'wrong' });
    const accepted = await client.deleteAllSnapshots({ confirmation: 'delete-all-snapshots', requestedBy: 'tester' });
    const list = await client.listSnapshots();

    expect(rejected.ok).toBe(false);
    if (!rejected.ok) {
      expect(rejected.error.category).toBe('validation');
    }
    expect(accepted.ok && accepted.data.deletedSnapshotCount).toBe(1);
    expect(list.ok && list.data.totalCount).toBe(0);
    expect(client.requests[1]).toMatchObject({ operation: 'deleteAllSnapshots', method: 'POST', path: archonApiRoutes.management.deleteAllSnapshots });
  });
});