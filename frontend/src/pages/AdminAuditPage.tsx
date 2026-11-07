import { useEffect, useState } from 'react';
import { adminApi } from '../api/endpoints';
import { Icon } from '../components/icons';
import { SkeletonTable } from '../components/Skeleton';
import {
  Card,
  EmptyState,
  ErrorBanner,
  Pagination,
  StatusBadge,
} from '../components/ui';
import { useAsync } from '../lib/useAsync';
import { formatDate, formatRelative, humanise } from '../lib/format';

const PAGE_SIZE = 25;

export function AdminAuditPage() {
  const [actionInput, setActionInput] = useState('');
  const [actorInput, setActorInput] = useState('');
  const [action, setAction] = useState('');
  const [actorUserId, setActorUserId] = useState('');
  const [page, setPage] = useState(1);

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      setAction(actionInput.trim());
      setActorUserId(actorInput.trim());
      setPage(1);
    }, 300);
    return () => window.clearTimeout(timeout);
  }, [actionInput, actorInput]);

  const { data, loading, error, reload } = useAsync(
    () =>
      adminApi.audit({
        action: action || undefined,
        actorUserId: actorUserId || undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
    [action, actorUserId, page],
  );

  const hasFilters = Boolean(action || actorUserId);
  const items = data?.items ?? [];

  function clearFilters() {
    setActionInput('');
    setActorInput('');
    setAction('');
    setActorUserId('');
    setPage(1);
  }

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <p className="eyebrow">Administration</p>
          <h1 className="page-title">Audit trail</h1>
          <p className="page-description">
            Immutable record of security-relevant actions across the platform.
          </p>
        </div>
        <div className="page-actions">
          <span className="badge badge--neutral">
            <Icon name="audit" size={13} />
            {data ? `${data.totalCount} events` : 'Loading…'}
          </span>
        </div>
      </div>

      <div className="filter-bar">
        <div className="filter-controls">
          <label className="field field-inline">
            <span>Action</span>
            <input
              placeholder="e.g. application.approved"
              value={actionInput}
              onChange={(event) => setActionInput(event.target.value)}
            />
          </label>
          <label className="field field-inline">
            <span>Actor user ID</span>
            <input
              placeholder="GUID"
              value={actorInput}
              onChange={(event) => setActorInput(event.target.value)}
            />
          </label>
          {hasFilters && (
            <button type="button" className="btn btn-ghost btn-sm" onClick={clearFilters}>
              <Icon name="close" size={14} />
              Clear
            </button>
          )}
        </div>
      </div>

      {error ? (
        <ErrorBanner message={error} onRetry={reload} />
      ) : loading ? (
        <Card flush>
          <SkeletonTable rows={8} />
        </Card>
      ) : items.length === 0 ? (
        <Card>
          <EmptyState
            icon="audit"
            title={hasFilters ? 'No audit events match' : 'No audit events yet'}
            message={
              hasFilters
                ? 'Try a different action name or actor user ID.'
                : 'Security-relevant actions will be recorded here as they occur.'
            }
            action={
              hasFilters ? (
                <button type="button" className="btn btn-secondary btn-sm" onClick={clearFilters}>
                  Clear filters
                </button>
              ) : undefined
            }
          />
        </Card>
      ) : (
        <>
          <Card flush>
            <div className="table-scroll">
              <table className="table">
                <thead>
                  <tr>
                    <th>When</th>
                    <th>Action</th>
                    <th>Actor</th>
                    <th>Entity</th>
                    <th>IP address</th>
                    <th>Outcome</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((event) => (
                    <tr key={event.id}>
                      <td title={formatDate(event.createdAtUtc)} className="muted">
                        {formatRelative(event.createdAtUtc)}
                      </td>
                      <td>
                        <span className="audit-action mono">{event.action}</span>
                      </td>
                      <td>
                        {event.actorRole ? (
                          <span className="badge badge--accent">{humanise(event.actorRole)}</span>
                        ) : (
                          <span className="muted">System</span>
                        )}
                        {event.actorUserId && (
                          <p className="muted text-xs mono">{event.actorUserId.slice(0, 8)}</p>
                        )}
                      </td>
                      <td>
                        {event.entityType ? (
                          <span className="audit-entity">
                            {event.entityType}
                            {event.entityId && <span className="mono"> · {event.entityId.slice(0, 8)}</span>}
                          </span>
                        ) : (
                          <span className="muted">—</span>
                        )}
                      </td>
                      <td className="mono text-xs">{event.ipAddress ?? '—'}</td>
                      <td>
                        <StatusBadge
                          value={event.succeeded ? 'Approved' : 'Rejected'}
                          dot={false}
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>

          {data && (
            <Pagination page={page} pageSize={PAGE_SIZE} total={data.totalCount} onPage={setPage} />
          )}
        </>
      )}
    </div>
  );
}
