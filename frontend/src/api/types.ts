// DTO types mirroring the Northstar API contracts. Enum members mirror the
// server-side enums and are serialised as their string names.

export type Role = 'Customer' | 'SupportAgent' | 'Reviewer' | 'Administrator';

export type ApplicationStatus =
  | 'Draft'
  | 'Submitted'
  | 'UnderReview'
  | 'AdditionalInformationRequired'
  | 'Approved'
  | 'Rejected'
  | 'Withdrawn';

export type TicketStatus =
  | 'New'
  | 'Open'
  | 'PendingCustomer'
  | 'PendingInternal'
  | 'Escalated'
  | 'Resolved'
  | 'Closed';

export type TicketPriority = 'Low' | 'Normal' | 'High' | 'Urgent';

export type TicketCategory =
  | 'AccountAccess'
  | 'ApplicationDelay'
  | 'MissingDocuments'
  | 'StatusEnquiry'
  | 'NotificationIssue'
  | 'Other';

export type MessageAuthorType = 'Customer' | 'Agent' | 'System';

export type KycDocumentType =
  | 'ProofOfIdentity'
  | 'ProofOfAddress'
  | 'ProofOfIncome'
  | 'Photograph'
  | 'Other';

export type DocumentReviewStatus = 'Pending' | 'Approved' | 'Rejected' | 'NeedsReplacement';

export type ScanStatus = 'NotScanned' | 'Clean' | 'Infected' | 'ScanFailed' | 'SkippedDev';

export type NotificationType =
  | 'ApplicationSubmitted'
  | 'ApplicationUnderReview'
  | 'AdditionalInformationRequired'
  | 'ApplicationApproved'
  | 'ApplicationRejected'
  | 'DocumentReviewed'
  | 'TicketUpdated'
  | 'General';

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface UserSummary {
  id: string;
  email: string;
  fullName: string;
  roles: Role[];
  isActive: boolean;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  user: UserSummary;
}

export interface MeResponse {
  id: string;
  email: string;
  fullName: string;
  roles: Role[];
  isActive: boolean;
}

export interface ProductResponse {
  id: string;
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
}

export interface ApplicationSummaryResponse {
  id: string;
  reference: string;
  productId: string;
  productName: string;
  status: ApplicationStatus;
  createdAtUtc: string;
  submittedAtUtc: string | null;
  decidedAtUtc: string | null;
}

export interface ApplicationStatusHistoryResponse {
  fromStatus: ApplicationStatus | null;
  toStatus: ApplicationStatus;
  changedByUserId: string | null;
  reason: string | null;
  changedAtUtc: string;
}

export interface DocumentResponse {
  id: string;
  applicationId: string;
  documentType: KycDocumentType;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  scanStatus: ScanStatus;
  reviewStatus: DocumentReviewStatus;
  reviewNotes: string | null;
  uploadedAtUtc: string;
  reviewedAtUtc: string | null;
  isReviewable: boolean;
}

export interface ApplicationDetailResponse {
  id: string;
  reference: string;
  productId: string;
  productName: string;
  status: ApplicationStatus;
  answers: Record<string, string>;
  decisionReason: string | null;
  assignedReviewerId: string | null;
  createdAtUtc: string;
  submittedAtUtc: string | null;
  decidedAtUtc: string | null;
  history: ApplicationStatusHistoryResponse[];
  documents: DocumentResponse[];
}

export interface TicketMessageResponse {
  id: string;
  authorUserId: string | null;
  authorType: MessageAuthorType;
  body: string;
  isInternalNote: boolean;
  createdAtUtc: string;
}

export interface TicketSummaryResponse {
  id: string;
  reference: string;
  subject: string;
  category: TicketCategory;
  priority: TicketPriority;
  status: TicketStatus;
  customerUserId: string | null;
  assignedAgentId: string | null;
  relatedApplicationId: string | null;
  slaDueAtUtc: string | null;
  resolvedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  messageCount: number;
}

export interface TicketDetailResponse {
  id: string;
  reference: string;
  subject: string;
  description: string;
  category: TicketCategory;
  priority: TicketPriority;
  status: TicketStatus;
  customerUserId: string | null;
  assignedAgentId: string | null;
  relatedApplicationId: string | null;
  slaDueAtUtc: string | null;
  resolvedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  messages: TicketMessageResponse[];
}

export interface AiProvenance {
  provider: string;
  model: string | null;
  isLiveInference: boolean;
  requiresHumanReview: boolean;
  disclaimer: string;
}

export interface AiSummaryResponse {
  mainIssue: string;
  keyContext: string[];
  actionsTaken: string[];
  outstandingItems: string[];
  currentState: string;
  uncertainties: string[];
  provenance: AiProvenance;
}

export interface AiSuggestedResponse {
  subject: string;
  body: string;
  notes: string[];
  provenance: AiProvenance;
}

export interface AiSentimentResponse {
  label: string;
  explanation: string;
  confidence: number | null;
  escalationRecommended: boolean;
  signals: string[];
  provenance: AiProvenance;
}

export interface NextBestAction {
  action: string;
  reason: string;
  evidence: string;
  priority: string;
  requiresHumanApproval: boolean;
}

export interface NextBestActionsResponse {
  actions: NextBestAction[];
  provenance: AiProvenance;
}

export interface NotificationResponse {
  id: string;
  type: NotificationType;
  title: string;
  body: string;
  isRead: boolean;
  applicationId: string | null;
  createdAtUtc: string;
  readAtUtc: string | null;
}

export interface ProfileResponse {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  dateOfBirth: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  country: string | null;
  occupation: string | null;
  annualIncomeBand: string | null;
  nationalIdMasked: string | null;
  isContactVerified: boolean;
  isComplete: boolean;
  kycVerifiedAtUtc: string | null;
}

export interface DashboardStatsResponse {
  applicationsDraft: number;
  applicationsSubmitted: number;
  applicationsUnderReview: number;
  applicationsApproved: number;
  applicationsRejected: number;
  openTickets: number;
  unreadNotifications: number;
}

export interface AuditEventResponse {
  id: string;
  actorUserId: string | null;
  actorRole: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  ipAddress: string | null;
  correlationId: string | null;
  succeeded: boolean;
  createdAtUtc: string;
}

export interface DownloadLinkResponse {
  url: string;
  fileName: string;
  expiresAtUtc: string;
}
