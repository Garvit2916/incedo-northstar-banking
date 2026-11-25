# Northstar Banking — Onboarding & AI-Assisted Support

A secure retail-banking **customer onboarding** platform with an **AI-assisted
customer-support workspace**, built as an independent engineering assessment.

A public landing page introduces a focused catalogue of savings, credit-card,
personal-loan and fixed-deposit products. Customers complete an application,
upload KYC documents, submit it, track its progress through a review workflow,
see a masked illustrative account overview and raise/track their own support
tickets. Support agents work a three-pane ticket queue with AI assistance grounded in an approved knowledge base
(summaries, suggested replies, sentiment, classified issues and evidenced
next-best actions). Administrators manage users, products and the audit trail.

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
- [Web experience](#web-experience)
- [Seeded accounts](#seeded-accounts)
- [Testing & verification](#testing--verification)
- [Configuration](#configuration)
- [Security](#security)
- [AI assistance](#ai-assistance)
- [Demo walkthrough](docs/DEMO_GUIDE.md)
- [Solution approach](docs/SOLUTION_APPROACH.md)
- [Project layout](#project-layout)
- [Docker (optional)](#docker-optional)
- [AWS deployment](docs/aws-deployment.md)
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
| `Northstar.Application` | Use-case services (`ApplicationService`, `TicketService`, `DocumentService`, …), DTO contracts, validation, AI orchestration, and the ports (`IApplicationDbContext`, `IAiProvider`, `IBlobStorage`, `IEmailSender`, `INotificationEventPublisher`, `IDateTimeProvider`, …). |
| `Northstar.Infrastructure` | EF Core (`AppDbContext`), ASP.NET Identity, JWT token issuing, blob storage (local FS / Amazon S3), email senders, the outbox processor (email + SNS events), audit writer, AI provider adapters. Implements the Application ports. |
| `Northstar.Api` | HTTP layer: minimal-hosting `Program`, controllers, middleware (exception handling, security headers), rate limiting, CORS, health checks, Swagger. |
| `Northstar.Domain.Tests` / `Northstar.Application.Tests` / `Northstar.Api.IntegrationTests` | xUnit test projects (see [Testing](#testing--verification)). |

Request flow for an onboarding action:

```
Controller ──► Application service ──► Domain entity ──► IApplicationDbContext ──► EF Core
                     │                                          │
                     ├─► IAuditWriter (who/what/when/where)     └─► OutboxMessage ──► OutboxProcessor ──► IEmailSender
                     └─► INotificationService (queued in-app notifications)
                      +-> OutboxMessage -> OutboxProcessor -> INotificationEventPublisher (no-op / SNS)
```

See [`docs/architecture.md`](docs/architecture.md) for the full breakdown,
[`docs/api.md`](docs/api.md) for the endpoint reference,
[`docs/testing.md`](docs/testing.md) for what each test suite covers,
[`docs/project-report.md`](docs/project-report.md) for the full project report
(business problem, methodology, approach, requirements and deliverables),
[`docs/aws-deployment.md`](docs/aws-deployment.md) for the prepared AWS stack,
and [`docs/aws-production-roadmap.md`](docs/aws-production-roadmap.md) for the
longer production-readiness roadmap.

## Technology

- **.NET 10** (ASP.NET Core, EF Core 10, ASP.NET Identity) — API.
- **SQLite** (local default) / **PostgreSQL** (AWS target) via EF Core migrations; the existing SQL Server provider remains selectable.
- **React 18 + TypeScript + Vite** — single-page web client.
- **CSS custom-property design system** — shared tokens, components, layout and page styles with no UI framework dependency.
- **JWT** bearer auth with rotating refresh tokens.
- **AWS Bedrock** (Converse API) via the AWS SDK, with an OpenAI-compatible adapter and a deterministic local fallback.
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

> If `dotnet` is not recognised, prepend it for the session:
> `$env:PATH = "C:\Program Files\dotnet;" + $env:PATH`.

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

## Web experience

The SPA is a responsive, role-aware banking workspace. Navigation, dashboards
and actions adapt to the signed-in role while the API remains the source of
truth for authorization and workflow rules.

| Area | Highlights |
| --- | --- |
| Public | Original Northstar landing page with product overview, journey steps, trust messaging and a clear sign-in CTA. `/login` and `/` are public; all other routes are role-gated. |
| Sign-in | Split marketing/auth layout, registration and a development-only demo account picker. Password reset is not part of this prototype. |
| Customer | Role-aware dashboard with a masked demo account overview (balance, sample transactions and active products), product catalogue with details, guided application wizard, profile completion, document upload, notifications, status tracking and a `/my-tickets` page for raising tickets and following public support replies. |
| Operations | Three-panel AI support workspace (filterable inbox, conversation and composer, AI copilot) with assistive classification, cited knowledge articles, match reasons, workflows and confidence. Review queue and decision workspace, internal notes, assignment and status changes remain human-controlled. |
| Knowledge | Read-only agent knowledge workspace with search, category filters, article detail, resolution steps, customer explanations, agent guidance and escalation criteria. |
| Administration | User role management, product catalogue administration and a filterable audit trail. |
| Shared UX | Dark navigation rail, breadcrumb header, toasts, dialogs, skeleton loading, empty states, tables, pagination and accessible form validation. |

The visual system uses an ink/teal/brass palette with CSS custom properties.
Styles are split into `tokens.css`, `base.css`, `components.css`, `layout.css`
and `pages.css`, imported through `frontend/src/styles.css`. Shared primitives
live in `components/ui.tsx` and `components/forms.tsx`; page-specific
compositions stay in the page files.

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

Seed data also includes **4 active products** (`SAV-001`, `CC-001`, `FD-001`,
`PERS-001`; legacy `WM-001` is deactivated), **10 tickets**
(`NS-TKT-1001`…`NS-TKT-1010`), **26 knowledge articles** (`KB-001`…`KB-026`) and
one submitted demo application (`NS-APP-DEMO01`) for the customer. The customer
account overview is deterministic demo data with masked account number and IFSC.

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

# 3. Frontend checks (Vitest + strict TypeScript/Vite build)
cd frontend; npm install; npm run test; npm run build
```

| Suite | Type | What it proves |
| --- | --- | --- |
| `Northstar.Domain.Tests` | pure unit | State-machine transitions, entity behaviour and invariants. |
| `Northstar.Application.Tests` | service tests (real EF Core, in-memory SQLite) | Onboarding/review/document/ticket/notification use cases, including append-only child handling, ticket reassignment, customer ticket ownership/isolation, internal-note filtering, the registration password policy, AI sentiment persistence, next-best-action shape and summary provenance. |
| `Northstar.Api.IntegrationTests` | HTTP integration (`WebApplicationFactory`) | Auth, health/security headers, the authorization matrix, the full application lifecycle, operations ticket endpoints, scoped customer ticket endpoints, the operations overview and persisted sentiment over real HTTP. |
| `frontend` (Vitest) | frontend unit | Registration password policy, document upload policy, safe AI draft wording, confidence formatting and the knowledge/account/customer-ticket endpoint wiring. |

### Live smoke suite

With the API running on <http://localhost:5199>:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1
```

This drives 97 end-to-end HTTP checks (health, security headers, token rotation,
the full onboarding lifecycle, document upload/validation, notifications,
customer ticket creation/tracking and privacy, operations tickets + AI, masked
account data, knowledge search/detail, grounded citations, assistive
classification, admin and the role-authorization matrix) and exits non-zero on
any failure. See
[`docs/testing.md`](docs/testing.md) for the full list.

## Configuration

Settings live in `backend/src/Northstar.Api/appsettings.json` (overridden by
`appsettings.Development.json`). Key options:

| Section | Key | Values | Default |
| --- | --- | --- | --- |
| `Database` | `Provider` | `Sqlite` \| `Postgres` \| `SqlServer` | `Sqlite` |
| `Database` | `ApplyMigrations` | boolean | `true` |
| `Database` | `EnsureRoles` | boolean | `false` |
| `ConnectionStrings` | `Default` | connection string | `Data Source=northstar.db` |
| `Jwt` | `SigningKey` | ≥32-byte secret | dev-only key injected in Development/Testing |
| `Storage` | `Provider` | `Local` \| `S3` | `Local` |
| `Storage` | `LocalRootPath` | path | `App_Data/kyc` |
| `Storage` | `S3BucketName`, `S3Region` | private bucket / optional region override | empty |
| `Email` | `Provider` | `Noop` \| `Smtp` \| `AzureCommunication` | `Noop` |
| `Sns` | `TopicArn`, `Region` | coarse event topic / optional region override | empty (no-op publisher) |
| `Security` | `EnableHsts`, `RequireHttps` | boolean | `false` outside Development |
| `Ai` | `Provider` | `deterministic` \| `bedrock` \| `openai-compatible` \| `azure-openai` | `deterministic` |
| `Ai` | `BedrockModelId`, `Region`, `GuardrailIdentifier`, `GuardrailVersion` | Bedrock model/inference region/optional guardrail | Claude 3.5 Sonnet v2; region from the AWS credential chain |
| `Ai` | `BaseUrl`, `ApiKey`, `Model` | OpenAI-compatible endpoint and model | unset; `gpt-4o-mini` |
| `FileUpload` | `MaxFileSizeBytes`, `AllowedExtensions`, `AllowedContentTypes` | — | 5 MB; `.pdf/.jpg/.jpeg/.png` |
| `Cors` | `Origins` | allowed SPA origins | `http://localhost:5173` |
| `RateLimiting` | `Auth` / `Ai` / `Uploads` | `PermitLimit`, `WindowSeconds` | see file |
| `Seed` | `Enabled`, `DefaultPassword` | — | disabled in base config; enabled by Development overrides |

`Database:EnsureRoles=true` creates only the four missing Identity roles. It is
independent of `Seed:Enabled` and does not create demo users or other seed data.

Database migrations are provider-specific. SQLite migrations remain in
`Northstar.Infrastructure`; the clean PostgreSQL initial migration lives in
`backend/src/Northstar.Infrastructure.Persistence.PostgresMigrations`. The API
references that migrations project; any other host selecting `Postgres` must do
the same. PostgreSQL selection needs only `Database__Provider=Postgres` and a
PostgreSQL `ConnectionStrings__Default` value and does not require AWS
credentials.

Each external concern sits behind a port, so switching provider is a
configuration change, not a code change (local filesystem / Amazon S3 storage,
SMTP/Azure Communication email, no-op/SNS notification events,
Bedrock/OpenAI-compatible AI). AWS credentials are never read from these files;
the SDK default credential chain is used in AWS.

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
  `X-Frame-Options: DENY`, etc.) on every response; HSTS + HTTPS redirection are
  opt-in outside Development for HTTP-only Fargate demos.
- **Document storage**: KYC blobs are private; S3 uploads use SSE-S3 and are read
  only through short-lived pre-signed URLs. The AWS task role has no delete
  permission.
- **Auditing**: security-relevant actions are written to an append-only audit
  trail (`actor`, `action`, `entity`, `ip`, `correlationId`, `succeeded`).
- **No secrets in the repo**: the signing key and all credentials are supplied
  via configuration/environment or secure task-secret values from the selected
  secret store; a dev-only key is injected automatically only in
  Development/Testing. SNS events contain only a coarse event type and timestamp,
  never customer, KYC, account or email content.

## AI assistance

Agents get four AI helpers on any ticket:

| Helper | Endpoint |
| --- | --- |
| Summarise | `POST /api/tickets/{id}/ai/summarize` |
| Suggest response | `POST /api/tickets/{id}/ai/suggest-response` |
| Analyse sentiment | `POST /api/tickets/{id}/ai/analyze-sentiment` |
| Next best actions | `POST /api/tickets/{id}/ai/next-best-actions` |

By default (`Ai:Provider=deterministic`) these run against a local deterministic
provider so the app is fully functional offline and in CI. Set
`Ai:Provider=bedrock` to use **Amazon Bedrock** through the Converse API. The
Bedrock adapter uses the **AWS default credential chain** (environment variables,
shared profile, or the workload/instance role) - no AWS keys are stored in the
repository. Configure `Ai:Region`, `Ai:BedrockModelId`, and optionally
`Ai:GuardrailIdentifier` / `Ai:GuardrailVersion` for guardrail enforcement; the
identity needs `bedrock:InvokeModel` and, when configured, guardrail permissions.
When an OpenAI-compatible provider is configured (`BaseUrl` + `ApiKey`), requests
go to the live model instead. Both live providers are wrapped in a **resilient
provider that falls back to the deterministic implementation on any error**
(timeout, transport failure, or schema mismatch). Every response carries
`provenance` (`isLiveInference`, `requiresHumanReview`, `disclaimer`) and the UI
labels output as live or fallback.

The workspace also surfaces **persisted sentiment** on the ticket (list, detail
and operations overview), with confidence and frustration/urgency indicators.
Next-best actions are deterministic, explainable rules that include a title,
explanation, priority, evidence, expected outcome, typed action, optional
confidence, a human-confirmation-only demo workflow and citations to approved
knowledge articles. The assistive issue classifier reads the subject,
description and conversation as **untrusted data**, shows matched signals and
never overwrites the persisted ticket category.

Every summary and suggested reply also cites the knowledge articles used to
assemble its guidance. Article text is data, not instructions. Draft replies for
sensitive categories use safe language (secure upload links, approved identity
verification, no OTP/PIN/password requests) and stay editable. Status-changing
actions are proposed only and require explicit confirmation in the UI; there is
no endpoint that executes a privileged action from AI output. AI output is
advisory only, labelled as live inference or deterministic fallback, and must be
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
│   └── src/
│       ├── api/                       # typed API client, DTOs and endpoints
│       ├── auth/                      # auth context and role guards
│       ├── components/                # shared UI, forms, icons, layout, dialogs
│       ├── lib/                       # formatting, uploads, async and policy helpers
│       ├── pages/                     # customer, operations and admin screens
│       └── styles/                    # tokens, base, components, layout, pages
├── docs/                              # architecture, API, AWS and testing notes
├── scripts/smoke.ps1                  # live end-to-end regression suite
└── Northstar.slnx                     # solution
```

## Docker (optional)

A `docker-compose.yml` is provided to run the API and web app together locally.
The current `scripts/smoke.ps1` result below was verified against a locally
started API; the container stack was **not re-verified** for this change.

```powershell
docker compose up --build
# web  : http://localhost:5173   (nginx serving the SPA + proxying /api and /health)
# api  : http://localhost:8080

# tear down (add -v to also drop the northstar-data volume / SQLite DB)
docker compose down
```

> Requires Docker Desktop with the WSL2 backend. The API container runs in
> `Development` (SQLite at `/data/northstar.db`, uploaded documents at
> `/data/kyc`, seeding enabled) on the named `northstar-data` volume and as a
> non-root user. Compose sets `API_UPSTREAM=api:8080`; the same image defaults to
> `127.0.0.1:8080` for the ECS sidecar pattern. The local `dotnet run` +
> `npm run dev` workflow above remains the primary development path.

## AWS deployment

AWS resources are configured manually through the AWS Console. This repository
contains the S3/SNS application adapters, container images and an OIDC
image-publishing workflow; it contains **no infrastructure-as-code** and no ECS
deployment step. Local defaults remain SQLite, local document storage,
no AWS credentials and a no-op SNS publisher. No AWS resource was created,
modified or deleted by this work; console-created resources remain untouched.
The manual configuration guide is in
[`docs/aws-deployment.md`](docs/aws-deployment.md).

## Scope & limitations

- **Local-first.** The solution is provider-agnostic but ships configured for
  local development (SQLite + local file storage + deterministic AI + no-op SNS).
  Amazon S3 / Azure OpenAI / Bedrock / SMTP adapters exist but require
  credentials or configuration to enable.
- **AWS deployment is manual and not executed from this repository.** The OIDC
  image-publishing workflow is included, but no account changes were made here.
  RDS final configuration, secret-store selection and ALB/ingress remain manual
  decisions pending evaluation; see
  [`docs/aws-deployment.md`](docs/aws-deployment.md) before using AWS with real
  data.
- All demonstration content is **fictitious**; there is no integration with a
  core banking system, payment network or real KYC provider.
- The file scanner is a development stub; malware scanning is out of scope.
