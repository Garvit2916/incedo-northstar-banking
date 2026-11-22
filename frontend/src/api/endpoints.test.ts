import { beforeEach, describe, expect, it, vi } from 'vitest';
import { customerTicketsApi, dashboardApi, knowledgeApi } from './endpoints';
import { http } from './client';

vi.mock('./client', async () => {
  const actual = await vi.importActual<typeof import('./client')>('./client');
  return {
    ...actual,
    http: {
      get: vi.fn(() => Promise.resolve({})),
      post: vi.fn(() => Promise.resolve({})),
      put: vi.fn(() => Promise.resolve({})),
      patch: vi.fn(() => Promise.resolve({})),
      delete: vi.fn(() => Promise.resolve(undefined)),
    },
  };
});

const get = vi.mocked(http.get);
const post = vi.mocked(http.post);

describe('knowledgeApi', () => {
  beforeEach(() => get.mockClear());

  it('builds a filtered search query', async () => {
    await knowledgeApi.search({ search: 'card dispute', category: 'Cards', page: 2, pageSize: 10 });

    expect(get).toHaveBeenCalledTimes(1);
    const path = get.mock.calls[0][0];
    expect(path).toContain('/api/knowledge?');
    expect(path).toContain('search=card+dispute');
    expect(path).toContain('category=Cards');
    expect(path).toContain('page=2');
    expect(path).toContain('pageSize=10');
  });

  it('omits empty filters and exposes categories and detail endpoints', async () => {
    await knowledgeApi.search({ search: '', category: undefined });
    await knowledgeApi.categories();
    await knowledgeApi.get('KB-001-id');

    expect(get.mock.calls.map(([path]) => path)).toEqual([
      '/api/knowledge',
      '/api/knowledge/categories',
      '/api/knowledge/KB-001-id',
    ]);
  });
});

describe('dashboardApi', () => {
  beforeEach(() => get.mockClear());

  it('uses the customer-only account overview route', async () => {
    await dashboardApi.accountOverview();
    expect(get).toHaveBeenCalledWith('/api/dashboard/account-overview');
  });
});

describe('customerTicketsApi', () => {
  beforeEach(() => {
    get.mockClear();
    post.mockClear();
  });

  it('uses the customer-only ticket routes and never sends a customer id', async () => {
    const input = {
      subject: 'Card charge I do not recognise',
      description: 'I need help with a charge.',
      category: 'CardTransactionDispute' as const,
      priority: 'High' as const,
      relatedApplicationId: null,
    };

    await customerTicketsApi.create(input);
    await customerTicketsApi.list(2, 10);
    await customerTicketsApi.get('ticket-id');

    expect(post).toHaveBeenCalledWith('/api/customer/tickets', input);
    expect(post.mock.calls[0][1]).not.toHaveProperty('customerUserId');
    expect(get.mock.calls.map(([path]) => path)).toEqual([
      '/api/customer/tickets?page=2&pageSize=10',
      '/api/customer/tickets/ticket-id',
    ]);
  });
});
