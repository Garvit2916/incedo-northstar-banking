import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ticketsApi } from '../api/endpoints';
import { Badge, Card, EmptyState, ErrorBanner } from '../components/ui';
import { SkeletonTable } from '../components/Skeleton';
import { useAsync } from '../lib/useAsync';
import { formatDate, humanise } from '../lib/format';
import type { TicketStatus } from '../api/types';

const STATUS_OPTIONS: Array<TicketStatus | ''> = [
  '',
  'New',
  'Open',
  'PendingCustomer',
  'PendingInternal',
  'Escalated',
  'Resolved',
  'Closed',
];

export function TicketsPage() {
  const [status, setStatus] = useState<TicketStatus | ''>('');
  const [search, setSearch] = useState('');

  const { data, loading, error } = useAsync(
    () => ticketsApi.list({ status: status || undefined, search: search || undefined }),
    [status, search],
  );

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>Support tickets</h1>
          <p className="muted">AI-assisted customer support workspace.</p>
        </div>
      </header>

      <div className="filter-bar">
        <label className="field">
          <span>Status</span>
          <select value={status} onChange={(e) => setStatus(e.target.value as TicketStatus | '')}>
            {STATUS_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option === '' ? 'All' : humanise(option)}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Search</span>
          <input
            placeholder="Reference or subject"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </label>
      </div>

      {loading ? (
        <Card>
          <SkeletonTable rows={6} />
        </Card>
      ) : error ? (
        <ErrorBanner message={error} />
      ) : (
        <Card>
          {!data || data.items.length === 0 ? (
            <EmptyState message="No tickets match the current filter." />
          ) : (
            <table className="table">
              <thead>
                <tr>
                  <th>Reference</th>
                  <th>Subject</th>
                  <th>Priority</th>
                  <th>Status</th>
                  <th>Messages</th>
                  <th>SLA due</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {data.items.map((ticket) => (
                  <tr key={ticket.id}>
                    <td>{ticket.reference}</td>
                    <td>{ticket.subject}</td>
                    <td>
                      <Badge value={ticket.priority} />
                    </td>
                    <td>
                      <Badge value={ticket.status} />
                    </td>
                    <td>{ticket.messageCount}</td>
                    <td>{formatDate(ticket.slaDueAtUtc)}</td>
                    <td className="right">
                      <Link className="btn btn-ghost" to={`/tickets/${ticket.id}`}>
                        Open
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>
      )}
    </div>
  );
}
