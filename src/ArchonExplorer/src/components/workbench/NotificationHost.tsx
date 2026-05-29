import { Badge } from '@/components/ui/badge';

/**
 * Renders the shell-local notification placement marker.
 *
 * @returns A concise accessible landmark that explains where shell notifications appear.
 */
export function NotificationHost() {
  // The application-level NotificationProvider owns the live toast viewport. This shell host is a
  // visible placement seam for contributors and assistive technology without creating a competing
  // notification runtime or hiding persistent page-level errors behind transient toasts.
  return (
    <section className="workbench-notification-host" aria-label="Workbench shell notifications">
      <Badge variant="outline">Notifications</Badge>
      <span>Notification host ready for safe shell feedback. Shell notifications appear as transient safe messages; persistent errors remain visible in their owning region.</span>
    </section>
  );
}
