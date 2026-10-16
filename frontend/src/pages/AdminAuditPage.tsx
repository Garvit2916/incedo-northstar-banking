import { useState } from 'react';
import { adminApi } from '../api/endpoints';
import { Badge, Card, EmptyState, ErrorBanner } from '../components/ui';
import { SkeletonTable } from '../components/Skeleton';
import { useAsync } from '../lib/useAsync';
import { formatDate, humanise } from '../lib/format';

export function AdminAuditPage() {
  const [action, setAction] = useState('');
  const [actorUserId, setActorUserId] = useState('');
  const [page, setPage] = useState(1);

  const { data, loading, error } = useAsync(
    () => adminApi.audit({ action: action || undefined, actorUserId: actorUserId || undefined, page, pageSize: 25 }),
    [action, actorUserId, page],
  );

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>Audit trail</h1>
          <p className="muted">Immutable record of security-relevant actions.</p>
        </div>
      </header>

      <div className="filter-bar">
        <label className="field">
          <span>Action</span>
          <input
            placeholder="e.g. application.approved"
            value={action}
            onChange={(e) => {
              setPage(1);
              setAction(e.target.value);
            }}
          />
        </label>
        <label className="field">
          <span>Actor user id</span>
          <input
            placeholder="GUID"
            value={actorUserId}
            onChange={(e) => {
              setPage(1);
              setActorUserId(e.target.value);
            }}
          />
        </label>
      </div>

      {loading ? (
        <Card>
          <SkeletonTable rows={8} />
        </Card>
      ) : error ? (
        <ErrorBanner message={error} />
      ) : (
        <>
          <Card>
            {!data || data.items.length === 0 ? (
              <EmptyState message="No audit events match the current filter." />
            ) : (
              <table className="table">
                <thead>
                  <tr>
                    <th>When</th>
                    <th>Action</th>
                    <th>Actor role</th>
                    <th>Entity</th>
                    <th>IP</th>
                    <th>Outcome</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((event) => (
                    <tr key={event.id}>
                      <td>{formatDate(event.createdAtUtc)}</td>
                      <td>{event.action}</td>
                      <td>{event.actorRole ? humanise(event.actorRole) : '—'}</td>
                      <td>
                        {event.entityType
                          ? `${event.entityType}${event.entityId ? ` · ${event.entityId}` : ''}`
                          : '—'}
                      </td>
                      <td>{event.ipAddress ?? '—'}</td>
                      <td>
                        <Badge value={event.succeeded ? 'Approved' : 'Rejected'} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Card>

          {data && data.totalPages > 1 && (
            <div className="pagination">
              <button
                type="button"
                className="btn btn-ghost"
                disabled={page <= 1}
                onClick={() => setPage((value) => Math.max(1, value - 1))}
              >
                Previous
              </button>
              <span className="muted">
                Page {data.page} of {data.totalPages} · {data.totalCount} events
              </span>
              <button
                type="button"
                className="btn btn-ghost"
                disabled={page >= data.totalPages}
                onClick={() => setPage((value) => value + 1)}
              >
                Next
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
