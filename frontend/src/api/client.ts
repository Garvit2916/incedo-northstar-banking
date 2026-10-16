import type { AuthResponse, MeResponse } from './types';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '';

const ACCESS_TOKEN_KEY = 'northstar.accessToken';
const REFRESH_TOKEN_KEY = 'northstar.refreshToken';

let accessToken: string | null = safeGet(ACCESS_TOKEN_KEY);
let refreshToken: string | null = safeGet(REFRESH_TOKEN_KEY);
let unauthorizedHandler: (() => void) | null = null;

function safeGet(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function safeSet(key: string, value: string | null): void {
  try {
    if (value === null) {
      window.localStorage.removeItem(key);
    } else {
      window.localStorage.setItem(key, value);
    }
  } catch {
    /* storage unavailable - tokens remain in memory only */
  }
}

export function setSession(response: AuthResponse): void {
  accessToken = response.accessToken;
  refreshToken = response.refreshToken;
  safeSet(ACCESS_TOKEN_KEY, accessToken);
  safeSet(REFRESH_TOKEN_KEY, refreshToken);
}

export function clearSession(): void {
  accessToken = null;
  refreshToken = null;
  safeSet(ACCESS_TOKEN_KEY, null);
  safeSet(REFRESH_TOKEN_KEY, null);
}

export function hasSession(): boolean {
  return accessToken !== null;
}

export function onUnauthorized(handler: () => void): void {
  unauthorizedHandler = handler;
}

export class ApiError extends Error {
  readonly status: number;
  readonly detail: string | null;
  /** Field-level validation messages keyed by lower-cased field name. */
  readonly fields: Record<string, string[]> | null;

  constructor(
    status: number,
    message: string,
    detail: string | null = null,
    fields: Record<string, string[]> | null = null,
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.detail = detail;
    this.fields = fields;
  }
}

interface RequestOptions {
  /** Skip the automatic refresh-and-retry cycle (used by the refresh call itself). */
  skipRefresh?: boolean;
}

async function parseError(response: Response): Promise<ApiError> {
  let detail: string | null = null;
  let title = `Request failed with status ${response.status}.`;
  let fields: Record<string, string[]> | null = null;
  try {
    const problem = await response.json();
    title = problem.title ?? problem.error ?? title;
    detail = problem.detail ?? problem.message ?? null;
    if (problem.errors && typeof problem.errors === 'object') {
      const normalised: Record<string, string[]> = {};
      for (const [key, value] of Object.entries(problem.errors as Record<string, unknown>)) {
        const messages = (Array.isArray(value) ? value : [value]).filter(
          (entry): entry is string => typeof entry === 'string' && entry.length > 0,
        );
        if (messages.length > 0) {
          normalised[key.charAt(0).toLowerCase() + key.slice(1)] = messages;
        }
      }
      if (Object.keys(normalised).length > 0) {
        fields = normalised;
        if (!detail) {
          detail = Object.values(normalised).flat().join(' ');
        }
      }
    }
  } catch {
    /* non-JSON error body */
  }
  return new ApiError(response.status, title, detail, fields);
}

async function refreshSession(): Promise<boolean> {
  if (!refreshToken) {
    return false;
  }
  try {
    const response = await fetch(`${API_BASE}/api/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    });
    if (!response.ok) {
      return false;
    }
    const data = (await response.json()) as AuthResponse;
    setSession(data);
    return true;
  } catch {
    return false;
  }
}

async function send(
  method: string,
  path: string,
  body: unknown,
  options: RequestOptions,
  isRetry: boolean,
): Promise<Response> {
  const headers: Record<string, string> = {};
  if (accessToken) {
    headers.Authorization = `Bearer ${accessToken}`;
  }

  let payload: BodyInit | undefined;
  if (body instanceof FormData) {
    payload = body;
  } else if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
    payload = JSON.stringify(body);
  }

  const response = await fetch(`${API_BASE}${path}`, { method, headers, body: payload });

  if (response.status === 401 && !options.skipRefresh && !isRetry) {
    const refreshed = await refreshSession();
    if (refreshed) {
      return send(method, path, body, options, true);
    }
    clearSession();
    unauthorizedHandler?.();
    throw await parseError(response);
  }

  return response;
}

async function request<T>(
  method: string,
  path: string,
  body?: unknown,
  options: RequestOptions = {},
): Promise<T> {
  const response = await send(method, path, body, options, false);

  if (!response.ok) {
    throw await parseError(response);
  }

  if (response.status === 204 || response.headers.get('content-length') === '0') {
    return undefined as T;
  }

  const text = await response.text();
  return text ? (JSON.parse(text) as T) : (undefined as T);
}

export async function fetchBlob(path: string): Promise<Blob> {
  const response = await send('GET', path, undefined, {}, false);
  if (!response.ok) {
    throw await parseError(response);
  }
  return response.blob();
}

export const http = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>('POST', path, body, options),
  put: <T>(path: string, body?: unknown) => request<T>('PUT', path, body),
  patch: <T>(path: string, body?: unknown) => request<T>('PATCH', path, body),
  delete: <T>(path: string) => request<T>('DELETE', path),
};

export async function fetchMe(): Promise<MeResponse> {
  return http.get<MeResponse>('/api/auth/me');
}

export function buildQuery(params: Record<string, string | number | boolean | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.append(key, String(value));
    }
  }
  const query = search.toString();
  return query ? `?${query}` : '';
}
