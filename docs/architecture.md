# Architecture

Northstar is a **Clean Architecture** .NET 10 solution with a separate React SPA.
This document describes the layers, the key components, the domain model, the
main workflows and the cross-cutting concerns.

## Solution structure

```
Northstar.Api ──► Northstar.Infrastructure ──► Northstar.Application ──► Northstar.Domain
```

Dependencies point inwards. `Northstar.Domain` has no project or framework
dependencies. `Northstar.Application` depends only on `Northstar.Domain` and
abstracts every external concern behind a port. `Northstar.Infrastructure`
implements those ports. `Northstar.Api` composes everything and exposes HTTP.

## Layers

### Domain (`Northstar.Domain`)

Pure business model — no EF Core, no ASP.NET.

- **Entities**: `Application`, `ApplicationStatusHistory`, `CustomerProfile`,
  `KycDocument`, `Product`, `Ticket`, `TicketMessage`, `TicketAssignment`,
  `Notification`, `AuditEvent`, `OutboxMessage`, `RefreshToken`,
  `AiAssistanceRecord`.
- **Enums** (`Enums.cs`): `ApplicationStatus`, `TicketStatus`, `TicketPriority`,
  `TicketCategory`, `MessageAuthorType`, `KycDocumentType`,
  `DocumentReviewStatus`, `ScanStatus`, `NotificationType`, `Roles`.
- **Workflow**: `ApplicationStateMachine` is the single source of truth for the
  application lifecycle.
- **Behaviour** lives on the entities themselves: `Application.Submit`,
  `Application.ChangeStatus`, `Ticket.ChangeStatus`, `Ticket.AssignTo`, etc.
  Collections such as `Ticket.Messages` are *append-only children*.

#### Application lifecycle

```
Draft ──submit──► Submitted ──start──► UnderReview ──approve──► Approved
  │                   │                    │       ──reject───► Rejected
  │                   │                    └──request-info──► AdditionalInformationRequired
  │                   │                                              │
  └──────withdraw─────┴──────────────────────────────────────────────┴──► Withdrawn
                                                     addinfo ──resubmit──► Submitted
```

`Draft → Submitted` records exactly one history transition. Terminal states
(`Approved`, `Rejected`, `Withdrawn`) have no outgoing transitions. Submitting
requires a complete customer profile and at least one answer; the product's
`MinimumAge` is enforced as an eligibility rule.

### Application (`Northstar.Application`)

Use-case services and contracts. This layer knows *what* the system does, not
*how* it is stored or transported.

- **Services**: `ApplicationService`, `CustomerService`, `DocumentService`,
  `TicketService`, `NotificationService`, `AiAssistanceService`,
  `ProductService`, `AdminService`, `DashboardService`.
- **Contracts**: request/response DTOs grouped by area (`ApplicationContracts`,
  `TicketContracts`, …) in `Contracts/`.
- **Ports** (`Abstractions/`): `IApplicationDbContext`, `IAiProvider`,
  `IBlobStorage`, `IEmailSender`, `IFileScanner`, `IAuditWriter`,
  `INotificationService`, `ICurrentUser`, `IDateTimeProvider`,
  `IReferenceGenerator`, `IAuthService`, `IUserDirectory`.
- **Validation**: FluentValidation validators; failures surface as HTTP 400.
- **AI orchestration**: prompt assembly, guidance catalog, sentiment rules and
  the deterministic next-best-action engine live here, independent of the
  concrete model provider.

Services receive `IApplicationDbContext` and their collaborators via
constructor injection and are registered with `AddApplication()`.

### Infrastructure (`Northstar.Infrastructure`)

Adapters and persistence. Everything external lives here.

- **Persistence**: `AppDbContext : IdentityDbContext<ApplicationUser,
  IdentityRole<Guid>, Guid>, IApplicationDbContext`. Entity configuration is via
  `ApplyConfigurationsFromAssembly` (`Persistence/Configurations.cs`); EF
  migrations in `Persistence/Migrations`; `DbInitializer` migrates then seeds.
- **Identity & tokens**: `AuthService`, `TokenService`, `UserDirectory`,
  `CurrentUser`, `JwtOptions`.
- **Storage**: `FileSystemBlobStorage` (default) and `AzureBlobStorage`,
  selected by `Storage:Provider`.
- **Email**: `NoopEmailSender`, `SmtpEmailSender`,
  `AzureCommunicationEmailSender`, selected by `Email:Provider`.
- **AI**: `DeterministicFallbackAiProvider` (default) and
  `OpenAiCompatibleAiProvider`, wrapped by `ResilientAiProvider`.
- **Cross-cutting**: `AuditWriter`, `OutboxProcessor` (hosted service),
  `ReferenceGenerator`, `SystemDateTimeProvider`, `DevelopmentFileScanner`.

`AddInfrastructure(configuration, environment)` wires all of the above.

### API (`Northstar.Api`)

The HTTP edge.

- `Program.cs` (minimal hosting) builds the host: configuration, Kestrel,
  authentication, authorization, rate limiting, CORS, health checks, Swagger,
  problem details, and exposes `public partial class Program;` for the
  integration-test host.
- **Controllers** under `Controllers/` map routes to Application services.
  Role requirements are enforced here via `[Authorize(Roles = …)]`.
- **Middleware**: `ExceptionHandlingMiddleware` (maps domain/validation
  exceptions to RFC 7807 problem responses) and `SecurityHeadersMiddleware`.
- **Rate limiting** policies `auth`, `ai`, `uploads`, all configuration-driven.

## Data model

Key relationships (SQLite enforces foreign keys):

- `ApplicationUser` (Identity) ──1:1── `CustomerProfile` (when the user is a
  customer).
- `Product` ──1:N── `Application`.
- `Application` ──N── `ApplicationStatusHistory` (append-only), and ──N──
  `KycDocument`.
- `Ticket` ──N── `TicketMessage` (append-only) and ──N──
  `TicketAssignment` (append-only history of assignments).
- `Notification`, `AuditEvent`, `OutboxMessage`, `RefreshToken`,
  `AiAssistanceRecord` are standalone audit/queue tables.

By design there are **no foreign keys** on loose reference ids that may dangle
by intent: `Ticket.CustomerUserId`, `Ticket.AssignedAgentId`,
`TicketMessage.AuthorUserId`, `TicketAssignment.*UserId`,
`AuditEvent.ActorUserId`, `RefreshToken.UserId`. Persisted references that must
be valid (`CustomerProfile.UserId`, `Notification.UserId`) **do** have FKs.

### Append-only children

`ApplicationStatusHistory`, `TicketMessage` and `TicketAssignment` are
immutable children of their aggregate. `AppDbContext.SaveChangesAsync` promotes a
`Modified` instance of these types to `Added` **only when no property's original
value differs from its current value** — i.e. a *new* child added through a
navigation property is inserted rather than updated. This rule keeps historical
rows append-only while still allowing a row that was loaded and genuinely edited
to update, and it fixes ticket reassignment (where a new assignment row was
otherwise mis-issued as an `UPDATE`, violating the primary key).

## Main workflows

**Onboarding.** Customer creates a draft (`POST /api/applications`), edits
answers, optionally uploads documents, then submits. Submission validates profile
completeness, presence of answers and age eligibility, records one history
transition, queues a notification and writes an audit event. A reviewer picks the
application from the queue, starts review, and approves, rejects or requests
more information; the latter allows the customer to amend and resubmit.

**Documents.** Uploads are validated (extension, content type, magic bytes,
size), stored via `IBlobStorage`, recorded, and scanned by the dev scanner.
Customers, reviewers and administrators may list and fetch content; only
reviewers/administrators may set the review status. Downloads are served through
the authenticated content endpoint (or a presigned URL when Azure storage is
configured).

**Support + AI.** Agents open tickets, append public messages or internal notes,
reassign and change status. On demand, four AI helpers run over the ticket
thread; the deterministic provider guarantees offline behaviour and the resilient
wrapper falls back to it if a live model errors. Every AI response carries
provenance metadata and is marked as requiring human review.

**Notifications.** In-app notifications are queued inline within the same
transaction as the triggering change. Outbound email is queued as an
`OutboxMessage` and delivered asynchronously by the `OutboxProcessor`, so
delivery failures never roll back the business transaction.

## Cross-cutting concerns

| Concern | Implementation |
| --- | --- |
| Errors | `ExceptionHandlingMiddleware` → RFC 7807 problem details (400/403/404/409/…) |
| Security headers | `SecurityHeadersMiddleware` on every response |
| AuthN | JWT bearer; access token 15 min, refresh token 7 days, rotation + reuse detection |
| AuthZ | Role-based at controller boundary; cross-tenant reads return 404 |
| Rate limiting | Fixed-window policies `auth`, `ai`, `uploads` |
| Auditing | `IAuditWriter` → `AuditEvent` (append-only) |
| Observability | Structured logging; `/health/live` and `/health/ready` |
| Time | `IDateTimeProvider` (wraps `TimeProvider`) for testability |
| Reference ids | `IReferenceGenerator` for human-friendly references |
