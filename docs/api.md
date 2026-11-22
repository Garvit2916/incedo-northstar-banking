# API reference

Base URL (development): `http://localhost:5199`. All routes are under `/api`.
Requests and responses are JSON (enums serialised as string names). Swagger UI is
available at `/swagger` in Development.

## Conventions

- **Auth**: protected endpoints require `Authorization: Bearer <accessToken>`
  obtained from `/api/auth/login`, `/api/auth/register` or `/api/auth/refresh`.
- **Errors**: RFC 7807 problem details (`application/problem+json`) with
  `title` and `detail`. Common status codes: `400` validation, `401`
  unauthenticated, `403` wrong role, `404` not found / not yours, `409`
  conflict (invalid state transition), `429` rate limited.
- **Pagination**: list endpoints return
  `{ items, page, pageSize, totalCount, totalPages }` and accept `page` /
  `pageSize` query parameters.
- **Roles**: `Customer`, `SupportAgent`, `Reviewer`, `Administrator`.

## Auth — `/api/auth`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| POST | `/register` | anonymous | Body: `email, password, firstName, lastName, phoneNumber?` → `AuthResponse`. Rate limited (`auth`). |
| POST | `/login` | anonymous | Body: `email, password` → `AuthResponse`. Rate limited (`auth`). |
| POST | `/refresh` | anonymous | Body: `refreshToken` → new `AuthResponse`. Refresh tokens rotate; reuse is rejected. Rate limited (`auth`). |
| POST | `/logout` | authenticated | Body: `refreshToken`; revokes the refresh token. |
| GET | `/me` | authenticated | → `MeResponse`. |

`AuthResponse`: `{ accessToken, accessTokenExpiresAtUtc, refreshToken,
refreshTokenExpiresAtUtc, user: { id, email, fullName, roles, isActive } }`.

## Products — `/api/products`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| GET | `/` | authenticated | Query `includeInactive` (default false). |
| GET | `/{id}` | authenticated | Single product. |

## Customer profile — `/api/customers`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| GET | `/me` | Customer | → `ProfileResponse`. |
| PUT | `/me` | Customer | Update the current customer's profile. |

## Applications — `/api/applications`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| POST | `/` | Customer | Body: `{ productId, answers: Record<string,string> }` → draft detail. |
| GET | `/` | Customer | List the caller's applications (paged). |
| GET | `/{id}` | Customer | Detail incl. `answers`, `history`, `documents`. Cross-tenant → 404. |
| PUT | `/{id}` | Customer | Update draft answers. Only `Draft`/`AdditionalInformationRequired`. |
| POST | `/{id}/submit` | Customer | Submit. Requires complete profile + ≥1 answer. |
| POST | `/{id}/withdraw` | Customer | Withdraw a `Draft` application. |
| GET | `/{id}/history` | Customer | Status history. |

## Documents — `/api` and `/api/applications`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| POST | `/applications/{applicationId}/documents` | Customer | `multipart/form-data`: `file`, `documentType`. Rate limited (`uploads`). |
| GET | `/applications/{applicationId}/documents` | Customer, Reviewer, Administrator | List documents for an application. |
| POST | `/documents/{id}/review` | Reviewer, Administrator | Body: `{ decision, notes? }`. |
| GET | `/documents/{id}/link` | Customer, Reviewer, Administrator | → `DownloadLinkResponse`. |
| GET | `/documents/{id}/content` | Customer, Reviewer, Administrator | Streams the file (authenticated). |

Uploads are validated for extension (allow-list: `.pdf`, `.jpg`, `.jpeg`,
`.png`), declared content type, magic-byte signature and size
(`FileUpload.MaxFileSizeBytes`).

## Reviews — `/api/reviews`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| GET | `/` | Reviewer, Administrator | Queue; query `status`, `search`, `page`, `pageSize`. |
| GET | `/{id}` | Reviewer, Administrator | Application detail for review. |
| POST | `/{id}/start` | Reviewer, Administrator | `Submitted → UnderReview`. |
| POST | `/{id}/request-information` | Reviewer, Administrator | Body `{ reason }` → `AdditionalInformationRequired`. |
| POST | `/{id}/approve` | Reviewer, Administrator | Body `{ reason }` → `Approved`. |
| POST | `/{id}/reject` | Reviewer, Administrator | Body `{ reason }` → `Rejected`. |

## Tickets — `/api/tickets`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| GET | `/` | SupportAgent, Administrator | Query `status`, `category`, `priority`, `assignedAgentId`, `search`, paging. |
| GET | `/overview` | SupportAgent, Administrator | Operations overview counts (open, unassigned, urgent, escalated, awaiting customer, resolved today, assigned to me, negative/analysed sentiment). |
| POST | `/` | SupportAgent, Administrator | Create a ticket (seeds an initial agent message). |
| GET | `/{id}` | SupportAgent, Administrator | Detail incl. `messages`. |
| POST | `/{id}/messages` | SupportAgent, Administrator | Append message. Body `{ body, isInternalNote }`. |
| PATCH | `/{id}/status` | SupportAgent, Administrator | Body `{ status }`. |
| POST | `/{id}/assign` | SupportAgent, Administrator | Body `{ agentUserId }`. |
| POST | `/{id}/ai/summarize` | SupportAgent, Administrator | AI summary. Rate limited (`ai`). |
| POST | `/{id}/ai/suggest-response` | SupportAgent, Administrator | Suggested reply. Rate limited (`ai`). |
| POST | `/{id}/ai/analyze-sentiment` | SupportAgent, Administrator | Sentiment. Rate limited (`ai`). |
| POST | `/{id}/ai/next-best-actions` | SupportAgent, Administrator | Next best actions. Rate limited (`ai`). |

Every AI response includes `provenance`:
`{ provider, model, isLiveInference, requiresHumanReview, disclaimer }`.
`summarize`, `suggest-response` and `next-best-actions` also return `sources`:
`{ id, articleCode, title, category, score, matchReason }` from the approved
knowledge base. Next-best actions add `title`, `explanation`, `sources`,
`suggestedWorkflow`, `confidence` and `requiresHumanApproval: true`, plus an
assistive `classification` on the response
(`suggestedCategory`, `issueType`, `confidence`, `signals`, `explanation`).
Classification never changes the recorded ticket category, and there is no
AI endpoint that performs a status change or privileged action. Related
application references, statuses and document review states are injected into AI
context only for `Reviewer`/`Administrator` callers; support agents receive
ticket-only context.

## Customer self-service tickets — `/api/customer/tickets`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| POST | `/` | Customer | Create a ticket. Body `{ subject, description, category, priority, relatedApplicationId? }`; `CustomerUserId` is never accepted. An optional related application must belong to the caller (`404` otherwise). |
| GET | `/` | Customer | List only the caller's tickets, newest first; query `page`, `pageSize`. Message counts exclude internal notes. |
| GET | `/{id}` | Customer | Detail for a ticket owned by the caller (`404` otherwise). Agent internal notes are filtered out. |

Responses use customer-specific DTOs that omit `latestSentiment` and
`sentimentAnalyzedAtUtc`, which are internal triage signals. The opening message
is authored by `Customer` and is public. Operations roles receive `403` on all
three routes and continue to use `/api/tickets`.

## Notifications — `/api/notifications`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| GET | `/` | authenticated | Query `unreadOnly`, paging. |
| GET | `/unread-count` | authenticated | Integer count. |
| POST | `/{id}/read` | authenticated | Mark one read. |
| POST | `/read-all` | authenticated | Mark all read; returns count affected. |

## Dashboard — `/api/dashboard`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| GET | `/customer` | Customer | Customer stats. |
| GET | `/account-overview` | Customer | Deterministic demo account view with masked account number/IFSC, balance, sample transactions and active products. |
| GET | `/operations` | SupportAgent, Reviewer, Administrator | Operations stats. |

## Knowledge — `/api/knowledge`

Read-only catalogue of approved demonstration articles used to ground Copilot
output. Customers receive `403`.

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| GET | `/` | SupportAgent, Administrator | Query `search`, `category`, `product`, `page`, `pageSize`; returns paged articles. |
| GET | `/categories` | SupportAgent, Administrator | Distinct category names. |
| GET | `/{id}` | SupportAgent, Administrator | Full article incl. resolution steps, customer explanation, agent guidance and escalation criteria. |

`KnowledgeArticle`: `{ id, articleCode, title, category, tags, keywords,
content, applicableProduct, issueType, priority, resolutionSteps,
customerExplanation, agentGuidance, escalationCriteria, isActive,
createdAtUtc, updatedAtUtc }`. There are no create/update routes in the
prototype.

## Admin — `/api/admin`

| Method | Path | Roles | Notes |
| --- | --- | --- | --- |
| GET | `/users` | Administrator | List users with roles/status. |
| PUT | `/users/{id}/roles` | Administrator | Body `{ roles: string[] }`. |
| PUT | `/users/{id}/active` | Administrator | Body `{ isActive }`. |
| GET | `/audit` | Administrator | Query `action`, `actorUserId`, paging. |
| POST | `/products` | Administrator | Create a product. |
| PUT | `/products/{id}` | Administrator | Update a product. |

## Health

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/health/live` | Liveness (no dependencies). |
| GET | `/health/ready` | Readiness (includes a database check). |

These are **outside** `/api` and require no authentication.
