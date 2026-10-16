import { useState } from 'react';
import { adminApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { Card, ErrorBanner } from '../components/ui';
import { SkeletonTable } from '../components/Skeleton';
import { ConfirmDialog } from '../components/ConfirmDialog';
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
  const toast = useToast();

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
    if (!pending) {
      return;
    }
    await run(
      pending.user.id,
      () => adminApi.setActive(pending.user.id, pending.isActive),
      pending.isActive ? 'User activated.' : 'User deactivated.',
    );
  }

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>User administration</h1>
          <p className="muted">Manage roles and account status.</p>
        </div>
      </header>

      {error && <ErrorBanner message={error} />}

      <Card>
        {loading || !data ? (
          <SkeletonTable rows={6} />
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Email</th>
                <th>Roles</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {data.map((user) => (
                <tr key={user.id}>
                  <td>{user.fullName}</td>
                  <td>{user.email}</td>
                  <td>
                    <div className="role-toggles">
                      {ALL_ROLES.map((role) => (
                        <label key={role} className="checkbox">
                          <input
                            type="checkbox"
                            checked={user.roles.includes(role)}
                            disabled={busyId === user.id}
                            onChange={(e) => {
                              const next = e.target.checked
                                ? [...user.roles, role]
                                : user.roles.filter((r) => r !== role);
                              void run(user.id, () => adminApi.assignRoles(user.id, next), 'Roles updated.');
                            }}
                          />
                          {humanise(role)}
                        </label>
                      ))}
                    </div>
                  </td>
                  <td>
                    <span className={user.isActive ? 'badge badge-approved' : 'badge badge-rejected'}>
                      {user.isActive ? 'Active' : 'Deactivated'}
                    </span>
                  </td>
                  <td>
                    <button
                      type="button"
                      className="btn btn-ghost"
                      disabled={busyId === user.id}
                      onClick={() => setPendingToggle({ user, isActive: !user.isActive })}
                    >
                      {user.isActive ? 'Deactivate' : 'Activate'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <ConfirmDialog
        open={pendingToggle !== null}
        title={pendingToggle?.isActive ? 'Activate user' : 'Deactivate user'}
        message={
          pendingToggle?.isActive
            ? `Reactivate ${pendingToggle?.user.email}? They will be able to sign in again.`
            : `Deactivate ${pendingToggle?.user.email}? They will no longer be able to sign in.`
        }
        confirmLabel={pendingToggle?.isActive ? 'Activate' : 'Deactivate'}
        destructive={pendingToggle?.isActive === false}
        busy={busyId === pendingToggle?.user.id}
        onConfirm={confirmToggle}
        onCancel={() => setPendingToggle(null)}
      />
    </div>
  );
}
