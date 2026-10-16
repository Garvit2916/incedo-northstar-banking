import { Link } from 'react-router-dom';
import { applicationsApi } from '../api/endpoints';
import { Badge, Card, EmptyState, ErrorBanner } from '../components/ui';
import { SkeletonTable } from '../components/Skeleton';
import { useAsync } from '../lib/useAsync';
import { formatDate } from '../lib/format';

export function ApplicationsPage() {
  const { data, loading, error } = useAsync(() => applicationsApi.listMine(), []);

  if (error) {
    return <ErrorBanner message={error} />;
  }

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>My applications</h1>
          <p className="muted">Track and manage your onboarding applications.</p>
        </div>
        <Link className="btn btn-primary" to="/products">
          New application
        </Link>
      </header>

      <Card>
        {loading || !data ? (
          <SkeletonTable rows={5} />
        ) : data.items.length === 0 ? (
          <EmptyState message="You have not started any applications yet." />
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Reference</th>
                <th>Product</th>
                <th>Status</th>
                <th>Submitted</th>
                <th>Decided</th>
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
                  <td>{formatDate(application.decidedAtUtc)}</td>
                  <td className="right">
                    <Link className="btn btn-ghost" to={`/applications/${application.id}`}>
                      Open
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </div>
  );
}
