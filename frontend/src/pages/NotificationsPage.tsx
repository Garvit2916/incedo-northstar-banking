import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { notificationsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { Icon } from '../components/icons';
import type { IconName } from '../components/icons';
import { SkeletonTable } from '../components/Skeleton';
import {
  Card,
  EmptyState,
  ErrorBanner,
  Pagination,
  Segmented,
} from '../components/ui';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { formatDate, formatRelative } from '../lib/format';
import type { NotificationType } from '../api/types';

const PAGE_SIZE = 12;

const TYPE_ICON: Record<NotificationType, IconName> = {
  ApplicationSubmitted: 'send',
  ApplicationUnderReview: 'clock',
  AdditionalInformationRequired: 'alertTriangle',
  ApplicationApproved: 'checkCircle',
  ApplicationRejected: 'alertCircle',
  DocumentReviewed: 'file',
  TicketUpdated: 'support',
  General: 'bell',
};

export function NotificationsPage() {
  const { hasRole } = useAuth();
  const [filter, setFilter] = useState<'all' | 'unread'>('all');
  const [page, setPage] = useState(1);
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const list = useAsync(
    () => notificationsApi.list(filter === 'unread', page, PAGE_SIZE),
    [filter, page],
  );
  const unread = useAsync(() => notificationsApi.unreadCount(), []);

  useEffect(() => {
    if (list.data && list.data.items.length === 0 && page > 1) {
      setPage((current) => Math.max(1, current - 1));
    }
  }, [list.data, page]);

  function changeFilter(next: string) {
    setFilter(next === 'unread' ? 'unread' : 'all');
    setPage(1);
  }

  function refreshAll() {
    list.reload();
    unread.reload();
  }

  async function markRead(id: string) {
    setBusy(true);
    try {
      await notificationsApi.markRead(id);
      refreshAll();
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
      refreshAll();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to update notifications.');
    } finally {
      setBusy(false);
    }
  }

  const unreadCount = unread.data ?? 0;
  const items = list.data?.items ?? [];

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <p className="eyebrow">Inbox</p>
          <h1 className="page-title">Notifications</h1>
          <p className="page-description">
            Updates about your applications, documents and support conversations.
          </p>
        </div>
        <div className="page-actions">
          <button
            type="button"
            className="btn btn-secondary"
            disabled={busy || unreadCount === 0}
            onClick={() => void markAllRead()}
          >
            <Icon name="checkCircle" size={16} />
            Mark all read
          </button>
        </div>
      </div>

      <div className="filter-bar">
        <Segmented
          ariaLabel="Filter notifications"
          value={filter}
          onChange={changeFilter}
          options={[
            { value: 'all', label: 'All notifications' },
            { value: 'unread', label: `Unread${unreadCount > 0 ? ` (${unreadCount})` : ''}` },
          ]}
        />
        {list.data && (
          <span className="muted text-xs">
            {list.data.totalCount} notification{list.data.totalCount === 1 ? '' : 's'}
          </span>
        )}
      </div>

      {list.error ? (
        <ErrorBanner message={list.error} onRetry={list.reload} />
      ) : list.loading ? (
        <Card flush>
          <SkeletonTable rows={5} />
        </Card>
      ) : items.length === 0 ? (
        <Card>
          <EmptyState
            icon="bell"
            title={filter === 'unread' ? 'You are all caught up' : 'No notifications yet'}
            message={
              filter === 'unread'
                ? 'There are no unread notifications. New updates will appear here.'
                : 'Updates about your applications and account will appear here.'
            }
            action={
              filter === 'unread' ? (
                <button type="button" className="btn btn-secondary btn-sm" onClick={() => changeFilter('all')}>
                  View all notifications
                </button>
              ) : undefined
            }
          />
        </Card>
      ) : (
        <>
          <Card flush>
            <ul className="notification-list">
              {items.map((notification) => (
                <li
                  key={notification.id}
                  className={`notification-item ${notification.isRead ? '' : 'unread'}`.trim()}
                >
                  <span className="notification-icon" aria-hidden="true">
                    <Icon name={TYPE_ICON[notification.type] ?? 'bell'} size={17} />
                  </span>
                  <div className="notification-main">
                    <div className="notification-head">
                      <strong>{notification.title}</strong>
                      {!notification.isRead && <span className="badge badge--accent">New</span>}
                    </div>
                    <p className="notification-body">{notification.body}</p>
                    <div className="notification-meta">
                      <span title={formatDate(notification.createdAtUtc)}>
                        <Icon name="clock" size={13} />
                        {formatRelative(notification.createdAtUtc)}
                      </span>
                      {notification.applicationId && hasRole('Customer') && (
                        <Link
                          to={`/applications/${notification.applicationId}`}
                          className="link-button text-xs"
                        >
                          Open application
                          <Icon name="arrowUpRight" size={13} />
                        </Link>
                      )}
                    </div>
                  </div>
                  <div className="notification-actions">
                    {!notification.isRead ? (
                      <button
                        type="button"
                        className="btn btn-ghost btn-sm"
                        disabled={busy}
                        onClick={() => void markRead(notification.id)}
                      >
                        <Icon name="check" size={14} />
                        Mark read
                      </button>
                    ) : (
                      <span className="muted text-xs">Read</span>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          </Card>

          {list.data && (
            <Pagination
              page={page}
              pageSize={PAGE_SIZE}
              total={list.data.totalCount}
              onPage={setPage}
            />
          )}
        </>
      )}
    </div>
  );
}
