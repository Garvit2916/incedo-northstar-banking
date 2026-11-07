import type { ReactNode } from 'react';
import { Icon } from './icons';
import type { IconName } from './icons';
import { humanise, initials, statusTone } from '../lib/format';
import type { Tone } from '../lib/format';

/* ---- surfaces ------------------------------------------------------------- */

interface CardProps {
  title?: ReactNode;
  subtitle?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  className?: string;
  bodyClassName?: string;
  /** Remove body padding, e.g. for tables and lists. */
  flush?: boolean;
}

export function Card({ title, subtitle, actions, children, footer, className, bodyClassName, flush }: CardProps) {
  const classes = ['card', className].filter(Boolean).join(' ');
  const bodyClasses = ['card-body', flush ? 'card-body--flush' : '', bodyClassName]
    .filter(Boolean)
    .join(' ');

  return (
    <section className={classes}>
      {(title || subtitle || actions) && (
        <header className="card-header">
          <div>
            {title && <h2>{title}</h2>}
            {subtitle && <p className="card-subtitle">{subtitle}</p>}
          </div>
          {actions && <div className="card-actions">{actions}</div>}
        </header>
      )}
      <div className={bodyClasses}>{children}</div>
      {footer && <footer className="card-footer">{footer}</footer>}
    </section>
  );
}

export function PageHeader({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow?: ReactNode;
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className="page-header">
      <div className="page-header-main">
        {eyebrow && <p className="eyebrow">{eyebrow}</p>}
        <h1 className="page-title">{title}</h1>
        {description && <p className="page-description">{description}</p>}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </header>
  );
}

/* ---- status --------------------------------------------------------------- */

export function Badge({
  value,
  tone,
  dot = false,
  icon,
  className,
  children,
}: {
  value?: string;
  tone?: Tone;
  dot?: boolean;
  icon?: IconName;
  className?: string;
  children?: ReactNode;
}) {
  const resolvedTone = tone ?? statusTone(value);
  const resolvedIcon = icon;
  return (
    <span
      className={['badge', `badge--${resolvedTone}`, dot ? 'badge--dot' : '', className]
        .filter(Boolean)
        .join(' ')}
    >
      {resolvedIcon && <Icon name={resolvedIcon} size={12} strokeWidth={2.2} />}
      {children ?? (value ? humanise(value) : null)}
    </span>
  );
}

export function StatusBadge({ value, dot = true }: { value: string; dot?: boolean }) {
  return <Badge value={value} dot={dot} />;
}

/* ---- feedback ------------------------------------------------------------- */

export function Spinner({ label = 'Loading…' }: { label?: string }) {
  return (
    <span className="spinner" role="status">
      <span className="sr-only">{label}</span>
    </span>
  );
}

export function PageLoading({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="page-loading" role="status" aria-live="polite">
      <Spinner />
      <span className="muted text-sm">{label}</span>
    </div>
  );
}

export function ErrorBanner({
  message,
  onRetry,
}: {
  message: string;
  onRetry?: () => void;
}) {
  return (
    <div className="alert-error" role="alert">
      <Icon name="alertCircle" size={18} className="alert-icon" />
      <div className="grow">
        <p>{message}</p>
        {onRetry && (
          <button type="button" className="link-button" onClick={onRetry}>
            Try again
          </button>
        )}
      </div>
    </div>
  );
}

export function InfoBanner({
  title,
  message,
  tone = 'info',
  icon,
}: {
  title?: ReactNode;
  message: ReactNode;
  tone?: 'info' | 'success' | 'warning' | 'neutral';
  icon?: IconName;
}) {
  const iconName = icon ?? (tone === 'success' ? 'checkCircle' : tone === 'warning' ? 'alertTriangle' : 'info');
  return (
    <div className={`alert-${tone}`} role="status">
      <Icon name={iconName} size={18} className="alert-icon" />
      <div className="grow">
        {title && <strong className="alert-title">{title}</strong>}
        <p>{message}</p>
      </div>
    </div>
  );
}

export function EmptyState({
  title,
  message,
  icon = 'inbox',
  action,
}: {
  title?: ReactNode;
  message: ReactNode;
  icon?: IconName;
  action?: ReactNode;
}) {
  return (
    <div className="empty-state">
      <span className="empty-state-icon">
        <Icon name={icon} size={22} />
      </span>
      {title && <h3>{title}</h3>}
      <p>{message}</p>
      {action && <div className="empty-state-action">{action}</div>}
    </div>
  );
}

/* ---- stats & progress ----------------------------------------------------- */

export function StatCard({
  label,
  value,
  icon,
  tone = 'primary',
  foot,
}: {
  label: ReactNode;
  value: ReactNode;
  icon?: IconName;
  tone?: 'primary' | 'success' | 'warning' | 'danger' | 'accent';
  foot?: ReactNode;
}) {
  return (
    <div className="stat-card">
      <div className="stat-head">
        <span className="stat-label">{label}</span>
        {icon && (
          <span className={`stat-icon stat-icon--${tone}`}>
            <Icon name={icon} size={16} />
          </span>
        )}
      </div>
      <strong className="stat-value">{value}</strong>
      {foot && <div className="stat-foot">{foot}</div>}
    </div>
  );
}

export function StatGrid({ children, columns }: { children: ReactNode; columns?: number }) {
  return (
    <div
      className="stat-grid"
      style={columns ? { gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` } : undefined}
    >
      {children}
    </div>
  );
}

export function ProgressBar({
  value,
  label,
  tone = 'primary',
  showValue = true,
}: {
  value: number;
  label?: ReactNode;
  tone?: 'primary' | 'success' | 'warning' | 'danger';
  showValue?: boolean;
}) {
  const clamped = Math.max(0, Math.min(100, Math.round(value)));
  return (
    <div className="progress">
      {(label || showValue) && (
        <div className="progress-head">
          <span>{label}</span>
          {showValue && <span className="tabular">{clamped}%</span>}
        </div>
      )}
      <div
        className="progress-track"
        role="progressbar"
        aria-valuenow={clamped}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={typeof label === 'string' ? label : undefined}
      >
        <div
          className={`progress-bar${tone === 'primary' ? '' : ` progress-bar--${tone}`}`}
          style={{ width: `${clamped}%` }}
        />
      </div>
    </div>
  );
}

/* ---- identity ------------------------------------------------------------- */

export function Avatar({
  name,
  size = 'md',
  dark = false,
  title,
}: {
  name: string;
  size?: 'sm' | 'md' | 'lg';
  dark?: boolean;
  title?: string;
}) {
  const classes = ['avatar', size !== 'md' ? `avatar--${size}` : '', dark ? 'avatar--dark' : '']
    .filter(Boolean)
    .join(' ');
  return (
    <span className={classes} title={title ?? name} aria-hidden={title ? undefined : true}>
      {initials(name)}
    </span>
  );
}

/* ---- navigation & pagination ---------------------------------------------- */

export interface TabItem {
  id: string;
  label: ReactNode;
  count?: number;
}

export function Tabs({
  items,
  value,
  onChange,
  ariaLabel = 'Views',
}: {
  items: TabItem[];
  value: string;
  onChange: (id: string) => void;
  ariaLabel?: string;
}) {
  return (
    <div className="tabs" role="tablist" aria-label={ariaLabel}>
      {items.map((item) => (
        <button
          key={item.id}
          type="button"
          role="tab"
          aria-selected={item.id === value}
          className={item.id === value ? 'tab active' : 'tab'}
          onClick={() => onChange(item.id)}
        >
          {item.label}
          {typeof item.count === 'number' && <span className="count">{item.count}</span>}
        </button>
      ))}
    </div>
  );
}

export function Segmented({
  options,
  value,
  onChange,
  ariaLabel = 'Options',
}: {
  options: { value: string; label: ReactNode }[];
  value: string;
  onChange: (value: string) => void;
  ariaLabel?: string;
}) {
  return (
    <div className="segmented" role="group" aria-label={ariaLabel}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          aria-pressed={option.value === value}
          className={option.value === value ? 'active' : undefined}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

export function Pagination({
  page,
  pageSize,
  total,
  onPage,
}: {
  page: number;
  pageSize: number;
  total: number;
  onPage: (page: number) => void;
}) {
  const pageCount = Math.max(1, Math.ceil(total / pageSize));
  if (total <= pageSize) {
    return null;
  }
  const first = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, total);

  return (
    <div className="pagination">
      <span className="pagination-info">
        Showing <span className="tabular">{first}</span>–<span className="tabular">{last}</span> of{' '}
        <span className="tabular">{total}</span>
      </span>
      <div className="btn-group">
        <button
          type="button"
          className="btn btn-ghost btn-sm"
          onClick={() => onPage(page - 1)}
          disabled={page <= 1}
        >
          <Icon name="chevronLeft" size={15} />
          Previous
        </button>
        <span className="btn btn-ghost btn-sm" aria-current="page">
          {page} / {pageCount}
        </span>
        <button
          type="button"
          className="btn btn-ghost btn-sm"
          onClick={() => onPage(page + 1)}
          disabled={page >= pageCount}
        >
          Next
          <Icon name="chevronRight" size={15} />
        </button>
      </div>
    </div>
  );
}

/* ---- timeline ------------------------------------------------------------- */

export interface TimelineEntry {
  title: ReactNode;
  meta?: ReactNode;
  note?: ReactNode;
  tone?: 'success' | 'danger' | 'active';
}

export function Timeline({ items }: { items: TimelineEntry[] }) {
  return (
    <ol className="timeline">
      {items.map((item, index) => (
        <li
          key={index}
          className={item.tone ? `timeline-item timeline-item--${item.tone}` : 'timeline-item'}
        >
          <span className="timeline-marker" aria-hidden="true" />
          <div className="timeline-body">
            <p className="timeline-title">{item.title}</p>
            {item.meta && <p className="timeline-meta">{item.meta}</p>}
            {item.note && <p className="timeline-note">{item.note}</p>}
          </div>
        </li>
      ))}
    </ol>
  );
}
