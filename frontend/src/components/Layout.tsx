import { Fragment, useEffect, useRef, useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { notificationsApi } from '../api/endpoints';
import type { NotificationResponse, Role } from '../api/types';
import { Icon } from './icons';
import type { IconName } from './icons';
import { Avatar } from './ui';
import { useToast } from './Toast';
import { formatRelative, humanise } from '../lib/format';

interface NavItem {
  to: string;
  label: string;
  icon: IconName;
  roles: Role[];
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

const ALL_ROLES: Role[] = ['Customer', 'SupportAgent', 'Reviewer', 'Administrator'];

const NAV_GROUPS: NavGroup[] = [
  {
    label: 'Workspace',
    items: [
      { to: '/dashboard', label: 'Dashboard', icon: 'dashboard', roles: ALL_ROLES },
      { to: '/notifications', label: 'Notifications', icon: 'bell', roles: ALL_ROLES },
    ],
  },
  {
    label: 'Banking',
    items: [
      { to: '/products', label: 'Products', icon: 'products', roles: ['Customer'] },
      { to: '/applications', label: 'My applications', icon: 'applications', roles: ['Customer'] },
      { to: '/my-tickets', label: 'My tickets', icon: 'support', roles: ['Customer'] },
      { to: '/profile', label: 'Profile & KYC', icon: 'profile', roles: ['Customer'] },
    ],
  },
  {
    label: 'Operations',
    items: [
      {
        to: '/support',
        label: 'AI support workspace',
        icon: 'support',
        roles: ['SupportAgent', 'Administrator'],
      },
      {
        to: '/knowledge',
        label: 'Knowledge base',
        icon: 'file',
        roles: ['SupportAgent', 'Administrator'],
      },
    ],
  },
  {
    label: 'Review',
    items: [{ to: '/reviews', label: 'Review queue', icon: 'reviews', roles: ['Reviewer', 'Administrator'] }],
  },
  {
    label: 'Administration',
    items: [
      { to: '/admin/users', label: 'Users & roles', icon: 'users', roles: ['Administrator'] },
      { to: '/admin/products', label: 'Product catalogue', icon: 'products', roles: ['Administrator'] },
      { to: '/admin/audit', label: 'Audit log', icon: 'audit', roles: ['Administrator'] },
    ],
  },
];

const SEGMENT_LABELS: Record<string, string> = {
  dashboard: 'Dashboard',
  products: 'Products',
  applications: 'My applications',
  'my-tickets': 'My tickets',
  profile: 'Profile & KYC',
  notifications: 'Notifications',
  reviews: 'Review queue',
  support: 'AI support workspace',
  tickets: 'AI support workspace',
  knowledge: 'Knowledge base',
  admin: 'Administration',
  users: 'Users & roles',
  audit: 'Audit log',
};

const NOTIFICATION_ICONS: Record<string, IconName> = {
  ApplicationSubmitted: 'applications',
  ApplicationUnderReview: 'reviews',
  AdditionalInformationRequired: 'alertTriangle',
  ApplicationApproved: 'checkCircle',
  ApplicationRejected: 'alertCircle',
  DocumentReviewed: 'file',
  TicketUpdated: 'support',
  General: 'bell',
};

function buildCrumbs(pathname: string): { label: string; to: string; current: boolean }[] {
  const parts = pathname.split('/').filter(Boolean);
  return parts.map((part, index) => {
    const isId = /^[0-9a-f]{8}-/i.test(part) || /^\d+$/.test(part);
    const label = SEGMENT_LABELS[part] ?? (isId ? 'Details' : humanise(part));
    return {
      label,
      to: `/${parts.slice(0, index + 1).join('/')}`,
      current: index === parts.length - 1,
    };
  });
}

export function Layout() {
  const { user, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const toast = useToast();

  const [unread, setUnread] = useState(0);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [notifOpen, setNotifOpen] = useState(false);
  const [userOpen, setUserOpen] = useState(false);
  const [preview, setPreview] = useState<NotificationResponse[]>([]);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState(false);

  const notifRef = useRef<HTMLDivElement>(null);
  const userRef = useRef<HTMLDivElement>(null);

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
  }, [user, location.pathname]);

  useEffect(() => {
    setSidebarOpen(false);
    setNotifOpen(false);
    setUserOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    if (!notifOpen) {
      return;
    }
    let active = true;
    setPreviewLoading(true);
    setPreviewError(false);
    notificationsApi
      .list(false, 1, 4)
      .then((result) => {
        if (active) {
          setPreview(result.items);
        }
      })
      .catch(() => {
        if (active) {
          setPreviewError(true);
        }
      })
      .finally(() => {
        if (active) {
          setPreviewLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [notifOpen]);

  useEffect(() => {
    function handlePointerDown(event: MouseEvent) {
      const target = event.target as Node;
      if (notifRef.current && !notifRef.current.contains(target)) {
        setNotifOpen(false);
      }
      if (userRef.current && !userRef.current.contains(target)) {
        setUserOpen(false);
      }
    }
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setNotifOpen(false);
        setUserOpen(false);
        setSidebarOpen(false);
      }
    }
    document.addEventListener('mousedown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('mousedown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, []);

  if (!user) {
    return null;
  }

  const visibleGroups = NAV_GROUPS.map((group) => ({
    ...group,
    items: group.items.filter((item) => item.roles.some((role) => user.roles.includes(role))),
  })).filter((group) => group.items.length > 0);

  const isCustomer = user.roles.includes('Customer');
  const crumbs = buildCrumbs(location.pathname);

  function handleLogout() {
    logout();
    navigate('/login', { replace: true });
  }

  async function handleMarkAllRead() {
    try {
      await notificationsApi.markAllRead();
      setUnread(0);
      setPreview((current) => current.map((item) => ({ ...item, isRead: true })));
      toast.success('All notifications marked as read.');
    } catch {
      toast.error('Unable to mark notifications as read.');
    }
  }

  async function openNotification(item: NotificationResponse) {
    if (!item.isRead) {
      try {
        await notificationsApi.markRead(item.id);
        setUnread((count) => Math.max(0, count - 1));
      } catch {
        // A failed mark-as-read should not block navigation.
      }
    }
    setNotifOpen(false);
    navigate('/notifications');
  }

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>

      {sidebarOpen && (
        <div className="sidebar-scrim" onClick={() => setSidebarOpen(false)} aria-hidden="true" />
      )}

      <aside className={sidebarOpen ? 'sidebar open' : 'sidebar'}>
        <Link to="/dashboard" className="brand" onClick={() => setSidebarOpen(false)}>
          <span className="brand-mark" aria-hidden="true">
            <Icon name="star" size={20} strokeWidth={2} />
          </span>
          <span className="brand-text">
            <span className="brand-name">Northstar</span>
            <span className="brand-tagline">Banking workspace</span>
          </span>
        </Link>

        <nav className="sidebar-nav" aria-label="Primary">
          {visibleGroups.map((group) => (
            <div className="nav-group" key={group.label}>
              <span className="nav-group-label">{group.label}</span>
              {group.items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
                  onClick={() => setSidebarOpen(false)}
                >
                  <Icon name={item.icon} size={18} />
                  <span>{item.label}</span>
                  {item.to === '/notifications' && unread > 0 && (
                    <>
                      <span className="nav-badge" aria-hidden="true">
                        {unread > 99 ? '99+' : unread}
                      </span>
                      <span className="sr-only">{unread} unread notifications</span>
                    </>
                  )}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>

        <div className="sidebar-footer">
          <div className="sidebar-note">
            <strong>Secure workspace</strong>
            Role-based access with automatic session renewal. Sign out when using a shared device.
          </div>
        </div>
      </aside>

      <div className="main">
        <header className="topbar">
          <div className="topbar-left">
            <button
              type="button"
              className="icon-btn menu-toggle"
              aria-label="Open navigation"
              aria-expanded={sidebarOpen}
              onClick={() => setSidebarOpen((open) => !open)}
            >
              <Icon name="menu" size={20} />
            </button>

            <nav className="breadcrumbs" aria-label="Breadcrumb">
              {crumbs.map((crumb, index) => (
                <Fragment key={crumb.to}>
                  {index > 0 && (
                    <span className="sep" aria-hidden="true">
                      /
                    </span>
                  )}
                  {crumb.current ? (
                    <span className="current" aria-current="page">
                      {crumb.label}
                    </span>
                  ) : (
                    <Link to={crumb.to} className="breadcrumb-link">
                      {crumb.label}
                    </Link>
                  )}
                </Fragment>
              ))}
            </nav>
          </div>

          <div className="topbar-right">
            <div className="user-menu" ref={notifRef}>
              <button
                type="button"
                className="icon-btn"
                aria-label={unread > 0 ? `Notifications, ${unread} unread` : 'Notifications'}
                aria-haspopup="dialog"
                aria-expanded={notifOpen}
                onClick={() => {
                  setNotifOpen((open) => !open);
                  setUserOpen(false);
                }}
              >
                <Icon name="bell" size={20} />
                {unread > 0 && <span className="dot-badge" aria-hidden="true" />}
              </button>

              {notifOpen && (
                <div className="notif-menu" role="dialog" aria-label="Recent notifications">
                  <div className="notif-menu-header">
                    <span className="strong text-sm">Notifications</span>
                    {unread > 0 && (
                      <button type="button" className="link-button text-xs" onClick={handleMarkAllRead}>
                        Mark all read
                      </button>
                    )}
                  </div>
                  <div className="notif-menu-body">
                    {previewLoading && (
                      <div className="centered" style={{ padding: 'var(--space-5)' }}>
                        <span className="spinner" role="status">
                          <span className="sr-only">Loading notifications</span>
                        </span>
                      </div>
                    )}
                    {!previewLoading && previewError && (
                      <p className="muted text-sm" style={{ padding: 'var(--space-3)' }}>
                        Notifications could not be loaded.
                      </p>
                    )}
                    {!previewLoading && !previewError && preview.length === 0 && (
                      <p className="muted text-sm" style={{ padding: 'var(--space-3)' }}>
                        You are all caught up.
                      </p>
                    )}
                    {!previewLoading &&
                      !previewError &&
                      preview.map((item) => (
                        <button
                          key={item.id}
                          type="button"
                          className={item.isRead ? 'notif-mini' : 'notif-mini unread'}
                          onClick={() => openNotification(item)}
                        >
                          <span className="notif-mini-icon" aria-hidden="true">
                            <Icon name={NOTIFICATION_ICONS[item.type] ?? 'bell'} size={16} />
                          </span>
                          <span className="grow">
                            <span className="notif-mini-title">{item.title}</span>
                            <span className="notif-mini-body">{item.body}</span>
                            <span className="notif-mini-time">{formatRelative(item.createdAtUtc)}</span>
                          </span>
                        </button>
                      ))}
                  </div>
                  <div className="notif-menu-footer">
                    <Link to="/notifications" className="link-button text-xs" onClick={() => setNotifOpen(false)}>
                      View all notifications
                    </Link>
                  </div>
                </div>
              )}
            </div>

            <div className="user-menu" ref={userRef}>
              <button
                type="button"
                className="user-trigger"
                aria-haspopup="menu"
                aria-expanded={userOpen}
                onClick={() => {
                  setUserOpen((open) => !open);
                  setNotifOpen(false);
                }}
              >
                <Avatar name={user.fullName} size="sm" />
                <span className="user-trigger-text">
                  <span className="user-trigger-name">{user.fullName}</span>
                  <span className="user-trigger-role">{humanise(user.roles[0] ?? 'Member')}</span>
                </span>
                <Icon name="chevronDown" size={15} />
              </button>

              {userOpen && (
                <div className="menu" role="menu" aria-label="Account">
                  <div className="user-menu-head">
                    <Avatar name={user.fullName} />
                    <div>
                      <div className="name">{user.fullName}</div>
                      <div className="email">{user.email}</div>
                    </div>
                  </div>
                  {isCustomer && (
                    <Link
                      to="/profile"
                      className="menu-item"
                      role="menuitem"
                      onClick={() => setUserOpen(false)}
                    >
                      <Icon name="profile" size={16} />
                      Profile &amp; KYC
                    </Link>
                  )}
                  <Link
                    to="/dashboard"
                    className="menu-item"
                    role="menuitem"
                    onClick={() => setUserOpen(false)}
                  >
                    <Icon name="dashboard" size={16} />
                    Dashboard
                  </Link>
                  <div className="menu-divider" />
                  <button
                    type="button"
                    className="menu-item menu-item--danger"
                    role="menuitem"
                    onClick={handleLogout}
                  >
                    <Icon name="logout" size={16} />
                    Sign out
                  </button>
                </div>
              )}
            </div>
          </div>
        </header>

        <main id="main-content" className="content" tabIndex={-1}>
          <div className="content-inner">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  );
}
