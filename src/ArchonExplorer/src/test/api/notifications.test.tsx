import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { ApplicationProviders } from '@/providers/ApplicationProviders';
import {
  createErrorNotification,
  createOperationNotification,
  NotificationProvider,
  useNotifications,
  type NotificationCategory,
} from '@/providers/NotificationProvider';
import { createNormalizedError } from '@/api/errors';

/**
 * Captures notification runtime data during server rendering without needing a browser DOM.
 */
interface RuntimeSnapshot {
  /**
   * Stores the number of notifications currently visible through the provider.
   */
  readonly notificationCount: number;

  /**
   * Stores one generated notification title so tests can prove helper methods are available.
   */
  readonly generatedTitle: string;
}

/**
 * Renders a runtime consumer under NotificationProvider and returns the captured runtime evidence.
 *
 * @returns A snapshot proving provider composition and hook availability.
 */
function renderNotificationConsumer(): RuntimeSnapshot {
  // The test uses React server rendering because the project intentionally avoids a DOM test
  // environment dependency for this runtime slice while still exercising provider composition.
  let snapshot: RuntimeSnapshot | undefined;

  /**
   * Reads the runtime from context during render and records safe observable state.
   *
   * @returns A small element that allows server rendering to execute the hook path.
   */
  function RuntimeConsumer() {
    // Calling the helper during render is acceptable in this test-only consumer because the
    // helper payload is pure; the provider's stateful notify methods are not invoked here.
    const runtime = useNotifications();
    const generated = createOperationNotification('success', { operationName: 'Snapshot deleted' });
    snapshot = {
      notificationCount: runtime.notifications.length,
      generatedTitle: generated.title,
    };

    return <span>{generated.title}</span>;
  }

  renderToStaticMarkup(
    <NotificationProvider>
      <RuntimeConsumer />
    </NotificationProvider>,
  );

  if (snapshot === undefined) {
    throw new Error('Notification runtime snapshot was not captured.');
  }

  return snapshot;
}

/**
 * Verifies notification categories and safe operation-message shaping.
 */
describe('notification operation helpers', () => {
  /**
   * Confirms every supported category can create a safe operation notification payload.
   */
  it('creates safe operation notifications for each supported category', () => {
    const categories: readonly NotificationCategory[] = ['success', 'information', 'warning', 'error'];

    const notifications = categories.map((category) => createOperationNotification(category, {
      operationName: `${category} operation`,
      detail: 'Safe runtime detail.',
    }));

    expect(notifications.map((notification) => notification.category)).toEqual(categories);
    expect(notifications.every((notification) => notification.description === 'Safe runtime detail.')).toBe(true);
  });

  /**
   * Confirms helper-authored messages are still sanitized before they can reach UI state.
   */
  it('suppresses unsafe operation details', () => {
    const notification = createOperationNotification('warning', {
      operationName: 'Snapshot warning',
      detail: 'System.Exception at GraphClient Password=secret',
    });

    expect(notification.description).toBe('The operation could not be described safely.');
    expect(notification.description).not.toContain('Password=secret');
  });
});

/**
 * Verifies normalized API errors become safe notification payloads.
 */
describe('notification error helpers', () => {
  /**
   * Confirms normalized error copy and safe support metadata are preserved for error notifications.
   */
  it('converts normalized errors into safe error notifications', () => {
    const error = createNormalizedError({
      category: 'conflict',
      message: 'Snapshot is still active.',
      code: 'ARCHON_SNAPSHOT_ACTIVE',
      traceIdentifier: 'trace-7',
      retryable: false,
    });

    const notification = createErrorNotification(error, {
      operationName: 'Delete snapshot',
      requiresPersistentDisplay: true,
    });

    expect(notification).toMatchObject({
      category: 'error',
      title: 'Delete snapshot',
      description: 'Snapshot is still active.',
      requiresPersistentDisplay: true,
      metadata: {
        code: 'ARCHON_SNAPSHOT_ACTIVE',
        traceIdentifier: 'trace-7',
      },
    });
  });

  /**
   * Confirms unsafe backend diagnostics remain suppressed even if a malformed normalized error is supplied.
   */
  it('suppresses unsafe normalized error diagnostics', () => {
    const error = createNormalizedError({
      category: 'server',
      message: 'Neo4j driver failed with Password=secret',
      retryable: true,
    });

    const notification = createErrorNotification(error, { operationName: 'Start extraction' });

    expect(notification.description).toBe('Archon API could not complete the request.');
    expect(notification.description).not.toContain('Neo4j');
    expect(notification.description).not.toContain('Password=secret');
  });
});

/**
 * Verifies provider composition and accessible server-rendered notification structure.
 */
describe('notification provider composition', () => {
  /**
   * Confirms the provider exposes the runtime hook under its own composition boundary.
   */
  it('provides a notification runtime to descendants', () => {
    const snapshot = renderNotificationConsumer();

    expect(snapshot.notificationCount).toBe(0);
    expect(snapshot.generatedTitle).toBe('Snapshot deleted');
  });

  /**
   * Confirms the application provider tree includes the notification provider and stable live region.
   */
  it('renders the notification viewport through application providers', () => {
    const markup = renderToStaticMarkup(
      <ApplicationProviders>
        <span>Application child</span>
      </ApplicationProviders>,
    );

    expect(markup).toContain('Application child');
    expect(markup).toContain('aria-label="Application notifications"');
    expect(markup).toContain('aria-live="polite"');
  });
});
