import { Link } from 'react-router-dom';
import { dashboardApi } from '../api/endpoints';
import { useAuth } from '../auth/AuthContext';
import { Card, ErrorBanner } from '../components/ui';
import { SkeletonCard, SkeletonStats } from '../components/Skeleton';
import { useAsync } from '../lib/useAsync';

interface QuickAction {
  to: string;
  label: string;
  description: string;
  primary?: boolean;
}

const CUSTOMER_ACTIONS: QuickAction[] = [
  { to: '/products', label: 'Browse products', description: 'Start a new onboarding application.', primary: true },
  { to: '/applications', label: 'My applications', description: 'Track progress and upload documents.' },
  { to: '/profile', label: 'Complete your profile', description: 'Keep your details up to date.' },
];

const OPERATIONS_ACTIONS: QuickAction[] = [
  { to: '/reviews', label: 'Review queue', description: 'Assess submitted applications.' },
  { to: '/tickets', label: 'Support tickets', description: 'Respond to customer enquiries.' },
];

const ADMIN_ACTIONS: QuickAction[] = [
  { to: '/admin/users', label: 'Manage users', description: 'Roles and account status.' },
  { to: '/admin/products', label: 'Manage products', description: 'Publish or retire products.' },
  { to: '/admin/audit', label: 'Audit log', description: 'Review privileged activity.' },
];

export function DashboardPage() {
  const { user, hasRole } = useAuth();
  const isCustomer = hasRole('Customer');

  const { data, loading, error } = useAsync(
    () => (isCustomer ? dashboardApi.customer() : dashboardApi.operations()),
    [isCustomer],
  );

  const actions: QuickAction[] = isCustomer
    ? CUSTOMER_ACTIONS
    : [
        ...OPERATIONS_ACTIONS,
        ...(hasRole('Administrator') ? ADMIN_ACTIONS : []),
      ];

  if (error) {
    return <ErrorBanner message={error} />;
  }

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>Welcome, {user?.fullName}</h1>
          <p className="muted">
            {isCustomer ? 'Your onboarding overview' : 'Operations overview'}
          </p>
        </div>
      </header>

      {loading || !data ? (
        <>
          <SkeletonStats count={isCustomer ? 6 : 5} />
          <SkeletonCard />
        </>
      ) : (
        <>
          {isCustomer ? (
            <div className="stat-grid">
              <Stat label="Draft applications" value={data.applicationsDraft} to="/applications" />
              <Stat label="Submitted" value={data.applicationsSubmitted} to="/applications" />
              <Stat label="Under review" value={data.applicationsUnderReview} to="/applications" />
              <Stat label="Approved" value={data.applicationsApproved} to="/applications" />
              <Stat label="Rejected" value={data.applicationsRejected} to="/applications" />
              <Stat label="Unread notifications" value={data.unreadNotifications} to="/notifications" />
            </div>
          ) : (
            <div className="stat-grid">
              <Stat label="Submitted" value={data.applicationsSubmitted} to="/reviews" />
              <Stat label="Under review" value={data.applicationsUnderReview} to="/reviews" />
              <Stat label="Approved" value={data.applicationsApproved} to="/reviews" />
              <Stat label="Rejected" value={data.applicationsRejected} to="/reviews" />
              <Stat label="Open tickets" value={data.openTickets} to="/tickets" />
            </div>
          )}

          <Card title="Quick actions">
            <div className="quick-actions">
              {actions.map((action) => (
                <Link
                  key={action.to}
                  to={action.to}
                  className={action.primary ? 'quick-action quick-action-primary' : 'quick-action'}
                >
                  <strong>{action.label}</strong>
                  <span className="muted">{action.description}</span>
                </Link>
              ))}
            </div>
          </Card>
        </>
      )}
    </div>
  );
}

function Stat({ label, value, to }: { label: string; value: number; to: string }) {
  return (
    <Link to={to} className="stat-card">
      <span className="stat-value">{value}</span>
      <span className="stat-label">{label}</span>
    </Link>
  );
}
