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

Expected: **185 tests passing** across the three projects below. The exact
counts are recorded when you run the command; the table summarises scope.

| Project | Type | Scope |
| --- | --- | --- |
| `Northstar.Domain.Tests` | pure unit | `ApplicationStateMachine` transitions (allowed/forbidden), entity behaviour and invariants, ticket lifecycle, enum coverage. |
| `Northstar.Application.Tests` | service tests | Use cases against a **real EF Core stack on in-memory SQLite**: onboarding lifecycle, review decisions, document upload/validation, notifications, ticket messages/assignment — including the append-only child and ticket-reassignment regression. |
| `Northstar.Api.IntegrationTests` | HTTP integration | The API booted with `WebApplicationFactory<Program>`: auth, health, security headers, the authorization matrix, the full application lifecycle and ticket endpoints over real HTTP. |

### Frontend unit tests

The React/TypeScript SPA is covered by **Vitest** (26 tests). Run from
`frontend/`:

```powershell
npm run test
```

`frontend/src/lib/passwordPolicy.test.ts` pins the registration password
rules so the frontend validator stays in agreement with the authoritative
backend policy in `Northstar.Application/Validation/PasswordPolicy.cs`, and
`frontend/src/lib/uploads.test.ts` pins the client-side mirror of the document
upload policy (allowed extensions, size limit) against
`Northstar.Application/Configuration/FileUploadOptions.cs`.

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

Expected: `RESULT  PASS=73  FAIL=0` (exit code 0). To target another host:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1 -BaseUrl http://localhost:5242
```

### What the 73 checks cover

- **Health & headers** — `/health/live`, `/health/ready`, security headers
  (`X-Content-Type-Options`, `X-Frame-Options`).
- **Auth** — login for all four roles, anonymous `401`, bad-password `401`,
  registration, refresh-token rotation, refresh reuse rejection, `/me`.
- **Read paths** — products, `customers/me`, customer and operations dashboards.
- **Application lifecycle** — create, edit draft, submit, history after submit,
  list, withdraw; the approve path (start → approve) and the
  request-information → resubmit → re-start → reject path; `409` on editing a
  closed application and on resubmitting a rejected one.
- **Documents** — PDF upload (`201`), list, authenticated content, download
  link, review; `400` for unsupported extension (message lists the 4 allowed
  extensions) and magic-byte mismatch.
- **Notifications** — list, unread count, mark one read, mark all read.
- **Tickets** — list, get, append message (append-only insert verified),
  internal note, assign, status change, and all four AI endpoints (asserting the
  deterministic fallback provenance).
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
   application, edit and submit it, upload a PDF, and watch notifications appear.
2. Sign in as **reviewer@northstar.example** — open the review queue, start the
   application, request information, then approve or reject it.
3. Sign in as **agent@northstar.example** — open a ticket, add a public message
   and an internal note, assign it, change its status, and run each AI helper.
4. Sign in as **admin@northstar.example** — manage users, create a product and
   inspect the audit trail.

All accounts use the password `Northstar#Dev01`.
