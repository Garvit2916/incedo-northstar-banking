import { useMemo, useState } from 'react';
import { adminApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { Icon } from '../components/icons';
import { SkeletonTable } from '../components/Skeleton';
import {
  Avatar,
  Card,
  EmptyState,
  ErrorBanner,
  StatCard,
  StatGrid,
} from '../components/ui';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { humanise } from '../lib/format';
import type { Role, UserSummary } from '../api/types';

const ALL_ROLES: Role[] = ['Customer', 'SupportAgent', 'Reviewer', 'Administrator'];

interface PendingToggle {
  user: UserSummary;
  isActive: boolean;
}

export function AdminUsersPage() {
  const { data, loading, error, reload } = useAsync(() => adminApi.users(), []);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [pendingToggle, setPendingToggle] = useState<PendingToggle | null>(null);
  const [query, setQuery] = useState('');
  const toast = useToast();

  const users = data ?? [];

  const summary = useMemo(() => {
    const active = users.filter((user) => user.isActive).length;
    const admins = users.filter((user) => user.roles.includes('Administrator')).length;
    const staff = users.filter((user) =>
      user.roles.some((role) => role === 'SupportAgent' || role === 'Reviewer'),
    ).length;
    return { total: users.length, active, admins, staff };
  }, [users]);

  const filtered = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return users;
    return users.filter(
      (user) =>
        user.fullName.toLowerCase().includes(term) ||
        user.email.toLowerCase().includes(term) ||
        user.roles.some((role) => humanise(role).toLowerCase().includes(term)),
    );
  }, [users, query]);

  async function run(id: string, action: () => Promise<unknown>, successMessage: string) {
    setBusyId(id);
    try {
      await action();
      toast.success(successMessage);
      reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Action failed.');
    } finally {
      setBusyId(null);
    }
  }

  async function confirmToggle() {
    const pending = pendingToggle;
    setPendingToggle(null);
    if (!pending) return;
    await run(
      pending.user.id,
      () => adminApi.setActive(pending.user.id, pending.isActive),
      pending.isActive ? 'User activated.' : 'User deactivated.',
    );
  }

  function toggleRole(user: UserSummary, role: Role, checked: boolean) {
    const next = checked ? [...user.roles, role] : user.roles.filter((item) => item !== role);
    if (next.length === 0) {
      toast.error('A user must have at least one role.');
      return;
    }
    void run(user.id, () => adminApi.assignRoles(user.id, next), 'Roles updated.');
  }

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <p className="eyebrow">Administration</p>
          <h1 className="page-title">Users & roles</h1>
          <p className="page-description">
            Manage access for customers, support agents, reviewers and administrators.
          </p>
        </div>
        <div className="page-actions">
          <div className="input-group">
            <span className="input-icon" aria-hidden="true">
              <Icon name="search" size={15} />
            </span>
            <input
              className="input"
              type="search"
              aria-label="Search users"
              placeholder="Search name, email or role"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
          </div>
        </div>
      </div>

      {error ? (
        <ErrorBanner message={error} onRetry={reload} />
      ) : loading ? (
        <>
          <StatGrid>
            <StatCard label="Total users" value="—" icon="users" tone="primary" />
            <StatCard label="Active" value="—" icon="checkCircle" tone="success" />
            <StatCard label="Administrators" value="—" icon="shieldCheck" tone="accent" />
          </StatGrid>
          <Card flush>
            <SkeletonTable rows={7} />
          </Card>
        </>
      ) : (
        <>
          <StatGrid>
            <StatCard label="Total users" value={summary.total} icon="users" tone="primary" foot="All accounts" />
            <StatCard label="Active accounts" value={summary.active} icon="checkCircle" tone="success" foot="Can sign in" />
            <StatCard label="Administrators" value={summary.admins} icon="shieldCheck" tone="accent" foot="Full access" />
            <StatCard label="Operations staff" value={summary.staff} icon="support" tone="warning" foot="Support & review" />
          </StatGrid>

          {filtered.length === 0 ? (
            <Card>
              <EmptyState
                icon="search"
                title="No users match your search"
                message="Try a different name, email address or role."
                action={
                  <button type="button" className="btn btn-secondary btn-sm" onClick={() => setQuery('')}>
                    Clear search
                  </button>
                }
              />
            </Card>
          ) : (
            <Card
              title="Accounts"
              subtitle={`${filtered.length} of ${users.length} users`}
              flush
            >
              <div className="table-scroll">
                <table className="table">
                  <thead>
                    <tr>
                      <th>User</th>
                      <th>Roles</th>
                      <th>Status</th>
                      <th className="right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filtered.map((user) => {
                      const isBusy = busyId === user.id;
                      const isOnlyAdmin =
                        user.roles.includes('Administrator') && summary.admins <= 1;
                      return (
                        <tr key={user.id}>
                          <td>
                            <div className="review-applicant">
                              <Avatar name={user.fullName} />
                              <div>
                                <strong>{user.fullName}</strong>
                                <p className="muted text-xs">{user.email}</p>
                              </div>
                            </div>
                          </td>
                          <td>
                            <div className="role-grid">
                              {ALL_ROLES.map((role) => {
                                const checked = user.roles.includes(role);
                                const locked = isBusy || (isOnlyAdmin && role === 'Administrator');
                                return (
                                  <label
                                    key={role}
                                    className={`role-chip ${checked ? 'active' : ''} ${locked ? 'disabled' : ''}`.trim()}
                                    title={
                                      isOnlyAdmin && role === 'Administrator'
                                        ? 'At least one administrator is required.'
                                        : undefined
                                    }
                                  >
                                    <input
                                      type="checkbox"
                                      checked={checked}
                                      disabled={locked}
                                      onChange={(event) => toggleRole(user, role, event.target.checked)}
                                    />
                                    <span>{humanise(role)}</span>
                                  </label>
                                );
                              })}
                            </div>
                          </td>
                          <td>
                            <span className={`badge badge--${user.isActive ? 'success' : 'danger'}`}>
                              {user.isActive ? 'Active' : 'Deactivated'}
                            </span>
                          </td>
                          <td className="right">
                            <button
                              type="button"
                              className={`btn btn-sm ${user.isActive ? 'btn-ghost' : 'btn-secondary'}`}
                              disabled={isBusy}
                              onClick={() => setPendingToggle({ user, isActive: !user.isActive })}
                            >
                              {isBusy ? 'Updating…' : user.isActive ? 'Deactivate' : 'Activate'}
                            </button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </Card>
          )}
        </>
      )}

      <ConfirmDialog
        open={pendingToggle !== null}
        title={pendingToggle?.isActive ? 'Activate user' : 'Deactivate user'}
        message={
          pendingToggle?.isActive
            ? `Reactivate ${pendingToggle?.user.email}? They will be able to sign in again.`
            : `Deactivate ${pendingToggle?.user.email}? They will no longer be able to sign in. Existing sessions end on their next request.`
        }
        confirmLabel={pendingToggle?.isActive ? 'Activate' : 'Deactivate'}
        destructive={pendingToggle?.isActive === false}
        busy={busyId === pendingToggle?.user.id}
        onConfirm={() => void confirmToggle()}
        onCancel={() => setPendingToggle(null)}
      />
    </div>
  );
}
