# Northstar Banking — Onboarding & AI-Assisted Support

A secure retail-banking **customer onboarding** platform with an **AI-assisted
customer-support workspace**, built as an independent engineering assessment.

Customers browse a (fictitious) product catalogue, complete an application,
upload KYC documents, submit it, and track its progress through a review
workflow. Support agents work a ticket queue with AI assistance (summaries,
suggested replies, sentiment, next-best actions). Administrators manage users,
products and the audit trail.

> **Disclaimer.** This is an independent assessment project. All products,
> customers, tickets, rates and terms are **fictitious**. It is not an official
> Incedo product and is not connected to any real bank. The AI features run
> against a deterministic local provider by default and **always** flag output
> as requiring human review.

---

## Contents

- [Architecture](#architecture)
- [Technology](#technology)
- [Prerequisites](#prerequisites)
- [Run the API](#run-the-api)
- [Run the web app](#run-the-web-app)
- [Seeded accounts](#seeded-accounts)
- [Testing & verification](#testing--verification)
- [Configuration](#configuration)
- [Security](#security)
- [AI assistance](#ai-assistance)
- [Project layout](#project-layout)
- [Docker (optional)](#docker-optional)
- [Scope & limitations](#scope--limitations)

---

## Architecture

The backend is a **Clean Architecture** .NET solution. Dependencies point
inwards only:

```
Northstar.Api ──► Northstar.Infrastructure ──► Northstar.Application ──► Northstar.Domain
```

| Project | Responsibility |
| --- | --- |
| `Northstar.Domain` | Entities, enums, value behaviour, the `ApplicationStateMachine` workflow. No external dependencies. |
| `Northstar.Application` | Use-case services (`ApplicationService`, `TicketService`, `DocumentService`, …), DTO contracts, validation, AI orchestration, and the ports (`IApplicationDbContext`, `IAiProvider`, `IBlobStorage`, `IEmailSender`, `IDateTimeProvider`, …). |
| `Northstar.Infrastructure` | EF Core (`AppDbContext`), ASP.NET Identity, JWT token issuing, blob storage (local FS / Azure Blob), email senders, the outbox processor, audit writer, AI provider adapters. Implements the Application ports. |
| `Northstar.Api` | HTTP layer: minimal-hosting `Program`, controllers, middleware (exception handling, security headers), rate limiting, CORS, health checks, Swagger. |
| `Northstar.Domain.Tests` / `Northstar.Application.Tests` / `Northstar.Api.IntegrationTests` | xUnit test projects (see [Testing](#testing--verification)). |

Request flow for an onboarding action:

```
Controller ──► Application service ──► Domain entity ──► IApplicationDbContext ──► EF Core
                     │                                          │
                     ├─► IAuditWriter (who/what/when/where)     └─► OutboxMessage ──► OutboxProcessor ──► IEmailSender
                     └─► INotificationService (queued in-app notifications)
```

See [`docs/architecture.md`](docs/architecture.md) for the full breakdown,
[`docs/api.md`](docs/api.md) for the endpoint reference and
[`docs/testing.md`](docs/testing.md) for what each test suite covers.

## Technology

- **.NET 10** (ASP.NET Core, EF Core 10, ASP.NET Identity) — API.
- **SQLite** (development) / **SQL Server** (production profile) via EF Core migrations.
- **React 18 + TypeScript + Vite** — single-page web client.
- **JWT** bearer auth with rotating refresh tokens.
- **xUnit** — unit and integration tests (`WebApplicationFactory`).
- **Vitest** — frontend unit tests for the client-side policy mirrors.
- **Swashbuckle** — OpenAPI/Swagger UI (development only).

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/) (the repo targets `net10.0`).
- [Node.js 22+](https://nodejs.org/) (Vite 7 / Vitest 4 require Node `^20.19.0 || >=22.12.0`; 22 LTS recommended) and npm.
- PowerShell (only for the optional smoke script).
- Docker is **optional** (see [Docker](#docker-optional)).

## Run the API

From the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS        = "http://localhost:5199"
dotnet run --project backend/src/Northstar.Api --no-launch-profile
```

- The database (SQLite, `northstar.db`) is created and migrated automatically on
  startup and seeded with the demonstration data below.
- Swagger UI: <http://localhost:5199/swagger>
- Health: <http://localhost:5199/health/live> and `/health/ready`

> Port **5199** is used throughout the docs because it matches the Vite dev
> proxy (`frontend/vite.config.ts`) and the smoke script default. The Visual
> Studio launch profile uses 5242; if you run without an explicit URL, update
> the proxy and smoke `-BaseUrl` accordingly.

### Reset the local database

Stop the API and delete the SQLite files, then start again:

```powershell
Remove-Item backend/src/Northstar.Api/northstar.db*
```

## Run the web app

```powershell
cd frontend
npm install
npm run dev
```

Open <http://localhost:5173>. The dev server proxies `/api` and `/health` to
`http://localhost:5199`, so start the API first. To point the SPA at a different
backend, copy `frontend/.env.example` to `frontend/.env` and set
`VITE_API_BASE_URL`.

The seeded demo logins are shown on the sign-in page only in development
(`npm run dev`). For a non-development build they are hidden unless
`VITE_ENABLE_DEMO_ACCOUNTS=true` is supplied at build time — the local
Docker Compose stack opts in deliberately (`frontend/.env.example` documents
the flag); never enable it for a publicly exposed build.

Production build:

```powershell
cd frontend
npm run build   # tsc --noEmit && vite build  ->  frontend/dist
```

## Seeded accounts

Development seeding is idempotent. All accounts use the password
`Northstar#Dev01` (override with `Seed:DefaultPassword`).

| Role | Email |
| --- | --- |
| Administrator | `admin@northstar.example` |
| Reviewer | `reviewer@northstar.example` |
| Reviewer (2nd) | `reviewer2@northstar.example` |
| Support agent | `agent@northstar.example` |
| Customer | `customer@northstar.example` |

Seed data also includes **4 products** (`SAV-001`, `FD-001`, `PERS-001`,
`WM-001`), **5 tickets** (`NS-TKT-1001`…`1005`) and one submitted demo
application (`NS-APP-DEMO01`) for the customer.

## Testing & verification

Three automated suites plus a live smoke suite. Run everything from the repo
root.

> If `dotnet` is not on your `PATH`, prepend it for the session, e.g.
> `$env:PATH = "C:\Program Files\dotnet;" + $env:PATH`.

```powershell
# 1. Build the whole solution
dotnet build Northstar.slnx -v q --nologo

# 2. Run all automated tests (Domain + Application + API integration)
dotnet test Northstar.slnx

# 3. Frontend unit tests (Vitest)
cd frontend; npm install; npm run test
```

| Suite | Type | What it proves |
| --- | --- | --- |
| `Northstar.Domain.Tests` | pure unit | State-machine transitions, entity behaviour and invariants. |
| `Northstar.Application.Tests` | service tests (real EF Core, in-memory SQLite) | Onboarding/review/document/ticket/notification use cases, including append-only child handling, ticket reassignment and the registration password policy. |
| `Northstar.Api.IntegrationTests` | HTTP integration (`WebApplicationFactory`) | Auth, health/security headers, the authorization matrix, the full application lifecycle and ticket endpoints over real HTTP. |
| `frontend` (Vitest) | frontend unit | The registration password-policy mirror, confirmation rules and the document upload policy mirror. |

### Live smoke suite

With the API running on <http://localhost:5199>:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1
```

This drives 73 end-to-end HTTP checks (health, security headers, token rotation,
the full onboarding lifecycle, document upload/validation, notifications,
tickets + AI, admin and the role-authorization matrix) and exits non-zero on any
failure. See [`docs/testing.md`](docs/testing.md) for the full list.

## Configuration

Settings live in `backend/src/Northstar.Api/appsettings.json` (overridden by
`appsettings.Development.json`). Key options:

| Section | Key | Values | Default |
| --- | --- | --- | --- |
| `Database` | `Provider` | `Sqlite` \| `SqlServer` | `Sqlite` |
| `ConnectionStrings` | `Default` | connection string | `Data Source=northstar.db` |
| `Jwt` | `SigningKey` | ≥32-byte secret | dev-only key injected in Development/Testing |
| `Storage` | `Provider` | `Local` \| `Azure` | `Local` |
| `Storage` | `LocalRootPath` | path | `App_Data/kyc` |
| `Email` | `Provider` | `Noop` \| `Smtp` \| `AzureCommunication` | `Noop` |
| `Ai` | `Provider` | `deterministic` \| (an OpenAI-compatible provider) | `deterministic` |
| `FileUpload` | `MaxFileSizeBytes`, `AllowedExtensions`, `AllowedContentTypes` | — | 5 MB; `.pdf/.jpg/.jpeg/.png` |
| `Cors` | `Origins` | allowed SPA origins | `http://localhost:5173` |
| `RateLimiting` | `Auth` / `Ai` / `Uploads` | `PermitLimit`, `WindowSeconds` | see file |
| `Seed` | `Enabled`, `DefaultPassword` | — | `true`, dev password |

Each external concern sits behind a port, so switching provider is a
configuration change, not a code change (Azure Blob, SMTP/Azure Communication
email, OpenAI-compatible AI).

## Security

- **Authentication**: JWT bearer access tokens (15 min) + rotating refresh
  tokens (7 days). Refresh tokens are single-use; reuse is rejected.
- **Authorization**: role-based (`Customer`, `SupportAgent`, `Reviewer`,
  `Administrator`) enforced at the controller boundary. Cross-customer access to
  an application returns **404** (not 403) to avoid leaking existence.
- **Password policy**: ASP.NET Identity — minimum length 10, upper/lower/digit,
  lockout after 5 failed attempts.
- **Upload safety**: extension allow-list, content-type check, magic-byte
  signature validation, and a size limit.
- **Headers**: security headers middleware (`X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, etc.) on every response; HSTS + HTTPS redirection
  outside Development.
- **Auditing**: security-relevant actions are written to an append-only audit
  trail (`actor`, `action`, `entity`, `ip`, `correlationId`, `succeeded`).
- **No secrets in the repo**: the signing key and all credentials are supplied
  via configuration/environment; a dev-only key is injected automatically only
  in Development/Testing.

## AI assistance

Agents get four AI helpers on any ticket:

| Helper | Endpoint |
| --- | --- |
| Summarise | `POST /api/tickets/{id}/ai/summarize` |
| Suggest response | `POST /api/tickets/{id}/ai/suggest-response` |
| Analyse sentiment | `POST /api/tickets/{id}/ai/analyze-sentiment` |
| Next best actions | `POST /api/tickets/{id}/ai/next-best-actions` |

By default (`Ai:Provider=deterministic`) these run against a local deterministic
provider so the app is fully functional offline and in CI. When an
OpenAI-compatible provider is configured (`BaseUrl` + `ApiKey`), requests go to
the live model wrapped in a **resilient provider that falls back to the
deterministic implementation on any error**. Every response carries
`provenance` (`isLiveInference`, `requiresHumanReview`, `disclaimer`) and the UI
labels output as live or fallback. AI output is assistive only and must be
reviewed by a human.

## Project layout

```
.
├── backend/
│   ├── src/
│   │   ├── Northstar.Api/             # HTTP host, controllers, middleware
│   │   ├── Northstar.Application/     # use cases, DTOs, ports, validation
│   │   ├── Northstar.Domain/          # entities, enums, workflow
│   │   └── Northstar.Infrastructure/  # EF Core, Identity, storage, email, AI, outbox
│   └── tests/
│       ├── Northstar.Domain.Tests/
│       ├── Northstar.Application.Tests/
│       └── Northstar.Api.IntegrationTests/
├── frontend/                          # React + TypeScript + Vite SPA
│   └── src/{api,auth,components,lib,pages}/
├── docs/                              # architecture, API and testing notes
├── scripts/smoke.ps1                  # live end-to-end regression suite
├── infra/                             # reserved for infrastructure-as-code
└── Northstar.slnx                     # solution
```

## Docker (optional)

A `docker-compose.yml` is provided to run the API and web app together locally
(verified end-to-end: images build, both containers come up healthy, and the
full `scripts/smoke.ps1` suite passes 73/73 against the containerised API).

```powershell
docker compose up --build
# web  : http://localhost:5173   (nginx serving the SPA + proxying /api and /health)
# api  : http://localhost:8080

# tear down (add -v to also drop the northstar-data volume / SQLite DB)
docker compose down
```

> Requires Docker Desktop with the WSL2 backend. The API container runs in
> `Development` (SQLite at `/data/northstar.db`, uploaded documents at
> `/data/kyc`, seeding enabled) on the named `northstar-data` volume. The local
> `dotnet run` + `npm run dev` workflow above remains the primary development
> path.

## Scope & limitations

- **Local-first.** The solution is provider-agnostic but ships configured for
  local development (SQLite + local file storage + deterministic AI). Azure
  Blob / Azure OpenAI / SMTP adapters exist but require credentials to enable.
- **No infrastructure-as-code** is included; `infra/` is reserved.
- All demonstration content is **fictitious**; there is no integration with a
  core banking system, payment network or real KYC provider.
- The file scanner is a development stub; malware scanning is out of scope.
