import type { CSSProperties } from 'react';

/** A single shimmering placeholder block. */
export function Skeleton({
  width = '100%',
  height = '1rem',
  radius = 'var(--radius-sm)',
  className = '',
}: {
  width?: string;
  height?: string;
  radius?: string;
  className?: string;
}) {
  const style: CSSProperties = { width, height, borderRadius: radius };
  return <span className={`skeleton ${className}`.trim()} style={style} aria-hidden="true" />;
}

/** Several stacked lines, as for a paragraph or list. */
export function SkeletonText({ lines = 3 }: { lines?: number }) {
  return (
    <div className="skeleton-text" aria-hidden="true">
      {Array.from({ length: lines }).map((_, index) => (
        <Skeleton key={index} width={index === lines - 1 ? '60%' : '100%'} />
      ))}
    </div>
  );
}

/** A placeholder for a card-shaped region. */
export function SkeletonCard() {
  return (
    <div className="card skeleton-card" aria-hidden="true">
      <Skeleton width="40%" height="1.1rem" />
      <SkeletonText lines={3} />
    </div>
  );
}

/** A grid of stat placeholders used by dashboard-style pages. */
export function SkeletonStats({ count = 6 }: { count?: number }) {
  return (
    <div className="stat-grid" aria-hidden="true">
      {Array.from({ length: count }).map((_, index) => (
        <div key={index} className="stat-card skeleton-stat">
          <Skeleton width="40%" height="1.6rem" />
          <Skeleton width="70%" height="0.8rem" />
        </div>
      ))}
    </div>
  );
}

/** A placeholder for a table/list of rows. */
export function SkeletonTable({ rows = 5 }: { rows?: number }) {
  return (
    <div className="skeleton-table" aria-hidden="true">
      {Array.from({ length: rows }).map((_, index) => (
        <Skeleton key={index} height="2.4rem" />
      ))}
    </div>
  );
}

/**
 * Announces a busy region to assistive technology while showing placeholder
 * content visually.
 */
export function PageLoader({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="page-loader" role="status" aria-live="polite">
      <span className="sr-only">{label}</span>
      <SkeletonStats />
      <SkeletonTable />
    </div>
  );
}
