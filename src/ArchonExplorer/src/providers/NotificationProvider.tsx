import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import type { NormalizedArchonApiError } from '@/api/archonApiTypes';
import { sanitizeDiagnosticMessage } from '@/api/errors';
import { cn } from '@/lib/utils';

/**
 * Names the safe presentation categories supported by the ArchonExplorer notification runtime.
 */
export type NotificationCategory = 'success' | 'information' | 'warning' | 'error';

/**
 * Carries safe optional metadata that can be shown in or near a notification.
 */
export interface NotificationMetadata {
  /**
   * Contains a stable backend or frontend code that is safe for user-visible support context.
   */
  readonly code?: string;

  /**
   * Contains a safe trace or correlation identifier that support staff can use without exposing diagnostics.
   */
  readonly traceIdentifier?: string;
}

/**
 * Describes the caller-supplied notification request before the runtime assigns identity.
 */
export interface NotificationInput {
  /**
   * Selects the notification category and resulting accessible urgency.
   */
  readonly category: NotificationCategory;

  /**
   * Provides the short safe title rendered as the primary notification text.
   */
  readonly title: string;

  /**
   * Provides optional safe supporting text for the transient message.
   */
  readonly description?: string;

  /**
   * Carries optional safe machine-readable support details.
   */
  readonly metadata?: NotificationMetadata;

  /**
   * Indicates whether the same failure must also be represented in page-level UI outside the transient notification.
   */
  readonly requiresPersistentDisplay?: boolean;
}

/**
 * Represents a notification after it has been accepted by the runtime state container.
 */
export interface NotificationMessage extends NotificationInput {
  /**
   * Identifies the notification instance for rendering, dismissal, and test assertions.
   */
  readonly id: string;
}

/**
 * Describes the operation-copy inputs used to create common safe success, information, warning, or error messages.
 */
export interface OperationNotificationOptions {
  /**
   * Names the operation in user-facing copy without exposing routes, stack traces, or raw backend text.
   */
  readonly operationName: string;

  /**
   * Supplies optional safe detail text that has already been authored for user presentation.
   */
  readonly detail?: string;
}

/**
 * Describes the notification runtime operations exposed to feature components.
 */
export interface NotificationRuntime {
  /**
   * Contains the currently visible safe notification messages.
   */
  readonly notifications: readonly NotificationMessage[];

  /**
   * Adds a caller-authored safe notification to the runtime queue.
   *
   * @param input The safe notification details requested by a feature or runtime helper.
   * @returns The accepted notification message with its generated identity.
   */
  readonly notify: (input: NotificationInput) => NotificationMessage;

  /**
   * Adds a success notification for an operation that completed safely.
   *
   * @param options The operation name and optional safe detail shown to the user.
   * @returns The accepted success notification message.
   */
  readonly notifySuccess: (options: OperationNotificationOptions) => NotificationMessage;

  /**
   * Adds an informational notification for a safe runtime or workflow update.
   *
   * @param options The operation name and optional safe detail shown to the user.
   * @returns The accepted informational notification message.
   */
  readonly notifyInformation: (options: OperationNotificationOptions) => NotificationMessage;

  /**
   * Adds a warning notification for a recoverable operation state.
   *
   * @param options The operation name and optional safe detail shown to the user.
   * @returns The accepted warning notification message.
   */
  readonly notifyWarning: (options: OperationNotificationOptions) => NotificationMessage;

  /**
   * Adds an error notification derived from the normalized API error model.
   *
   * @param error The safe normalized error emitted by the API client foundation.
   * @param options The operation name and optional persistent-display requirement for page-level errors.
   * @returns The accepted error notification message.
   */
  readonly notifyError: (error: NormalizedArchonApiError, options: ErrorNotificationOptions) => NotificationMessage;

  /**
   * Removes one notification from the visible queue.
   *
   * @param id The runtime-generated notification identity to dismiss.
   */
  readonly dismiss: (id: string) => void;

  /**
   * Clears all currently visible notifications.
   */
  readonly clear: () => void;
}

/**
 * Describes the operation context required to convert a normalized error into a safe notification.
 */
export interface ErrorNotificationOptions extends OperationNotificationOptions {
  /**
   * Indicates whether the error also needs a durable page-level representation for accessibility and retry context.
   */
  readonly requiresPersistentDisplay?: boolean;
}

/**
 * Stores the active notification runtime for components rendered under NotificationProvider.
 */
const NotificationContext = createContext<NotificationRuntime | undefined>(undefined);

/**
 * Provides category labels that remain safe for screen-reader and test assertions.
 */
const categoryLabels: Record<NotificationCategory, string> = {
  success: 'Success',
  information: 'Information',
  warning: 'Warning',
  error: 'Error',
};

/**
 * Provides safe fallback titles for normalized error categories when operation-specific copy is absent.
 */
const errorTitles: Record<NormalizedArchonApiError['category'], string> = {
  configuration: 'Configuration needs attention',
  network: 'Archon API unavailable',
  timeout: 'Archon API request timed out',
  validation: 'Request needs attention',
  notFound: 'Requested item was not found',
  conflict: 'Operation conflicts with current state',
  server: 'Archon API operation failed',
  unexpectedResponse: 'Archon API response could not be read',
  cancelled: 'Operation cancelled',
  unknown: 'Operation could not be completed',
};

/**
 * Provides a safe fallback message for notification text that fails diagnostic sanitization.
 */
const unsafeNotificationFallback = 'The operation could not be described safely.';

/**
 * Creates a safe operation notification payload for routine runtime messages.
 *
 * @param category The notification category used for presentation and accessibility.
 * @param options The operation name and optional safe detail that should be displayed.
 * @returns A sanitized notification input that can be passed to the runtime queue.
 */
export function createOperationNotification(category: NotificationCategory, options: OperationNotificationOptions): NotificationInput {
  // Operation messages are authored by the frontend, but they still pass through the same
  // diagnostic sanitizer so accidental raw backend or route detail cannot leak through helpers.
  const title = sanitizeDiagnosticMessage(options.operationName, categoryLabels[category]);
  const description = options.detail === undefined
    ? undefined
    : sanitizeDiagnosticMessage(options.detail, unsafeNotificationFallback);

  return {
    category,
    title,
    description,
  };
}

/**
 * Converts a normalized API error into a safe transient notification payload.
 *
 * @param error The normalized API error already shaped by the request foundation.
 * @param options The operation context and persistent-display requirement for the failure.
 * @returns A sanitized error notification input that excludes raw backend diagnostics.
 */
export function createErrorNotification(error: NormalizedArchonApiError, options: ErrorNotificationOptions): NotificationInput {
  // Persistent page-level errors must be represented outside the toast stack because a
  // transient notification can be dismissed, missed by assistive technology timing, or lose
  // the retry context needed for a user to resolve the failure.
  const title = sanitizeDiagnosticMessage(options.operationName, errorTitles[error.category]);
  const detail = options.detail ?? error.message;
  const description = sanitizeDiagnosticMessage(detail, errorTitles[error.category]);

  return {
    category: 'error',
    title,
    description,
    metadata: {
      code: error.code,
      traceIdentifier: error.traceIdentifier,
    },
    requiresPersistentDisplay: options.requiresPersistentDisplay ?? false,
  };
}

/**
 * Provides notification state and helper functions to the ArchonExplorer React tree.
 *
 * @param props Contains the descendant application tree that should be able to publish notifications.
 * @param props.children The React nodes rendered inside the notification provider and viewport.
 * @returns The provider-wrapped application tree plus the accessible notification viewport.
 */
export function NotificationProvider({ children }: { readonly children: ReactNode }) {
  // State is intentionally limited to already-sanitized presentation payloads. Backend response
  // objects, thrown errors, routes, and request bodies are never stored in the notification queue.
  const [notifications, setNotifications] = useState<readonly NotificationMessage[]>([]);

  /**
   * Creates a stable-enough browser notification identity without depending on external libraries.
   *
   * @returns A notification identity unique for practical in-page rendering and dismissal.
   */
  const createNotificationId = useCallback((): string => {
    // crypto.randomUUID is preferred when available; the timestamp fallback keeps tests and
    // older runtimes functional without weakening the safe-presentation boundary.
    return globalThis.crypto?.randomUUID?.() ?? `notification-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  }, []);

  /**
   * Adds a sanitized notification message to the visible queue.
   *
   * @param input The caller-authored safe notification request.
   * @returns The accepted notification with generated identity.
   */
  const notify = useCallback((input: NotificationInput): NotificationMessage => {
    // The final sanitizer is deliberately applied at the runtime boundary so direct notify
    // calls and helper-generated calls receive identical fail-closed treatment.
    const message: NotificationMessage = {
      ...input,
      id: createNotificationId(),
      title: sanitizeDiagnosticMessage(input.title, categoryLabels[input.category]),
      description: input.description === undefined
        ? undefined
        : sanitizeDiagnosticMessage(input.description, unsafeNotificationFallback),
    };

    setNotifications((current) => [...current, message]);
    return message;
  }, [createNotificationId]);

  /**
   * Removes a notification by runtime identity.
   *
   * @param id The generated identity assigned when the notification was added.
   */
  const dismiss = useCallback((id: string): void => {
    // Dismissal is identity-based so duplicate safe text can appear without accidentally
    // removing the wrong notification.
    setNotifications((current) => current.filter((notification) => notification.id !== id));
  }, []);

  /**
   * Clears all visible notifications from the runtime queue.
   */
  const clear = useCallback((): void => {
    // Bulk clearing is useful for future route transitions or test setup without exposing
    // implementation details of the queue state.
    setNotifications([]);
  }, []);

  /**
   * Adds a safe success notification for completed operations.
   *
   * @param options The operation name and optional safe detail shown to the user.
   * @returns The accepted success notification message.
   */
  const notifySuccess = useCallback((options: OperationNotificationOptions): NotificationMessage => {
    // The category-specific wrapper keeps feature code expressive while still routing all
    // presentation text through the same sanitizer and queue boundary.
    return notify(createOperationNotification('success', options));
  }, [notify]);

  /**
   * Adds a safe informational notification for neutral runtime updates.
   *
   * @param options The operation name and optional safe detail shown to the user.
   * @returns The accepted informational notification message.
   */
  const notifyInformation = useCallback((options: OperationNotificationOptions): NotificationMessage => {
    // Information messages are transient hints; persistent setup or page-level state still
    // belongs in the owning feature surface rather than only in the toast stack.
    return notify(createOperationNotification('information', options));
  }, [notify]);

  /**
   * Adds a safe warning notification for recoverable operation states.
   *
   * @param options The operation name and optional safe detail shown to the user.
   * @returns The accepted warning notification message.
   */
  const notifyWarning = useCallback((options: OperationNotificationOptions): NotificationMessage => {
    // Warnings use the same safe operation helper so warning details cannot bypass diagnostic
    // suppression merely because they are not full API failures.
    return notify(createOperationNotification('warning', options));
  }, [notify]);

  /**
   * Adds a safe error notification derived from a normalized Archon API error.
   *
   * @param error The normalized frontend error emitted by the API client foundation.
   * @param options The operation context and persistent-display requirement for the failure.
   * @returns The accepted error notification message.
   */
  const notifyError = useCallback((error: NormalizedArchonApiError, options: ErrorNotificationOptions): NotificationMessage => {
    // Error notifications must start from the normalized error model so raw thrown values,
    // response bodies, and backend diagnostics never enter notification state.
    return notify(createErrorNotification(error, options));
  }, [notify]);

  const runtime = useMemo<NotificationRuntime>(() => ({
    notifications,
    notify,
    notifySuccess,
    notifyInformation,
    notifyWarning,
    notifyError,
    dismiss,
    clear,
  }), [clear, dismiss, notifications, notify, notifyError, notifyInformation, notifySuccess, notifyWarning]);

  return (
    <NotificationContext.Provider value={runtime}>
      {children}
      <NotificationViewport notifications={notifications} onDismiss={dismiss} />
    </NotificationContext.Provider>
  );
}

/**
 * Reads the notification runtime from the nearest provider.
 *
 * @returns The notification runtime API used by feature components and hooks.
 */
export function useNotifications(): NotificationRuntime {
  // The explicit provider check gives feature developers an actionable error if a future
  // test or route renders notification-aware code outside ApplicationProviders.
  const runtime = useContext(NotificationContext);

  if (runtime === undefined) {
    throw new Error('useNotifications must be used within NotificationProvider.');
  }

  return runtime;
}

/**
 * Describes the notification viewport rendering inputs.
 */
interface NotificationViewportProps {
  /**
   * Contains the safe notification messages currently visible to the user.
   */
  readonly notifications: readonly NotificationMessage[];

  /**
   * Removes a notification from the queue when the user dismisses it.
   *
   * @param id The notification identity selected for dismissal.
   */
  readonly onDismiss: (id: string) => void;
}

/**
 * Renders the accessible notification stack using shadcn-compatible local styling.
 *
 * @param props Contains safe notifications and the dismissal callback.
 * @param props.notifications The messages currently visible in the viewport.
 * @param props.onDismiss The callback used by each dismiss button.
 * @returns A fixed notification viewport that remains outside feature layout regions.
 */
function NotificationViewport({ notifications, onDismiss }: NotificationViewportProps) {
  // The viewport remains mounted even when empty so screen readers have a stable live region
  // target when the first notification appears.
  return (
    <section className="notification-viewport" aria-label="Application notifications" aria-live="polite">
      {notifications.map((notification) => (
        <NotificationToast key={notification.id} notification={notification} onDismiss={onDismiss} />
      ))}
    </section>
  );
}

/**
 * Describes the rendering inputs for a single notification toast.
 */
interface NotificationToastProps {
  /**
   * Contains the safe notification message to render.
   */
  readonly notification: NotificationMessage;

  /**
   * Removes the rendered notification when the user activates the dismiss button.
   *
   * @param id The rendered notification identity.
   */
  readonly onDismiss: (id: string) => void;
}

/**
 * Renders one safe notification message with accessible category and dismissal controls.
 *
 * @param props Contains the message and dismissal callback for one notification.
 * @param props.notification The safe notification message to display.
 * @param props.onDismiss The callback used when the notification is dismissed.
 * @returns A shadcn-compatible toast-style notification surface.
 */
function NotificationToast({ notification, onDismiss }: NotificationToastProps) {
  // Error notifications use alert urgency while other categories use status semantics so
  // assistive technology receives failures promptly without over-announcing routine updates.
  const role = notification.category === 'error' ? 'alert' : 'status';
  const categoryLabel = categoryLabels[notification.category];

  return (
    <article
      className={cn('notification-toast', `notification-toast--${notification.category}`)}
      role={role}
      aria-label={`${categoryLabel}: ${notification.title}`}
    >
      <div className="notification-toast__content">
        <p className="notification-toast__category">{categoryLabel}</p>
        <p className="notification-toast__title">{notification.title}</p>
        {notification.description === undefined ? null : <p className="notification-toast__description">{notification.description}</p>}
        <NotificationMetadataDetails metadata={notification.metadata} />
        {notification.requiresPersistentDisplay === true ? (
          <p className="notification-toast__persistent-note">Also shown in the page so it remains available after this notification is dismissed.</p>
        ) : null}
      </div>
      <Button
        className="notification-toast__dismiss"
        size="sm"
        variant="ghost"
        type="button"
        aria-label={`Dismiss ${notification.title}`}
        onClick={() => onDismiss(notification.id)}
      >
        Dismiss
      </Button>
    </article>
  );
}

/**
 * Renders safe support metadata for a notification when metadata is available.
 *
 * @param props Contains optional safe notification metadata.
 * @param props.metadata The safe support code or trace identifier selected for display.
 * @returns Metadata prose when safe values exist; otherwise no rendered content.
 */
function NotificationMetadataDetails({ metadata }: { readonly metadata?: NotificationMetadata }) {
  // Metadata is deliberately limited to code and trace identifiers that were already sanitized
  // by the normalized error model; raw exception text and backend detail are not accepted here.
  if (metadata?.code === undefined && metadata?.traceIdentifier === undefined) {
    return null;
  }

  return (
    <p className="notification-toast__metadata">
      {metadata.code === undefined ? null : <span>Code: {metadata.code}</span>}
      {metadata.traceIdentifier === undefined ? null : <span>Trace: {metadata.traceIdentifier}</span>}
    </p>
  );
}
