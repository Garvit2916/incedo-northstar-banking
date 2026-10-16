import { useState } from 'react';
import { notificationsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { Badge, Card, EmptyState, ErrorBanner } from '../components/ui';
import { SkeletonCard } from '../components/Skeleton';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { formatDate, humanise } from '../lib/format';

export function NotificationsPage() {
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [busy, setBusy] = useState(false);
  const { data, loading, error, reload } = useAsync(
    () => notificationsApi.list(unreadOnly),
    [unreadOnly],
  );
  const toast = useToast();

  if (error) {
    return <ErrorBanner message={error} />;
  }

  async function markRead(id: string) {
    setBusy(true);
    try {
      await notificationsApi.markRead(id);
      reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to update notification.');
    } finally {
      setBusy(false);
    }
  }

  async function markAllRead() {
    setBusy(true);
    try {
      await notificationsApi.markAllRead();
      toast.success('All notifications marked as read.');
      reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to update notifications.');
    } finally {
      setBusy(false);
    }
  }

  const hasUnread = data?.items.some((notification) => !notification.isRead) ?? false;

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>Notifications</h1>
          <p className="muted">Updates about your applications and account.</p>
        </div>
        <div className="card-actions">
          <label className="checkbox">
            <input
              type="checkbox"
              checked={unreadOnly}
              onChange={(e) => setUnreadOnly(e.target.checked)}
            />
            Unread only
          </label>
          <button
            type="button"
            className="btn btn-ghost"
            disabled={busy || !hasUnread}
            onClick={markAllRead}
          >
            Mark all read
          </button>
        </div>
      </header>

      {loading || !data ? (
        <Card>
          <div className="stack">
            <SkeletonCard />
            <SkeletonCard />
            <SkeletonCard />
          </div>
        </Card>
      ) : (
        <Card>
          {data.items.length === 0 ? (
            <EmptyState message="No notifications." />
          ) : (
            <ul className="notification-list">
              {data.items.map((notification) => (
                <li key={notification.id} className={notification.isRead ? 'notification' : 'notification unread'}>
                  <div className="notification-main">
                    <div className="notification-head">
                      <strong>{notification.title}</strong>
                      <Badge value={notification.type} />
                    </div>
                    <p>{notification.body}</p>
                    <span className="muted">
                      {humanise(notification.type)} · {formatDate(notification.createdAtUtc)}
                    </span>
                  </div>
                  {!notification.isRead && (
                    <button
                      type="button"
                      className="btn btn-ghost"
                      disabled={busy}
                      onClick={() => markRead(notification.id)}
                    >
                      Mark read
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </Card>
      )}
    </div>
  );
}
