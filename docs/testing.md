# Testing & verification

The solution is verified at four levels: three automated xUnit suites plus a
live HTTP smoke suite. All commands run from the repository root.

> If `dotnet` is not on your `PATH`:
> `$env:PATH = "C:\Program Files\dotnet;" + $env:PATH`

## Build

```powershell
dotnet build Northstar.slnx -v q --nologo
```

Expected: **0 warnings, 0 errors**.

## Automated tests

```powershell
dotnet test Northstar.slnx
```

Latest verified result: **228 tests passing** across the three projects below
(Domain 108, Application 75, API integration 45), with 0 failures.

| Project | Type | Scope |
| --- | --- | --- |
| `Northstar.Domain.Tests` | pure unit | `ApplicationStateMachine` transitions (allowed/forbidden), entity behaviour and invariants, ticket lifecycle, enum coverage. |
| `Northstar.Application.Tests` | service tests | Use cases against a **real EF Core stack on in-memory SQLite**: onboarding lifecycle, review decisions, document upload/validation, notifications and outbox event safety, ticket messages/assignment — including the append-only child and ticket-reassignment regression — customer ticket ownership/isolation/internal-note filtering, plus AI sentiment persistence, summary provenance and next-best-action shape. AWS adapters and provider selection are covered with proxied SDK clients and require no credentials. |
| `Northstar.Api.IntegrationTests` | HTTP integration | The API booted with `WebApplicationFactory<Program>`: auth, health, security headers, the authorization matrix, the full application lifecycle, operations and customer ticket endpoints, the operations overview and persisted sentiment over real HTTP. |

All suites run locally with no AWS credentials, cloud resources or infrastructure
tooling. AWS adapters are exercised with proxied SDK clients only.

### Frontend unit tests

The React/TypeScript SPA is covered by **Vitest** (37 tests across 4 files).
Run from `frontend/`:

```powershell
npm run test
```

`frontend/src/lib/passwordPolicy.test.ts` pins the registration password
rules so the frontend validator stays in agreement with the authoritative
backend policy in `Northstar.Application/Validation/PasswordPolicy.cs`, and
`frontend/src/lib/uploads.test.ts` pins the client-side mirror of the document
upload policy (allowed extensions, size limit) against
`Northstar.Application/Configuration/FileUploadOptions.cs`.
`frontend/src/lib/aiDrafts.test.ts` verifies that generated drafts keep secure
upload/identity-verification language and never solicit credentials, while
`frontend/src/api/endpoints.test.ts` verifies knowledge, account-overview and
customer-ticket route construction (including that no customer id is sent)
without a live backend.

### How the integration tests are wired

`NorthstarApiFactory` starts the real application host with an isolated,
shared **in-memory SQLite** database. Because the minimal-hosting `Program` reads
configuration eagerly before `Build()`, all overrides are supplied as
**environment variables** (double-underscore, e.g. `ConnectionStrings__Default`,
`Seed__Enabled`, `RateLimiting__Auth__PermitLimit`) set in the factory
constructor. The schema is migrated in the constructor (before the host — and its
`OutboxProcessor` background service — start), then seeded once. Test classes in
the `api` collection run sequentially, which keeps SQLite writes contention-free.

## Live smoke suite

Start the API on <http://localhost:5199>, then:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1
```

Latest verified result: `RESULT  PASS=97  FAIL=0` (exit code 0). To target another host:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1 -BaseUrl http://localhost:5242
```

### What the 97 checks cover

- **Health & headers** — `/health/live`, `/health/ready`, security headers
  (`X-Content-Type-Options`, `X-Frame-Options`).
- **Auth** — login for all four roles, anonymous `401`, bad-password `401`,
  registration, refresh-token rotation, refresh reuse rejection, `/me`.
- **Read paths** — products and the active catalogue (`SAV-001`, `CC-001`,
  `FD-001`, `PERS-001`, with legacy `WM-001` excluded), `customers/me`, customer
  and operations dashboards.
- **Account overview** — masked account number/IFSC, demo notice, sample
  transactions and active products; agent access returns `403`.
- **Knowledge base** — at least 20 seeded articles, categories, keyword search,
  cited article detail and `403` for customer access.
- **Application lifecycle** — create, edit draft, submit, history after submit,
  list, withdraw; the approve path (start → approve) and the
  request-information → resubmit → re-start → reject path; `409` on editing a
  closed application and on resubmitting a rejected one.
- **Documents** — PDF upload (`201`), list, authenticated content, download
  link, review; `400` for unsupported extension (message lists the 4 allowed
  extensions) and magic-byte mismatch.
- **Notifications** — list, unread count, mark one read, mark all read, separate
  email/SNS outbox rows, and sanitized external event payloads.
- **Tickets** — operations overview metrics, list, get, append message
  (append-only insert verified), internal note, assign, status change, all four
  AI endpoints (asserting the deterministic fallback provenance), and sentiment
  persistence on the ticket record.
- **Customer self-service tickets** — create, list and detail scoped to the
  caller, `Location` header on create, opening message authored by `Customer`,
  internal notes and sentiment signals hidden from customer responses, own
  application accepted, another customer's application rejected with `404`, and
  operations roles rejected with `403` on all three routes.
- **Grounded AI** — summary and suggested-reply KB citations on `NS-TKT-1006`;
  action titles, explanations, sources, safe workflows and human approval flags;
  assistive classification signals that never overwrite the recorded category;
  and no privileged/mutating AI route in `swagger.json`.
- **Admin** — users, audit, product creation, role assignment, deactivation, and
  that a deactivated user can no longer log in. The smoke suite creates a `TST*`
  product and then **deactivates it again**, so it never litters the customer
  catalogue (the seeder also cleans up any leftover active `TST*` rows on start).
- **Authorization matrix** — customer/reviewer/agent blocked from endpoints
  outside their role (`403`), and a cross-tenant application read returning `404`.
- **Swagger** — index page and generated `swagger.json`, confirming the document
  upload is described as `multipart/form-data`.

## Frontend

```powershell
cd frontend
npm install
npm run build     # tsc --noEmit && vite build
npm run test      # vitest run
```

`npm run build` type-checks the whole SPA (strict mode, no unused locals or
parameters) and produces a production bundle in `frontend/dist`. `npm run lint`
runs the type-check alone, and `npm run test` runs the Vitest unit tests.

## Manual verification checklist

For a hands-on pass with all roles, after starting the API and web app:

1. Sign in as **customer@northstar.example** — browse products, start an
   application, edit and submit it, upload a PDF, raise a ticket from
   `/my-tickets`, then watch notifications and ticket status appear.
2. Sign in as **reviewer@northstar.example** — open the review queue, start the
   application, request information, then approve or reject it.
3. Sign in as **agent@northstar.example** — open a ticket, add a public message
   and an internal note, assign it, change its status, and run each AI helper.
4. Sign in as **admin@northstar.example** — manage users, create a product and
   inspect the audit trail.

All accounts use the password `Northstar#Dev01`.
