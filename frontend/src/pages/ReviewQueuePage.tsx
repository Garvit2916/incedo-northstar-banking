import { useState } from 'react';
import { Link } from 'react-router-dom';
import { reviewsApi } from '../api/endpoints';
import { Badge, Card, EmptyState, ErrorBanner } from '../components/ui';
import { SkeletonTable } from '../components/Skeleton';
import { useAsync } from '../lib/useAsync';
import { formatDate, humanise } from '../lib/format';
import type { ApplicationStatus } from '../api/types';

const STATUS_OPTIONS: Array<ApplicationStatus | ''> = [
  '',
  'Submitted',
  'UnderReview',
  'AdditionalInformationRequired',
  'Approved',
  'Rejected',
];

export function ReviewQueuePage() {
  const [status, setStatus] = useState<ApplicationStatus | ''>('Submitted');
  const [search, setSearch] = useState('');

  const { data, loading, error } = useAsync(
    () => reviewsApi.queue({ status: status || undefined, search: search || undefined }),
    [status, search],
  );

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>Review queue</h1>
          <p className="muted">Applications awaiting or undergoing review.</p>
        </div>
      </header>

      <div className="filter-bar">
        <label className="field">
          <span>Status</span>
          <select value={status} onChange={(e) => setStatus(e.target.value as ApplicationStatus | '')}>
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
            placeholder="Reference or product"
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
            <EmptyState message="No applications match the current filter." />
          ) : (
            <table className="table">
              <thead>
                <tr>
                  <th>Reference</th>
                  <th>Product</th>
                  <th>Status</th>
                  <th>Submitted</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {data.items.map((application) => (
                  <tr key={application.id}>
                    <td>{application.reference}</td>
                    <td>{application.productName}</td>
                    <td>
                      <Badge value={application.status} />
                    </td>
                    <td>{formatDate(application.submittedAtUtc)}</td>
                    <td className="right">
                      <Link className="btn btn-ghost" to={`/reviews/${application.id}`}>
                        Review
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
