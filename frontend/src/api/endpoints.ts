import { buildQuery, fetchBlob, http, setSession } from './client';
import type {
  ApplicationDetailResponse,
  ApplicationStatus,
  AccountOverview,
  ApplicationStatusHistoryResponse,
  ApplicationSummaryResponse,
  AuditEventResponse,
  AuthResponse,
  CreateCustomerTicketRequest,
  CustomerTicketDetailResponse,
  CustomerTicketSummaryResponse,
  DashboardStatsResponse,
  DocumentResponse,
  DocumentReviewStatus,
  DownloadLinkResponse,
  KnowledgeArticle,
  KnowledgeQuery,
  KycDocumentType,
  NotificationResponse,
  PagedResult,
  ProductResponse,
  ProfileResponse,
  TicketCategory,
  TicketDetailResponse,
  TicketPriority,
  TicketStatus,
  TicketSummaryResponse,
  TicketOverviewResponse,
  UserSummary,
  AiSummaryResponse,
  AiSuggestedResponse,
  AiSentimentResponse,
  NextBestActionsResponse,
} from './types';

export const authApi = {
  async login(email: string, password: string): Promise<AuthResponse> {
    const response = await http.post<AuthResponse>('/api/auth/login', { email, password });
    setSession(response);
    return response;
  },
  async register(input: {
    email: string;
    password: string;
    confirmPassword: string;
    firstName: string;
    lastName: string;
    phoneNumber?: string;
  }): Promise<AuthResponse> {
    const response = await http.post<AuthResponse>('/api/auth/register', input);
    setSession(response);
    return response;
  },
};

export const productsApi = {
  list: (includeInactive = false) =>
    http.get<ProductResponse[]>(`/api/products${buildQuery({ includeInactive })}`),
  get: (id: string) => http.get<ProductResponse>(`/api/products/${id}`),
};

export const applicationsApi = {
  listMine: (page = 1, pageSize = 20) =>
    http.get<PagedResult<ApplicationSummaryResponse>>(
      `/api/applications${buildQuery({ page, pageSize })}`,
    ),
  getMine: (id: string) => http.get<ApplicationDetailResponse>(`/api/applications/${id}`),
  create: (productId: string, answers: Record<string, string> = {}) =>
    http.post<ApplicationDetailResponse>('/api/applications', { productId, answers }),
  update: (id: string, answers: Record<string, string>) =>
    http.put<ApplicationDetailResponse>(`/api/applications/${id}`, { answers }),
  submit: (id: string) => http.post<ApplicationDetailResponse>(`/api/applications/${id}/submit`),
  withdraw: (id: string) => http.post<ApplicationDetailResponse>(`/api/applications/${id}/withdraw`),
  history: (id: string) =>
    http.get<ApplicationStatusHistoryResponse[]>(`/api/applications/${id}/history`),
};

export const reviewsApi = {
  queue: (params: { status?: ApplicationStatus; search?: string; page?: number; pageSize?: number } = {}) =>
    http.get<PagedResult<ApplicationSummaryResponse>>(`/api/reviews${buildQuery(params)}`),
  get: (id: string) => http.get<ApplicationDetailResponse>(`/api/reviews/${id}`),
  start: (id: string) => http.post<ApplicationDetailResponse>(`/api/reviews/${id}/start`),
  requestInformation: (id: string, reason: string) =>
    http.post<ApplicationDetailResponse>(`/api/reviews/${id}/request-information`, { reason }),
  approve: (id: string, reason: string) =>
    http.post<ApplicationDetailResponse>(`/api/reviews/${id}/approve`, { reason }),
  reject: (id: string, reason: string) =>
    http.post<ApplicationDetailResponse>(`/api/reviews/${id}/reject`, { reason }),
};

export const documentsApi = {
  list: (applicationId: string) =>
    http.get<DocumentResponse[]>(`/api/applications/${applicationId}/documents`),
  async upload(applicationId: string, documentType: KycDocumentType, file: File): Promise<DocumentResponse> {
    const form = new FormData();
    form.append('file', file);
    form.append('documentType', documentType);
    return http.post<DocumentResponse>(`/api/applications/${applicationId}/documents`, form);
  },
  review: (documentId: string, decision: DocumentReviewStatus, notes?: string) =>
    http.post<DocumentResponse>(`/api/documents/${documentId}/review`, { decision, notes }),
  link: (documentId: string) =>
    http.get<DownloadLinkResponse>(`/api/documents/${documentId}/link`),
  contentUrl: (documentId: string) => `/api/documents/${documentId}/content`,
  async open(documentId: string): Promise<void> {
    const link = await documentsApi.link(documentId);
    if (/^https?:\/\//i.test(link.url)) {
      window.open(link.url, '_blank', 'noopener,noreferrer');
      return;
    }
    const blob = await fetchBlob(`/api/documents/${documentId}/content`);
    const objectUrl = URL.createObjectURL(blob);
    const anchor = window.document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = link.fileName;
    anchor.rel = 'noopener';
    window.document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60000);
  },
};

export const notificationsApi = {
  list: (unreadOnly = false, page = 1, pageSize = 20) =>
    http.get<PagedResult<NotificationResponse>>(
      `/api/notifications${buildQuery({ unreadOnly, page, pageSize })}`,
    ),
  unreadCount: () => http.get<number>('/api/notifications/unread-count'),
  markRead: (id: string) => http.post<void>(`/api/notifications/${id}/read`),
  markAllRead: () => http.post<number>('/api/notifications/read-all'),
};

export const customersApi = {
  me: () => http.get<ProfileResponse>('/api/customers/me'),
  update: (input: Partial<ProfileResponse>) => http.put<ProfileResponse>('/api/customers/me', input),
};

export const customerTicketsApi = {
  create: (input: CreateCustomerTicketRequest) =>
    http.post<CustomerTicketSummaryResponse>('/api/customer/tickets', input),
  list: (page = 1, pageSize = 50) =>
    http.get<PagedResult<CustomerTicketSummaryResponse>>(
      `/api/customer/tickets${buildQuery({ page, pageSize })}`,
    ),
  get: (id: string) => http.get<CustomerTicketDetailResponse>(`/api/customer/tickets/${id}`),
};

export const ticketsApi = {
  list: (
    params: {
      status?: TicketStatus;
      category?: TicketCategory;
      priority?: TicketPriority;
      assignedAgentId?: string;
      search?: string;
      page?: number;
      pageSize?: number;
    } = {},
  ) => http.get<PagedResult<TicketSummaryResponse>>(`/api/tickets${buildQuery(params)}`),
  overview: () => http.get<TicketOverviewResponse>('/api/tickets/overview'),
  get: (id: string) => http.get<TicketDetailResponse>(`/api/tickets/${id}`),
  create: (input: {
    subject: string;
    description: string;
    category: TicketCategory;
    priority: TicketPriority;
    customerUserId?: string | null;
    relatedApplicationId?: string | null;
  }) => http.post<TicketDetailResponse>('/api/tickets', input),
  addMessage: (id: string, body: string, isInternalNote = false) =>
    http.post<TicketDetailResponse>(`/api/tickets/${id}/messages`, { body, isInternalNote }),
  changeStatus: (id: string, status: TicketStatus) =>
    http.patch<TicketDetailResponse>(`/api/tickets/${id}/status`, { status }),
  assign: (id: string, agentUserId: string) =>
    http.post<TicketDetailResponse>(`/api/tickets/${id}/assign`, { agentUserId }),
  ai: {
    summarize: (id: string) => http.post<AiSummaryResponse>(`/api/tickets/${id}/ai/summarize`),
    suggestResponse: (id: string) =>
      http.post<AiSuggestedResponse>(`/api/tickets/${id}/ai/suggest-response`),
    analyzeSentiment: (id: string) =>
      http.post<AiSentimentResponse>(`/api/tickets/${id}/ai/analyze-sentiment`),
    nextBestActions: (id: string) =>
      http.post<NextBestActionsResponse>(`/api/tickets/${id}/ai/next-best-actions`),
  },
};

export const knowledgeApi = {
  search: (query: KnowledgeQuery = {}) =>
    http.get<PagedResult<KnowledgeArticle>>(
      `/api/knowledge${buildQuery(query as Record<string, string | number | undefined>)}`,
    ),
  categories: () => http.get<string[]>('/api/knowledge/categories'),
  get: (id: string) => http.get<KnowledgeArticle>(`/api/knowledge/${id}`),
};

export const adminApi = {
  users: () => http.get<UserSummary[]>('/api/admin/users'),
  assignRoles: (id: string, roles: string[]) =>
    http.put<void>(`/api/admin/users/${id}/roles`, { roles }),
  setActive: (id: string, isActive: boolean) =>
    http.put<void>(`/api/admin/users/${id}/active`, { isActive }),
  audit: (params: { action?: string; actorUserId?: string; page?: number; pageSize?: number } = {}) =>
    http.get<PagedResult<AuditEventResponse>>(`/api/admin/audit${buildQuery(params)}`),
  createProduct: (input: {
    code: string;
    name: string;
    summary: string;
    description: string;
    category: string;
    eligibility: string;
    requiredDocuments: string[];
    terms: string;
    minimumAge: number;
    isActive: boolean;
    displayOrder: number;
  }) => http.post<ProductResponse>('/api/admin/products', input),
  updateProduct: (id: string, input: Record<string, unknown>) =>
    http.put<ProductResponse>(`/api/admin/products/${id}`, input),
};

export const dashboardApi = {
  customer: () => http.get<DashboardStatsResponse>('/api/dashboard/customer'),
  accountOverview: () => http.get<AccountOverview>('/api/dashboard/account-overview'),
  operations: () => http.get<DashboardStatsResponse>('/api/dashboard/operations'),
};
