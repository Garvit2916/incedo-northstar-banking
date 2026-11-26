# Northstar original-account migration utility

One-time, controlled migration of the four original local SQLite accounts and their
related application data into deployed PostgreSQL.

This tool deliberately does **not**:

- enable `Seed__Enabled` or change API startup behaviour
- add a public HTTP endpoint
- create or promote administrators interactively
- migrate refresh tokens (all affected users must sign in again)
- copy source role IDs (role mappings are resolved by role name in the target)
- delete or overwrite the conflicting AWS account; it can only be quarantined by renaming
- upload, copy, overwrite, delete, or otherwise modify S3 objects; S3 access is read-only
- print passwords, password hashes, tokens, secrets, or connection strings

## Scope

Default approved scope:

| Table | Rows migrated |
|---|---:|
| `AspNetUsers` | 5 |
| `AspNetUserRoles` | 5 |
| `CustomerProfiles` | 2 |
| `Products` | 3 |
| `Applications` | 48 |
| `ApplicationStatusHistory` | 80 |
| `KycDocuments` | 16 |
| `Tickets` | 12 |
| `TicketAssignments` | 6 |
| `TicketMessages` | 36 |
| `AiAssistanceRecords` | 52 |
| `Notifications` | 61 |
| `AuditEvents` | 336 |
| `RefreshTokens` | 0 |

Apply adds one migration audit event after the source rows, so the expected target
`AuditEvents` increase is 337.

The five users are the four original accounts plus `garvitofficialid@gmail.com`
(`E7C90EF7-BD2B-48AA-BC70-83FD06B834E4`), which is required by hard foreign keys
through `CustomerProfiles.UserId` and `Notifications.UserId`.

The six `agent2_*` users are excluded by default because they are referenced only by
soft user-ID columns. They can be included with both
`--include-linked-agent-users --confirm-linked-agent-users`.

## Prerequisites

- A full PostgreSQL backup or verified restore point. `--apply` requires `--confirm-backup`.
- Target migration `20261125102100_InitialPostgres` already applied.
- All four target roles present: `Administrator`, `Customer`, `Reviewer`, `SupportAgent`.
- A maintenance window with API writes frozen (for example, ECS service scaled to zero).
- A private S3 bucket name for read-only KYC blob verification; all 16 `StorageKey`
  objects must already exist and match the source metadata. The tool cannot create or fix them.
- The conflicting AWS user `2bbaf7f4-63a7-463c-9eab-784c689ef69a` must have no related
  rows before quarantine. The tool checks this read-only before writing.
- The local source SQLite database should be checkpointed or readable with its WAL. The
  connection is opened with `Mode=ReadOnly`.

AWS credentials are taken from the AWS default credential chain; never pass access keys
to this tool.

## Dry run

Dry run is the default. It reads the SQLite source read-only, inspects PostgreSQL
read-only, verifies S3 objects if `--s3-bucket` is supplied, and writes a JSON report.
S3 verification uses HEAD and temporary streamed GET requests only; no object is created,
replaced, deleted, or modified.

```powershell
dotnet run --project backend/tools/Northstar.DataMigration -- `
  --source "backend/src/Northstar.Api/northstar.db" `
  --target $env:NORTHSTAR_TARGET_CONNECTION `
  --s3-bucket my-private-kyc-bucket `
  --report .\northstar-migration-report.json
```

Review the report before continuing. `Status: dry-run` means no blockers;
`Status: dry-run-blocked` means apply must not proceed.

Connection strings can also be supplied through:

- `NORTHSTAR_SOURCE_CONNECTION` or `NORTHSTAR_SOURCE_DB`
- `NORTHSTAR_TARGET_CONNECTION`

## Applying

```powershell
dotnet run --project backend/tools/Northstar.DataMigration -- `
  --source "backend/src/Northstar.Api/northstar.db" `
  --target $env:NORTHSTAR_TARGET_CONNECTION `
  --s3-bucket my-private-kyc-bucket `
  --confirm-admin-conflict quarantine `
  --confirm-backup `
  --report .\northstar-migration-report.json `
  --apply
```

Apply behaviour:

- all target writes run in one PostgreSQL transaction
- the conflict account is quarantined first, only if preflight proved no related rows
- users, role mappings, and domain rows are inserted in FK-safe order
- original timestamps and concurrency versions are copied verbatim
- a migration audit event records the manifest hash and counts
- verification runs before commit; any failure rolls the transaction back

### KYC blob verification (read-only)

For each `KycDocuments.StorageKey`, the utility:

1. issues a HEAD request for existence and size
2. if present with the expected size, streams a temporary GET response through SHA-256
3. compares the digest with the source `KycDocuments.Sha256`

No S3 object is uploaded, copied, overwritten, deleted, or tagged. Missing objects, size
mismatches, and checksum mismatches are recorded in the JSON report as blockers and abort
both dry-run and apply before any database write. Resolve those objects through an
approved external process, then rerun the utility.

## Verification

```powershell
dotnet run --project backend/tools/Northstar.DataMigration -- `
  --target $env:NORTHSTAR_TARGET_CONNECTION `
  --report .\northstar-migration-report.json `
  --verify
```

Checks include exact IDs, expected counts, per-table content hashes (including original
timestamps, versions, identity fields, and password hashes as hash input only), role
mappings by target role ID, unchanged refresh tokens, unchanged unrelated-row ID
fingerprints, quarantine state, and the migration audit event. Reports contain only the
hashes, never the underlying secret values.

## Rollback

Prefer restoring the PostgreSQL backup. A compensating rollback is available for a failed
post-commit verification or an explicitly approved abort:

```powershell
dotnet run --project backend/tools/Northstar.DataMigration -- `
  --target $env:NORTHSTAR_TARGET_CONNECTION `
  --report .\northstar-migration-report.json `
  --confirm-rollback `
  --rollback
```

Before changing rows, it asserts that the report's migration audit event exists and matches
the manifest hash. After deletion, it verifies that migrated IDs and role rows are absent,
refresh tokens are unchanged, and the quarantined admin identity is restored exactly. The
transaction is rolled back if any postcondition fails. It does not delete S3 objects.

## Post-migration manual checks

- Sign in with each original account and confirm expected roles from `/api/auth/me`.
- Confirm the customer, reviewer, support agent, and administrator UI flows.
- Confirm KYC document pre-signed URLs resolve to the expected S3 objects.
- Confirm all users sign in again; refresh tokens were intentionally not migrated.

## Verification status of this implementation

Unit and in-memory SQLite integration tests cover closure, role mapping, conflict and
quarantine decisions, preflight blockers, apply/verify, rollback postconditions, content
tamper detection, read-only S3 verification with an in-memory fake, option validation,
manifest hashing, timestamp preservation, and insert order. No PostgreSQL, AWS, or S3
connection was made while building or testing the utility.
