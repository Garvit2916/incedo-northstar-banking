import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { reviewsApi } from '../api/endpoints';
import { Icon } from '../components/icons';
import { SearchInput } from '../components/forms';
import { SkeletonTable } from '../components/Skeleton';
import {
  Card,
  EmptyState,
  ErrorBanner,
  Pagination,
  Segmented,
  StatusBadge,
} from '../components/ui';
import { useAsync } from '../lib/useAsync';
import { formatDate, formatRelative } from '../lib/format';
import type { ApplicationStatus } from '../api/types';

const PAGE_SIZE = 10;

const QUEUE_VIEWS: { value: string; label: string; status?: ApplicationStatus }[] = [
  { value: 'Submitted', label: 'Awaiting review', status: 'Submitted' },
  { value: 'UnderReview', label: 'Under review', status: 'UnderReview' },
  { value: 'AdditionalInformationRequired', label: 'Info requested', status: 'AdditionalInformationRequired' },
  { value: 'Approved', label: 'Approved', status: 'Approved' },
  { value: 'Rejected', label: 'Rejected', status: 'Rejected' },
  { value: 'all', label: 'All' },
];

export function ReviewQueuePage() {
  const [view, setView] = useState('Submitted');
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

  const selected = QUEUE_VIEWS.find((item) => item.value === view) ?? QUEUE_VIEWS[0];

  const { data, loading, error, reload } = useAsync(
    () =>
      reviewsApi.queue({
        status: selected.status,
        search: search || undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
    [view, search, page],
  );

  const items = data?.items ?? [];
  const hasFilters = Boolean(search);

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <p className="eyebrow">Decisioning</p>
          <h1 className="page-title">Review queue</h1>
          <p className="page-description">
            Assess customer applications, verify documents and record auditable decisions.
          </p>
        </div>
        <div className="page-actions">
          <span className="badge badge--neutral">
            <Icon name="reviews" size={13} />
            {data ? `${data.totalCount} in view` : 'Loading…'}
          </span>
        </div>
      </div>

      <div className="filter-bar">
        <Segmented
          ariaLabel="Queue view"
          value={view}
          onChange={(next) => {
            setView(next);
            setPage(1);
          }}
          options={QUEUE_VIEWS.map((option) => ({ value: option.value, label: option.label }))}
        />
        <div className="filter-controls">
          <SearchInput
            id="review-search"
            label="Search applications"
            placeholder="Search reference or product"
            value={searchInput}
            onChange={setSearchInput}
          />
          {hasFilters && (
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => {
                setSearchInput('');
                setSearch('');
                setPage(1);
              }}
            >
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
          <SkeletonTable rows={7} />
        </Card>
      ) : items.length === 0 ? (
        <Card>
          <EmptyState
            icon="reviews"
            title={hasFilters ? 'No applications match this view' : 'Nothing to review here'}
            message={
              hasFilters
                ? 'Try a different search term or switch queue view.'
                : 'Applications will appear here when they reach this stage.'
            }
            action={
              hasFilters ? (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={() => {
                    setSearchInput('');
                    setSearch('');
                  }}
                >
                  Clear search
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
                    <th>Reference</th>
                    <th>Product</th>
                    <th>Status</th>
                    <th>Submitted</th>
                    <th>Age</th>
                    <th className="right">Action</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((application) => (
                    <tr key={application.id}>
                      <td>
                        <Link className="table-link mono" to={`/reviews/${application.id}`}>
                          {application.reference}
                        </Link>
                      </td>
                      <td>{application.productName}</td>
                      <td>
                        <StatusBadge value={application.status} />
                      </td>
                      <td>{formatDate(application.submittedAtUtc)}</td>
                      <td className="muted">
                        {application.submittedAtUtc ? formatRelative(application.submittedAtUtc) : 'Not submitted'}
                      </td>
                      <td className="right">
                        <Link className="btn btn-secondary btn-sm" to={`/reviews/${application.id}`}>
                          Review
                          <Icon name="arrowRight" size={14} />
                        </Link>
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
