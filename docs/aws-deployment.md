# AWS deployment (manual configuration; not executed here)

This repository contains **application code, container image definitions and an
ECR image-publishing workflow**. AWS infrastructure is configured manually in the
AWS Console; there is no infrastructure-as-code and no deployment automation in
this project.

No AWS resource was created, modified or deleted by this repository work. AWS
resources that were already created manually remain untouched, and the console is
the source of truth for their exact names, ARNs and configuration.

## Agreed scope and current status

| Concern | Local default | AWS target / repository support | Status |
| --- | --- | --- | --- |
| Database | SQLite (`Database:Provider=Sqlite`) | PostgreSQL (`Database:Provider=Postgres`) with a separate clean migration set; RDS configured manually | Application support implemented; no RDS resource is created or configured here |
| KYC documents | Local filesystem (`Storage:Provider=Local`) | Private S3 bucket, SSE-S3, short-lived read-only pre-signed URLs (`Storage:Provider=S3`) | Adapter implemented; bucket configured manually |
| External notification events | No-op publisher | SNS publisher selected when `Sns:TopicArn` is set | Adapter implemented; topic configured manually |
| Transactional email | No-op / SMTP / Azure Communication Services | Unchanged; SNS does not replace email | Preserved |
| AI | Deterministic provider | Existing Bedrock adapter remains optional | Preserved |
| Hosting | `dotnet run` / Docker Compose | One ECS Fargate task: nginx SPA sidecar + API on `127.0.0.1:8080` | Container preparation implemented; ECS configured manually |
| Images | Built locally | GitHub Actions builds and pushes to ECR using GitHub OIDC | Workflow present; not executed here |
| Logs | Console | JSON console logs for the `awslogs` driver / CloudWatch Logs | Application support implemented; log group configured manually |
| Configuration and secrets | `appsettings.json` + environment overrides | Standard .NET configuration keys; values supplied as secure task secrets/environment variables | Secret store decision pending; no secret-store SDK in the app |
| Networking / ingress | Local ports | Ingress decision pending evaluation | No ALB or alternative networking is created or configured in this repo |

The four application roles — `Customer`, `Reviewer`, `SupportAgent` and
`Administrator` — are unchanged. IAM grants infrastructure access only; it does
not replace controller- and service-level authorization.

### Resource status

**Created manually; do not recreate.** The agreed services are IAM, S3, SNS, ECR,
ECS Fargate and CloudWatch, with PostgreSQL on Amazon RDS as the database target.
The ECR repositories are `northstar-backend` and `northstar-frontend`. The AWS
Console is the source of truth for exact names, ARNs and settings; this
repository does not track them. Confirm the following before changing anything:

- IAM task and execution roles, least-privilege policies and the GitHub OIDC
  provider/role.
- Private S3 bucket for KYC documents, with public access blocked and
  server-side encryption enabled.
- SNS topic for coarse notification events.
- ECR repositories `northstar-backend` and `northstar-frontend`.
- ECS cluster, task definition and service.
- CloudWatch log group and retention settings.
- Persistent database, if one has already been created.

**Pending / not created by this repository.** No ALB, no RDS instance and no
final ingress, TLS or DNS configuration is claimed or created here. The
secret-store choice remains open.

### Still pending or intentionally not done here

- Final RDS engine, size, backup, encryption and network configuration.
- Final secret-store choice (Systems Manager Parameter Store or Secrets Manager)
  and the exact task-secret references.
- ALB / ingress / TLS / DNS decision. There is no ALB configuration in this
  repository and no alternative networking architecture has been introduced.
- Running the GitHub Actions workflow.
- Starting or updating the ECS service and testing the deployed application.

## Architecture

```text
Browser
   │
   ▼
Ingress — decision pending evaluation; configure manually
   │
   ▼
ECS Fargate task (awsvpc)
   ├── web : nginx :80  → proxies /api and /health to 127.0.0.1:8080
   └── api : ASP.NET Core :8080 (non-root)
          ├── PostgreSQL on Amazon RDS (configured manually; not created here)
          ├── private S3 bucket (SSE-S3, no public access, no delete grant)
          ├── SNS topic (coarse notification events only)
          └── CloudWatch Logs
```

The application does not require a specific ingress product. If an ALB is chosen,
configure it manually and use `/health/live` and `/health/ready` for target
checks. `Program.cs` trusts forwarded headers only from the loopback nginx
sidecar, and HSTS/HTTPS redirection are opt-in for environments where HTTPS is
guaranteed.

## GitHub Actions and ECR

`.github/workflows/publish-images.yml`:

- assumes the OIDC role using the repository secret `AWS_ROLE_ARN`;
- uses repository variables `AWS_REGION`, `ECR_BACKEND_REPOSITORY` and
  `ECR_FRONTEND_REPOSITORY` (with safe defaults for the last two);
- builds `backend/Dockerfile` and `frontend/Dockerfile` and pushes both images
  with the commit SHA and `:latest` tags;
- does **not** deploy to ECS or change any AWS service state.

Configure these GitHub repository settings (no account IDs, role ARNs or registry
URLs are stored in this repository):

| Setting | Type | Value |
| --- | --- | --- |
| `AWS_ROLE_ARN` | Secret | ARN of the existing GitHub OIDC role, read from the AWS Console |
| `AWS_REGION` | Variable | Region that contains the existing ECR repositories |
| `ECR_BACKEND_REPOSITORY` | Variable | `northstar-backend` |
| `ECR_FRONTEND_REPOSITORY` | Variable | `northstar-frontend` |

No long-lived AWS access keys are used or required.

Docker is not required on the developer machine. The workflow has not been run
as part of this work.

## Manual ECS task configuration

Use one Fargate task with two containers:

- `web`: the frontend nginx image. It serves the SPA and proxies `/api` and
  `/health` to `127.0.0.1:8080`. The image defaults `API_UPSTREAM` to
  `127.0.0.1:8080`; Docker Compose overrides it to `api:8080`.
- `api`: the backend image, running as a non-root user on port 8080.

Suggested role boundaries:

- **Execution role**: pull ECR images, write CloudWatch Logs and read only the
  approved task-secret values.
- **Task role**: `s3:GetObject`, `s3:PutObject` and `s3:ListBucket` on the KYC
  bucket, `sns:Publish` on the notification topic, and `bedrock:InvokeModel` only
  if live AI is enabled. Do not grant `s3:DeleteObject`; the application has no
  authorized deletion workflow.
- **CI role**: push to the two ECR repositories only. The current workflow has no
  ECS or IAM permissions and no deployment step.

The application uses the AWS default credential chain, so no access keys are
stored in source, images, task environment variables, logs or SNS messages.

## Runtime configuration

Set these as standard .NET environment variables in the ECS task definition.
Pass sensitive values through the task-secret mechanism of the chosen secret
store; the application reads them as normal configuration keys and needs no
Systems Manager or Secrets Manager package.

| Setting | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Database__Provider` | `Postgres` |
| `Database__ApplyMigrations` | `true` for a controlled migration run, then review before future releases |
| `Database__EnsureRoles` | `true` for a controlled role-only bootstrap, then `false` for normal operation |
| `ConnectionStrings__Default` | Secure task-secret value for the PostgreSQL connection string |
| `Storage__Provider` | `S3` |
| `Storage__S3BucketName` | Name of the manually created private bucket |
| `Storage__S3Region` | Bucket region, or leave empty to use the SDK default region |
| `Sns__TopicArn` | ARN of the manually created topic; leave empty to use the no-op publisher |
| `Sns__Region` | Topic region, or leave empty to use the SDK default region |
| `Jwt__SigningKey` | Secure task-secret value, at least 32 bytes |
| `Seed__Enabled` | `true` only for an explicitly approved demo bootstrap; otherwise `false` |
| `Seed__DefaultPassword` | Secure task-secret value when seeding is enabled |
| `Email__Provider` | `Noop` by default; configure SMTP/Azure Communication separately for real mail |
| `Ai__Provider` | `deterministic` unless live Bedrock is explicitly enabled |
| `Cors__Origins__0` | The approved public SPA origin; leave the ALB/domain decision explicit |
| `Security__EnableHsts` / `Security__RequireHttps` | `false` only while HTTPS is not guaranteed; enable both behind HTTPS |

`Database:ApplyMigrations`, `Database:EnsureRoles` and `Seed:Enabled` are
independent. Once the Identity schema exists, roles can be bootstrapped without
applying migrations or creating the well-known demo accounts; `EnsureRoles` only
creates missing roles through ASP.NET Core Identity `RoleManager`.
Seeding is disabled in the base configuration and is enabled only by the
Development overrides (or an explicit task setting for an approved demo
bootstrap). Keep it disabled for real data and manage users through a controlled
process.

PostgreSQL migrations live in
`backend/src/Northstar.Infrastructure.Persistence.PostgresMigrations`, which the
API project references. Do not point the `Postgres` provider at the SQLite
migrations in `Northstar.Infrastructure`.

### Secret-store decision

No application code depends on SSM Parameter Store or Secrets Manager. Select one
manually, create the secret values there, and reference them from the ECS task
definition using the execution role. Do not introduce a second secrets
abstraction in the application just to support that choice.

## S3 and SNS application integration

- `S3BlobStorage` is selected only when `Storage:Provider=S3`. It uploads with
  SSE-S3, exposes short-lived GET-only pre-signed URLs, and throws for deletion.
- Local development remains on `Storage:Provider=Local` with no AWS credentials.
- `SnsNotificationEventPublisher` is selected only when `Sns:TopicArn` is set.
  Its payload is limited to `eventType`, `entityType`, `entityId` (`null`) and
  `occurredAtUtc`. It contains no customer, KYC, account, email, name or document
  content.
- Email delivery remains a separate transactional outbox message and provider.
  SNS is an operational signal only.

### Why the separate outbox rows are retained

`NotificationService` writes `email.notification` and `notification.event` in the
same unit of work as the business change. `OutboxProcessor` dispatches each type
through its own port:

- an SNS failure cannot roll back or block an email message, and vice versa;
- retry uses bounded attempts with exponential back-off;
- idempotency keys are stored for each row;
- delivery is at-least-once, so subscribers should treat duplicate SNS events as
  possible. No additional deduplication service is introduced here;
- the SNS payload is deliberately sanitized, so retries and logs cannot expose
  customer data.

This separation is useful because it keeps external event delivery out of the
email path while preserving the existing transactional notification behaviour.
It has not changed application roles, authorization or business workflows.

## Manual deployment sequence

1. Confirm the manually created AWS resources and record their names/ARNs.
2. Configure GitHub OIDC and the repository secret/variables described above.
3. Run the image-publishing workflow and verify both ECR images.
4. Configure the ECS task definition with the two containers, roles, secrets and
   environment values above.
5. Configure ECS service networking, desired count and CloudWatch logging
   manually. Keep the task stoppable/restartable and start with one task.
6. Connect PostgreSQL, S3 and SNS with the configured values and least-privilege
   roles.
7. Decide and configure ingress/access. ALB remains pending evaluation; do not
   add an alternative networking architecture without discussion.
8. Test `/health/live`, `/health/ready`, login, the four role journeys and the
   document/notification flows against the deployed endpoint.

## Security notes and open decisions

- No AWS keys, JWT keys, database passwords or SNS payload data belong in source,
  frontend assets, Dockerfiles, workflow files, logs or documentation.
- S3 stays private and encrypted; no `s3:DeleteObject` grant is included.
- IAM is infrastructure access only; application roles remain the authorization
  boundary.
- Rate limits are per task and the outbox is not distributed. Keep one task or
  add an explicit shared mechanism before scaling out.
- The file scanner remains a development stub; malware scanning is out of scope.
- RDS final configuration, secret-store selection and ALB/ingress remain manual
  decisions. `docs/aws-production-roadmap.md` is a longer-term wish list, not a
  statement that these services are deployed.

## Cost considerations

No budget or alert is created by this repository. Review these manually:

- ECS Fargate is billed per task vCPU/memory while running; the documented
  pattern starts with one task running the nginx and API containers.
- RDS, if selected, is a recurring cost even when idle. Engine, size, Multi-AZ
  and backup retention should be chosen deliberately.
- S3 cost depends on stored KYC object size, requests and data transfer.
  Lifecycle and retention policies remain a manual decision.
- SNS is charged per publish and delivery. Volume stays low because only coarse
  notification metadata is published.
- CloudWatch Logs charges for ingestion and retention; set retention explicitly.
- ECR charges for stored image layers. The workflow tags each commit SHA and
  `latest`, so an image lifecycle policy is worth configuring manually.
- GitHub Actions hosted minutes and AWS data transfer may also incur charges.

## Verification status

Local build, test and configuration verification is recorded in
[testing.md](testing.md). No Docker image build, GitHub Actions run, ECS task
startup, RDS connection, S3/SNS call or live Bedrock inference was performed as
part of this repository work. No AWS resource was created or changed.
