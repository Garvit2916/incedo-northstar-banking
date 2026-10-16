import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { notificationsApi } from '../api/endpoints';

interface NavItem {
  to: string;
  label: string;
  roles: string[];
}

const NAV_ITEMS: NavItem[] = [
  { to: '/dashboard', label: 'Dashboard', roles: ['Customer', 'SupportAgent', 'Reviewer', 'Administrator'] },
  { to: '/products', label: 'Products', roles: ['Customer'] },
  { to: '/applications', label: 'My Applications', roles: ['Customer'] },
  { to: '/profile', label: 'Profile', roles: ['Customer'] },
  { to: '/reviews', label: 'Review Queue', roles: ['Reviewer', 'Administrator'] },
  { to: '/tickets', label: 'Support Tickets', roles: ['SupportAgent', 'Administrator'] },
  { to: '/admin/users', label: 'Users', roles: ['Administrator'] },
  { to: '/admin/products', label: 'Product Admin', roles: ['Administrator'] },
  { to: '/admin/audit', label: 'Audit Log', roles: ['Administrator'] },
  { to: '/notifications', label: 'Notifications', roles: ['Customer', 'SupportAgent', 'Reviewer', 'Administrator'] },
];

export function Layout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [unread, setUnread] = useState(0);

  useEffect(() => {
    if (!user) {
      return;
    }
    let active = true;
    notificationsApi
      .unreadCount()
      .then((count) => {
        if (active) {
          setUnread(count);
        }
      })
      .catch(() => undefined);
    return () => {
      active = false;
    };
  }, [user]);

  if (!user) {
    return null;
  }

  const visibleItems = NAV_ITEMS.filter((item) => item.roles.some((role) => user.roles.includes(role as never)));

  function handleLogout() {
    logout();
    navigate('/login', { replace: true });
  }

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">
            ★
          </span>
          <span className="brand-name">Northstar</span>
        </div>
        <nav className="nav" aria-label="Primary">
          {visibleItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
            >
              {item.label}
              {item.to === '/notifications' && unread > 0 && (
                <>
                  <span className="pill" aria-hidden="true">
                    {unread}
                  </span>
                  <span className="sr-only">{unread} unread notifications</span>
                </>
              )}
            </NavLink>
          ))}
        </nav>
      </aside>
      <div className="main">
        <header className="topbar">
          <div className="topbar-user">
            <strong>{user.fullName}</strong>
            <span className="muted">{user.roles.join(', ')}</span>
          </div>
          <button type="button" className="btn btn-ghost" onClick={handleLogout}>
            Sign out
          </button>
        </header>
        <main id="main-content" className="content" tabIndex={-1}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
