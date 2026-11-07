import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { applicationsApi } from '../api/endpoints';
import { Icon } from '../components/icons';
import { SkeletonTable } from '../components/Skeleton';
import { Card, EmptyState, ErrorBanner, StatusBadge } from '../components/ui';
import { formatDateOnly, humanise, pluralise } from '../lib/format';
import { useAsync } from '../lib/useAsync';
import type { ApplicationStatus } from '../api/types';

const IN_PROGRESS: ApplicationStatus[] = ['Draft', 'Submitted', 'UnderReview', 'AdditionalInformationRequired'];
const FILTER_ORDER: ApplicationStatus[] = [
  'Draft',
  'Submitted',
  'UnderReview',
  'AdditionalInformationRequired',
  'Approved',
  'Rejected',
  'Withdrawn',
];

export function ApplicationsPage() {
  const { data, loading, error, reload } = useAsync(() => applicationsApi.listMine(1, 100), []);
  const [filter, setFilter] = useState<ApplicationStatus | 'all' | 'progress'>('all');

  const items = data?.items ?? [];

  const counts = useMemo(() => {
    const map = new Map<ApplicationStatus, number>();
    for (const application of items) {
      map.set(application.status, (map.get(application.status) ?? 0) + 1);
    }
    return map;
  }, [items]);

  const inProgressCount = IN_PROGRESS.reduce((sum, status) => sum + (counts.get(status) ?? 0), 0);

  const filtered = useMemo(() => {
    if (filter === 'all') return items;
    if (filter === 'progress') return items.filter((item) => IN_PROGRESS.includes(item.status));
    return items.filter((item) => item.status === filter);
  }, [items, filter]);

  const activeFilters: { key: ApplicationStatus | 'all' | 'progress'; label: string; count: number }[] = [
    { key: 'all', label: 'All', count: items.length },
    { key: 'progress', label: 'In progress', count: inProgressCount },
    ...FILTER_ORDER.filter((status) => (counts.get(status) ?? 0) > 0).map((status) => ({
      key: status,
      label: humanise(status),
      count: counts.get(status) ?? 0,
    })),
  ];

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <p className="eyebrow">Onboarding</p>
          <h1 className="page-title">My applications</h1>
          <p className="page-description">
            Track every application, pick up where you left off and respond to document or information
            requests.
          </p>
        </div>
        <div className="page-actions">
          <Link className="btn btn-primary" to="/products">
            <Icon name="plus" size={16} />
            New application
          </Link>
        </div>
      </div>

      {!loading && !error && items.length > 0 && (
        <div className="filter-bar">
          <div className="chip-row" role="group" aria-label="Filter applications by status">
            {activeFilters.map((option) => (
              <button
                key={option.key}
                type="button"
                className={`chip ${filter === option.key ? 'chip--active' : ''}`}
                aria-pressed={filter === option.key}
                onClick={() => setFilter(option.key)}
              >
                {option.label}
                <span className="badge badge--neutral">{option.count}</span>
              </button>
            ))}
          </div>
          <span className="muted text-xs">
            {pluralise(filtered.length, 'application')} shown
          </span>
        </div>
      )}

      {error ? (
        <ErrorBanner message={error} onRetry={reload} />
      ) : loading ? (
        <Card flush>
          <SkeletonTable rows={6} />
        </Card>
      ) : items.length === 0 ? (
        <Card>
          <EmptyState
            icon="applications"
            title="You have not started an application yet"
            message="Choose a product to begin. You can save a draft and return whenever you are ready."
            action={
              <Link className="btn btn-primary" to="/products">
                Browse products
                <Icon name="arrowRight" size={16} />
              </Link>
            }
          />
        </Card>
      ) : filtered.length === 0 ? (
        <Card>
          <EmptyState
            icon="filter"
            title="No applications match this filter"
            message="Try a different status filter to see your applications."
            action={
              <button type="button" className="btn btn-secondary btn-sm" onClick={() => setFilter('all')}>
                Show all applications
              </button>
            }
          />
        </Card>
      ) : (
        <Card
          title="Application history"
          subtitle={
            data && data.totalCount > items.length
              ? `Showing your most recent ${items.length} of ${data.totalCount} applications.`
              : 'All applications on your account.'
          }
          flush
        >
          <div className="app-list">
            {filtered.map((application) => (
              <Link
                key={application.id}
                to={`/applications/${application.id}`}
                className="app-row"
              >
                <div className="app-row-main">
                  <span className="app-row-ref">
                    <Icon name="file" size={13} />
                    {application.reference}
                  </span>
                  <span className="app-row-title">{application.productName}</span>
                  <span className="app-row-meta">
                    <span>
                      <Icon name="calendar" size={13} />
                      Started {formatDateOnly(application.createdAtUtc)}
                    </span>
                    {application.submittedAtUtc && (
                      <span>
                        <Icon name="send" size={13} />
                        Submitted {formatDateOnly(application.submittedAtUtc)}
                      </span>
                    )}
                    {application.decidedAtUtc && (
                      <span>
                        <Icon name="checkCircle" size={13} />
                        Decided {formatDateOnly(application.decidedAtUtc)}
                      </span>
                    )}
                  </span>
                </div>
                <div className="app-row-side">
                  <StatusBadge value={application.status} />
                  <Icon name="chevronRight" size={16} />
                </div>
              </Link>
            ))}
          </div>
        </Card>
      )}
    </div>
  );
}
