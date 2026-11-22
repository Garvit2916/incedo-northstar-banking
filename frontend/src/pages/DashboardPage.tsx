import type { CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import { applicationsApi, customersApi, dashboardApi, ticketsApi } from '../api/endpoints';
import { useAuth } from '../auth/AuthContext';
import { Icon } from '../components/icons';
import type { IconName } from '../components/icons';
import { Badge, Card, EmptyState, ErrorBanner, ProgressBar, StatusBadge } from '../components/ui';
import { SkeletonCard, SkeletonStats, SkeletonTable } from '../components/Skeleton';
import { useAsync } from '../lib/useAsync';
import { formatCurrency, formatDateOnly, formatRelative, humanise } from '../lib/format';
import type {
  AccountOverview,
  ApplicationSummaryResponse,
  DashboardStatsResponse,
  PagedResult,
  ProfileResponse,
  TicketSummaryResponse,
} from '../api/types';

type Tone = 'primary' | 'success' | 'warning' | 'danger' | 'accent';

interface StatDefinition {
  label: string;
  value: number;
  icon: IconName;
  tone: Tone;
  to: string;
}

const PROFILE_KEYS: (keyof ProfileResponse)[] = [
  'firstName',
  'lastName',
  'phoneNumber',
  'dateOfBirth',
  'addressLine1',
  'city',
  'state',
  'postalCode',
  'country',
  'occupation',
  'annualIncomeBand',
];

function greeting(): string {
  const hour = new Date().getHours();
  if (hour < 12) return 'Good morning';
  if (hour < 18) return 'Good afternoon';
  return 'Good evening';
}

export function DashboardPage() {
  const { user, hasRole } = useAuth();
  const isCustomer = hasRole('Customer');
  const canSupport = hasRole('SupportAgent') || hasRole('Administrator');
  const canReview = hasRole('Reviewer') || hasRole('Administrator');

  const stats = useAsync<DashboardStatsResponse>(
    () => (isCustomer ? dashboardApi.customer() : dashboardApi.operations()),
    [isCustomer],
  );
  const applications = useAsync<PagedResult<ApplicationSummaryResponse> | null>(
    () => (isCustomer ? applicationsApi.listMine(1, 4) : Promise.resolve(null)),
    [isCustomer],
  );
  const profile = useAsync<ProfileResponse | null>(
    () => (isCustomer ? customersApi.me() : Promise.resolve(null)),
    [isCustomer],
  );
  const account = useAsync<AccountOverview | null>(
    () => (isCustomer ? dashboardApi.accountOverview() : Promise.resolve(null)),
    [isCustomer],
  );
  const tickets = useAsync<PagedResult<TicketSummaryResponse> | null>(
    () => (canSupport ? ticketsApi.list({ page: 1, pageSize: 4 }) : Promise.resolve(null)),
    [canSupport],
  );

  const firstName = (profile.data?.firstName ?? user?.fullName?.split(' ')[0] ?? 'there').trim();
  const completion = profile.data
    ? Math.round((PROFILE_KEYS.filter((key) => Boolean(profile.data?.[key])).length / PROFILE_KEYS.length) * 100)
    : 0;

  const customerStats: StatDefinition[] = stats.data
    ? [
        { label: 'Draft applications', value: stats.data.applicationsDraft, icon: 'edit', tone: 'accent', to: '/applications' },
        { label: 'Submitted', value: stats.data.applicationsSubmitted, icon: 'send', tone: 'warning', to: '/applications' },
        { label: 'Under review', value: stats.data.applicationsUnderReview, icon: 'clock', tone: 'primary', to: '/applications' },
        { label: 'Approved', value: stats.data.applicationsApproved, icon: 'checkCircle', tone: 'success', to: '/applications' },
        { label: 'Rejected', value: stats.data.applicationsRejected, icon: 'alertCircle', tone: 'danger', to: '/applications' },
        { label: 'Unread updates', value: stats.data.unreadNotifications, icon: 'bell', tone: 'primary', to: '/notifications' },
      ]
    : [];

  const operationsStats: StatDefinition[] = stats.data
    ? [
        ...(canReview
          ? [
              { label: 'Submitted', value: stats.data.applicationsSubmitted, icon: 'send' as const, tone: 'warning' as const, to: '/reviews' },
              { label: 'Under review', value: stats.data.applicationsUnderReview, icon: 'clock' as const, tone: 'primary' as const, to: '/reviews' },
              { label: 'Approved', value: stats.data.applicationsApproved, icon: 'checkCircle' as const, tone: 'success' as const, to: '/reviews' },
              { label: 'Rejected', value: stats.data.applicationsRejected, icon: 'alertCircle' as const, tone: 'danger' as const, to: '/reviews' },
            ]
          : []),
        ...(canSupport
          ? [{ label: 'Open tickets', value: stats.data.openTickets, icon: 'support' as const, tone: 'accent' as const, to: '/support' }]
          : []),
        { label: 'Unread updates', value: stats.data.unreadNotifications, icon: 'bell' as const, tone: 'primary' as const, to: '/notifications' },
      ]
    : [];

  const tiles = isCustomer ? customerStats : operationsStats;

  const pipeline = stats.data
    ? [
        { label: 'Submitted', value: stats.data.applicationsSubmitted, className: 'swatch', color: 'var(--warning)' },
        { label: 'Under review', value: stats.data.applicationsUnderReview, className: 'swatch', color: 'var(--info)' },
        { label: 'Approved', value: stats.data.applicationsApproved, className: 'swatch', color: 'var(--success)' },
        { label: 'Rejected', value: stats.data.applicationsRejected, className: 'swatch', color: 'var(--danger)' },
      ]
    : [];
  const pipelineTotal = pipeline.reduce((sum, item) => sum + item.value, 0);
  const percentage = (value: number) => (pipelineTotal === 0 ? 0 : (value / pipelineTotal) * 100);

  return (
    <div className="page">
      <section className="dash-hero">
        <div className="dash-hero-main">
          <p className="eyebrow">{isCustomer ? 'Your overview' : 'Operations overview'}</p>
          <h1 className="dash-greeting">
            {greeting()}, <span className="accent">{firstName}</span>.
          </h1>
          <p className="muted text-sm">
            {isCustomer
              ? 'Track your applications, keep your profile current and act on outstanding requests.'
              : 'Monitor the onboarding pipeline, respond to customers and keep decisions moving.'}
          </p>
          <div className="dash-meta">
            <span>
              <Icon name="calendar" size={14} />
              {new Date().toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' })}
            </span>
            <span>
              <Icon name="shieldCheck" size={14} />
              {user?.roles.map((role) => humanise(role)).join(' · ')}
            </span>
            {isCustomer && profile.data && (
              <span>
                <Icon name={profile.data.kycVerifiedAtUtc ? 'checkCircle' : 'clock'} size={14} />
                {profile.data.kycVerifiedAtUtc ? 'KYC verified' : 'KYC pending'}
              </span>
            )}
          </div>
        </div>
        <div className="dash-hero-actions">
          {isCustomer ? (
            <>
              <Link className="btn btn-primary" to="/products">
                Browse products
                <Icon name="arrowRight" size={16} />
              </Link>
              <Link className="btn btn-secondary" to="/applications">
                My applications
              </Link>
            </>
          ) : (
            <>
              {canSupport ? (
                <Link className="btn btn-primary" to="/support">
                  Open support workspace
                  <Icon name="arrowRight" size={16} />
                </Link>
              ) : (
                <Link className="btn btn-primary" to="/reviews">
                  Open review queue
                  <Icon name="arrowRight" size={16} />
                </Link>
              )}
              <Link className="btn btn-secondary" to="/notifications">
                Notifications
              </Link>
            </>
          )}
        </div>
      </section>

      {stats.error ? (
        <ErrorBanner message={stats.error} onRetry={stats.reload} />
      ) : stats.loading || !stats.data ? (
        <SkeletonStats count={tiles.length || (isCustomer ? 6 : 5)} />
      ) : (
        <div className="stat-grid">
          {tiles.map((tile) => (
            <Link to={tile.to} className="stat-card stat-card--link" key={tile.label}>
              <div className="stat-head">
                <span className="stat-label">{tile.label}</span>
                <span className={`stat-icon stat-icon--${tile.tone}`}>
                  <Icon name={tile.icon} size={16} />
                </span>
              </div>
              <strong className="stat-value">{tile.value}</strong>
              <span className="stat-foot">
                View details
                <Icon name="chevronRight" size={13} />
              </span>
            </Link>
          ))}
        </div>
      )}

      {isCustomer && (
        <Card
          className="account-overview-card"
          title="Account overview"
          subtitle="Illustrative account view — not connected to a real bank account"
          actions={<Badge tone="accent" dot={false}>Demo data</Badge>}
        >
          {account.loading ? (
            <SkeletonCard />
          ) : account.error ? (
            <ErrorBanner message={account.error} onRetry={account.reload} />
          ) : account.data ? (
            <div className="account-overview">
              <div className="account-summary">
                <span className="account-label">
                  <Icon name="wallet" size={15} /> {account.data.accountName}
                </span>
                <strong className="account-balance">{formatCurrency(account.data.availableBalance)}</strong>
                <div className="account-identifiers">
                  <span>{account.data.maskedAccountNumber}</span>
                  <span>{account.data.maskedIfsc}</span>
                  <span>{account.data.branch}</span>
                </div>
                <p className="account-notice">
                  <Icon name="info" size={13} /> {account.data.demoNotice}
                </p>
              </div>
              <div className="account-activity">
                <h3>Recent activity</h3>
                <ul>
                  {account.data.transactions.map((transaction) => (
                    <li key={transaction.reference}>
                      <span className={`account-txn-icon ${transaction.direction === 'Credit' ? 'credit' : 'debit'}`}>
                        <Icon name={transaction.direction === 'Credit' ? 'download' : 'upload'} size={14} />
                      </span>
                      <span className="account-txn-main">
                        <strong>{transaction.narration}</strong>
                        <small>{formatDateOnly(transaction.postedAtUtc)} · {transaction.category}</small>
                      </span>
                      <span className={transaction.direction === 'Credit' ? 'amount-credit' : 'amount-debit'}>
                        {transaction.direction === 'Credit' ? '+' : '−'}{formatCurrency(Math.abs(transaction.amount))}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
              <div className="account-products">
                <h3>Active products</h3>
                <ul>
                  {account.data.activeProducts.map((product, index) => (
                    <li key={`${product.productCode}-${index}`}>
                      <span className="badge badge--info">{product.productCode || 'DEMO'}</span>
                      <div>
                        <strong>{product.name}</strong>
                        <small>{product.reference} · {product.status}</small>
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          ) : null}
        </Card>
      )}

      <div className="dashboard-grid">
        <div className="dashboard-col">
          {isCustomer ? (
            <Card
              title="Recent applications"
              subtitle="Your latest onboarding activity"
              actions={
                <Link to="/applications" className="link-button text-xs">
                  View all
                </Link>
              }
              flush
            >
              {applications.loading ? (
                <div style={{ padding: 'var(--space-4)' }}>
                  <SkeletonTable rows={3} />
                </div>
              ) : applications.error ? (
                <div style={{ padding: 'var(--space-4)' }}>
                  <ErrorBanner message={applications.error} onRetry={applications.reload} />
                </div>
              ) : !applications.data || applications.data.items.length === 0 ? (
                <EmptyState
                  icon="applications"
                  title="No applications yet"
                  message="Start your first onboarding application and it will appear here."
                  action={
                    <Link className="btn btn-primary btn-sm" to="/products">
                      Browse products
                    </Link>
                  }
                />
              ) : (
                <div className="app-list">
                  {applications.data.items.map((application) => (
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
                        </span>
                      </div>
                      <div className="app-row-side">
                        <StatusBadge value={application.status} />
                        <Icon name="chevronRight" size={16} />
                      </div>
                    </Link>
                  ))}
                </div>
              )}
            </Card>
          ) : canSupport ? (
            <Card
              title="Latest tickets"
              subtitle="Newest customer conversations"
              actions={
                <Link to="/support" className="link-button text-xs">
                  View all
                </Link>
              }
              flush
            >
              {tickets.loading ? (
                <div style={{ padding: 'var(--space-4)' }}>
                  <SkeletonTable rows={3} />
                </div>
              ) : tickets.error ? (
                <div style={{ padding: 'var(--space-4)' }}>
                  <ErrorBanner message={tickets.error} onRetry={tickets.reload} />
                </div>
              ) : !tickets.data || tickets.data.items.length === 0 ? (
                <EmptyState icon="support" title="No tickets" message="There are no customer tickets right now." />
              ) : (
                <div className="app-list">
                  {tickets.data.items.map((ticket) => (
                    <Link key={ticket.id} to={`/support/${ticket.id}`} className="app-row">
                      <div className="app-row-main">
                        <span className="app-row-ref">
                          <Icon name="support" size={13} />
                          {ticket.reference}
                        </span>
                        <span className="app-row-title">{ticket.subject}</span>
                        <span className="app-row-meta">
                          <span>{humanise(ticket.category)}</span>
                          <span>
                            <Icon name="clock" size={13} />
                            {ticket.slaDueAtUtc ? `SLA ${formatRelative(ticket.slaDueAtUtc)}` : 'No SLA'}
                          </span>
                        </span>
                      </div>
                      <div className="app-row-side">
                        <StatusBadge value={ticket.priority} dot={false} />
                        <StatusBadge value={ticket.status} />
                        <Icon name="chevronRight" size={16} />
                      </div>
                    </Link>
                  ))}
                </div>
              )}
            </Card>
          ) : (
            <Card
              title="Review focus"
              subtitle="Application decisions waiting on an authorised reviewer"
              actions={<Link to="/reviews" className="link-button text-xs">Open queue</Link>}
            >
              <div className="review-focus">
                <div>
                  <span className="stat-label">Submitted</span>
                  <strong>{stats.data?.applicationsSubmitted ?? '—'}</strong>
                </div>
                <div>
                  <span className="stat-label">Under review</span>
                  <strong>{stats.data?.applicationsUnderReview ?? '—'}</strong>
                </div>
              </div>
            </Card>
          )}

          <Card title="Quick actions" subtitle="Jump straight to the task at hand">
            <div className="recommend-list">
              {(isCustomer
                ? [
                    { to: '/products', category: 'Onboarding', title: 'Start an application', body: 'Compare products and begin a new application.' },
                    { to: '/applications', category: 'Progress', title: 'Track applications', body: 'See status, upload documents and respond to requests.' },
                    { to: '/profile', category: 'Profile', title: 'Complete your profile', body: 'Keep KYC details accurate for faster decisions.' },
                  ]
                : [
                    ...(canSupport
                      ? [{ to: '/support', category: 'Support', title: 'Customer conversations', body: 'Reply, assign and escalate with AI assistance.' }]
                      : []),
                    ...(canReview
                      ? [{ to: '/reviews', category: 'Review', title: 'Decision queue', body: 'Assess applications and record auditable decisions.' }]
                      : []),
                    { to: '/notifications', category: 'Updates', title: 'Notifications', body: 'Read unread updates from the workspace.' },
                    ...(hasRole('Administrator')
                      ? [{ to: '/admin/users', category: 'Admin', title: 'Users & roles', body: 'Manage access and account status.' }]
                      : []),
                  ]
              ).map((action) => (
                <Link to={action.to} className="recommend-card" key={action.title}>
                  <span className="cat">{action.category}</span>
                  <strong>{action.title}</strong>
                  <p>{action.body}</p>
                </Link>
              ))}
            </div>
          </Card>
        </div>

        <div className="dashboard-col">
          {isCustomer ? (
            <>
              <Card title="Profile strength" subtitle="Complete profiles unlock faster decisions">
                {profile.loading ? (
                  <SkeletonCard />
                ) : profile.error ? (
                  <ErrorBanner message={profile.error} onRetry={profile.reload} />
                ) : (
                  <div className="completion-card">
                    <div className="completion-ring" style={{ ['--value' as string]: completion } as CSSProperties}>
                      <span>{completion}%</span>
                    </div>
                    <div className="completion-copy">
                      <h3>{completion === 100 ? 'Your profile is complete' : 'A little more to do'}</h3>
                      <p>
                        {completion === 100
                          ? 'All key profile fields are filled in.'
                          : 'Add your remaining details to speed up verification.'}
                      </p>
                      <ProgressBar value={completion} label="Profile completeness" showValue={false} />
                      <Link to="/profile" className="link-button text-xs">
                        Update profile
                      </Link>
                    </div>
                  </div>
                )}
              </Card>

              <Card title="Your access" subtitle="What you can do in this workspace">
                <div className="profile-badges">
                  {user?.roles.map((role) => (
                    <span className="badge badge--accent" key={role}>
                      {humanise(role)}
                    </span>
                  ))}
                </div>
                <p className="muted text-sm" style={{ marginTop: 'var(--space-3)' }}>
                  You can apply for products, manage your profile, raise a support ticket and track
                  your onboarding. Support and review tools are restricted to operations roles.
                </p>
                <Link to="/my-tickets" className="btn btn-secondary btn-sm" style={{ marginTop: 'var(--space-3)' }}>
                  <Icon name="support" size={14} />
                  My tickets
                </Link>
              </Card>
            </>
          ) : (
            <>
              {canReview ? (
                <Card title="Decision pipeline" subtitle="Current application distribution">
                  {stats.loading || !stats.data ? (
                    <SkeletonCard />
                  ) : pipelineTotal === 0 ? (
                    <EmptyState icon="reviews" message="No applications in the pipeline yet." />
                  ) : (
                    <div className="breakdown">
                      <div
                        className="donut"
                        style={
                          {
                            '--a': percentage(pipeline[0].value),
                            '--b': percentage(pipeline[1].value),
                            '--c': percentage(pipeline[2].value),
                            '--d': percentage(pipeline[3].value),
                          } as CSSProperties
                        }
                      >
                        <div className="donut-center">
                          <strong>{pipelineTotal}</strong>
                          <span>Applications</span>
                        </div>
                      </div>
                      <ul className="legend">
                        {pipeline.map((item) => (
                          <li className="legend-item" key={item.label}>
                            <span className="swatch" style={{ background: item.color }} />
                            {item.label}
                            <span className="legend-value">{item.value}</span>
                          </li>
                        ))}
                      </ul>
                    </div>
                  )}
                </Card>
              ) : (
                <Card title="Copilot readiness" subtitle="Approved guidance behind every suggestion">
                  <div className="support-readiness">
                    <span className="stat-icon stat-icon--accent"><Icon name="file" size={16} /></span>
                    <div>
                      <strong>Approved knowledge base</strong>
                      <p className="muted text-sm">
                        Search the knowledge base and see exactly which articles ground Copilot output.
                      </p>
                    </div>
                  </div>
                  <Link to="/knowledge" className="btn btn-secondary btn-sm">
                    Browse knowledge base
                    <Icon name="arrowRight" size={14} />
                  </Link>
                </Card>
              )}

              <Card title="Your access" subtitle="Role-based capabilities">
                <div className="profile-badges">
                  {user?.roles.map((role) => (
                    <span className="badge badge--accent" key={role}>
                      {humanise(role)}
                    </span>
                  ))}
                </div>
                <p className="muted text-sm" style={{ marginTop: 'var(--space-3)' }}>
                  {hasRole('Administrator')
                    ? 'Full administrative access, including users, products and the audit trail.'
                    : hasRole('Reviewer')
                      ? 'Assess applications and record decisions. Support tools require the Support agent role.'
                      : 'Respond to customers and use AI assistance. Application decisions are reserved for reviewers.'}
                </p>
              </Card>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
