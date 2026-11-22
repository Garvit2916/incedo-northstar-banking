export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent';

export function formatDate(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function formatDateOnly(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
  });
}

const INR = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/** Formats demo monetary values using the Indian numbering system. */
export function formatCurrency(value: number): string {
  return Number.isFinite(value) ? INR.format(value) : '—';
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Turns a PascalCase enum name into spaced title case for display. */
export function humanise(value: string): string {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2');
}

const TONES: Record<string, Tone> = {
  approved: 'success',
  resolved: 'success',
  active: 'success',
  clean: 'success',
  verified: 'success',
  complete: 'success',
  passed: 'success',
  reopened: 'warning',
  rejected: 'danger',
  infected: 'danger',
  scanfailed: 'danger',
  failed: 'danger',
  urgent: 'danger',
  escalated: 'danger',
  error: 'danger',
  submitted: 'warning',
  new: 'warning',
  pending: 'warning',
  pendingcustomer: 'warning',
  pendinginternal: 'warning',
  needsreplacement: 'warning',
  additionalinformationrequired: 'warning',
  draft: 'neutral',
  closed: 'neutral',
  withdrawn: 'neutral',
  inactive: 'neutral',
  low: 'neutral',
  normal: 'info',
  underreview: 'info',
  open: 'info',
  inreview: 'info',
  high: 'accent',
  administrator: 'accent',
};

/**
 * Maps a status, priority or role value to a semantic colour tone. Unknown
 * values fall back to neutral so nothing renders unstyled.
 */
export function statusTone(value: string | null | undefined): Tone {
  if (!value) {
    return 'neutral';
  }
  return TONES[value.replace(/\s+/g, '').toLowerCase()] ?? 'neutral';
}

/** Backwards-compatible helper that returns the badge class for a value. */
export function statusClass(value: string): string {
  return `badge badge--${statusTone(value)}`;
}

export function initials(name: string | null | undefined): string {
  if (!name) {
    return '?';
  }
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) {
    return '?';
  }
  if (parts.length === 1) {
    return parts[0].slice(0, 2).toUpperCase();
  }
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

const RELATIVE = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

/** Short "just now / 5 minutes ago / 3 days ago" rendering for timestamps. */
export function formatRelative(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }

  const seconds = Math.round((date.getTime() - Date.now()) / 1000);
  const absolute = Math.abs(seconds);

  if (absolute < 45) {
    return 'just now';
  }
  if (absolute < 3600) {
    return RELATIVE.format(Math.round(seconds / 60), 'minute');
  }
  if (absolute < 86400) {
    return RELATIVE.format(Math.round(seconds / 3600), 'hour');
  }
  if (absolute < 86400 * 30) {
    return RELATIVE.format(Math.round(seconds / 86400), 'day');
  }
  return formatDateOnly(value);
}

export function pluralise(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}
