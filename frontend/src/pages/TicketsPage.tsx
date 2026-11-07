import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ticketsApi } from '../api/endpoints';
import { Icon } from '../components/icons';
import { SkeletonTable } from '../components/Skeleton';
import { SearchInput } from '../components/forms';
import {
  Card,
  EmptyState,
  ErrorBanner,
  Pagination,
  StatusBadge,
} from '../components/ui';
import { useAsync } from '../lib/useAsync';
import { formatRelative, humanise } from '../lib/format';
import type { TicketPriority, TicketStatus } from '../api/types';

const PAGE_SIZE = 12;

const STATUS_OPTIONS: TicketStatus[] = [
  'New',
  'Open',
  'PendingCustomer',
  'PendingInternal',
  'Escalated',
  'Resolved',
  'Closed',
];

const PRIORITY_OPTIONS: TicketPriority[] = ['Low', 'Normal', 'High', 'Urgent'];

export function TicketsPage() {
  const [status, setStatus] = useState<TicketStatus | ''>('');
  const [priority, setPriority] = useState<TicketPriority | ''>('');
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(1);
    }, 300);
    return () => window.clearTimeout(timeout);
  }, [searchInput]);

  const { data, loading, error, reload } = useAsync(
    () =>
      ticketsApi.list({
        status: status || undefined,
        priority: priority || undefined,
        search: search || undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
    [status, priority, search, page],
  );

  const hasFilters = Boolean(status || priority || search);

  function resetFilters() {
    setStatus('');
    setPriority('');
    setSearchInput('');
    setPage(1);
  }

  const items = data?.items ?? [];

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <p className="eyebrow">Operations</p>
          <h1 className="page-title">Support tickets</h1>
          <p className="page-description">
            Triage customer conversations, respond with AI assistance and keep SLAs on track.
          </p>
        </div>
        <div className="page-actions">
          <span className="badge badge--neutral">
            <Icon name="support" size={13} />
            {data ? `${data.totalCount} ticket${data.totalCount === 1 ? '' : 's'}` : 'Loading…'}
          </span>
        </div>
      </div>

      <div className="filter-bar">
        <div className="filter-controls">
          <SearchInput
            id="ticket-search"
            label="Search tickets"
            placeholder="Search reference, subject or customer"
            value={searchInput}
            onChange={setSearchInput}
          />
          <label className="field field-inline">
            <span>Status</span>
            <select
              value={status}
              onChange={(event) => {
                setStatus(event.target.value as TicketStatus | '');
                setPage(1);
              }}
            >
              <option value="">All statuses</option>
              {STATUS_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {humanise(option)}
                </option>
              ))}
            </select>
          </label>
          <label className="field field-inline">
            <span>Priority</span>
            <select
              value={priority}
              onChange={(event) => {
                setPriority(event.target.value as TicketPriority | '');
                setPage(1);
              }}
            >
              <option value="">All priorities</option>
              {PRIORITY_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </label>
          {hasFilters && (
            <button type="button" className="btn btn-ghost btn-sm" onClick={resetFilters}>
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
          <SkeletonTable rows={6} />
        </Card>
      ) : items.length === 0 ? (
        <Card>
          <EmptyState
            icon="support"
            title={hasFilters ? 'No tickets match your filters' : 'No tickets yet'}
            message={
              hasFilters
                ? 'Try a different search term, status or priority.'
                : 'Customer conversations will appear here when they are raised.'
            }
            action={
              hasFilters ? (
                <button type="button" className="btn btn-secondary btn-sm" onClick={resetFilters}>
                  Clear filters
                </button>
              ) : undefined
            }
          />
        </Card>
      ) : (
        <>
          <div className="ticket-cards">
            {items.map((ticket) => {
              const overdue =
                ticket.slaDueAtUtc &&
                new Date(ticket.slaDueAtUtc).getTime() < Date.now() &&
                ticket.status !== 'Resolved' &&
                ticket.status !== 'Closed';

              return (
                <Link className="ticket-card" to={`/tickets/${ticket.id}`} key={ticket.id}>
                  <div className="ticket-card-head">
                    <span className="ticket-ref">
                      <Icon name="support" size={13} />
                      {ticket.reference}
                    </span>
                    <span className="ticket-card-badges">
                      <span className="badge badge--neutral">{humanise(ticket.category)}</span>
                      <StatusBadge value={ticket.priority} dot={false} />
                    </span>
                  </div>
                  <h2 className="ticket-subject">{ticket.subject}</h2>
                  <div className="ticket-card-foot">
                    <span>
                      <Icon name="message" size={14} />
                      {ticket.messageCount} message{ticket.messageCount === 1 ? '' : 's'}
                    </span>
                    <span className={overdue ? 'text-danger' : undefined}>
                      <Icon name="clock" size={14} />
                      {ticket.slaDueAtUtc
                        ? `SLA ${formatRelative(ticket.slaDueAtUtc)}`
                        : 'No SLA'}
                    </span>
                    <span>
                      <Icon name="activity" size={14} />
                      Updated {formatRelative(ticket.updatedAtUtc)}
                    </span>
                    <span className="ticket-card-status">
                      <StatusBadge value={ticket.status} />
                      <Icon name="chevronRight" size={16} />
                    </span>
                  </div>
                </Link>
              );
            })}
          </div>

          {data && (
            <Pagination page={page} pageSize={PAGE_SIZE} total={data.totalCount} onPage={setPage} />
          )}
        </>
      )}
    </div>
  );
}
