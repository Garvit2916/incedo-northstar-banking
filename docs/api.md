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
| GET | `/operations` | SupportAgent, Reviewer, Administrator | Operations stats. |

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
