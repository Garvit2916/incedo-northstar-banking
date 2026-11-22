# Solution approach

## Objective and constraints

Northstar is an independent assessment prototype. The brief emphasized a
professional banking UX, a grounded agent support workspace, deterministic
offline behaviour and safe, human-controlled actions. The existing Clean
Architecture modular monolith was retained rather than replaced: working
workflows, the ports-and-adapters design, the AI provider abstraction and the
deterministic fallback all remain in place.

## Design decisions

| Decision | Rationale |
| --- | --- |
| Extend the existing `NextBestActionEngine` | It is already the authoritative, deterministic source of recommendations. Adding KB grounding there keeps one explainable rule engine instead of introducing a competing service. |
| `IKnowledgeRetrievalService` with deterministic scoring | 26 demo articles are enough for transparent keyword/field ranking. Scores, article codes and match reasons can be shown to agents, and a vector retriever can implement the same port later. |
| Articles stored as delimited/JSON strings | Tags, keywords and resolution steps persist through both SQLite and SQL Server without provider-specific column types. |
| Additive DTO fields | `title`, `explanation`, `sources`, `suggestedWorkflow`, `confidence` and `classification` extend existing contracts so older callers and tests keep working. |
| Customer account overview as a derived view | No ledger entity, balance engine or real account integration was introduced. Values are deterministic per user, identifiers are masked and the UI labels the view as demo data. |
| One support workspace | The old ticket pages redirect to `/support`. The API owns assignment, status, replies, internal notes and audit history; React never persists workflow state locally. |
| Customer self-service tickets | A separate customer-only controller creates tickets from the authenticated user id and scopes list/detail reads by that id; the UI has no way to supply or spoof ownership. |
| Public `/` only | A dedicated landing page communicates the product, trust boundaries and sign-in path without exposing the development credentials. `/login` stays public; every other route is guarded. |

## Grounded assistance pipeline

1. `AiAssistanceService` loads the ticket with its conversation and builds a
   minimised context. Ticket text is treated as untrusted data, never as a
   prompt instruction; messages and field lengths are capped.
2. `IssueClassifier` extracts banking signals and proposes a category with a
   confidence and explanation. The original persisted category is never
   overwritten.
3. `IKnowledgeRetrievalService` scores active articles against the category,
   subject, description, product and conversation. Each hit carries a score and
   a human-readable match reason.
4. Deterministic rules in `NextBestActionEngine` combine ticket context,
   sentiment, application state, classification and KB hits. Each action has a
   title, reason, explanation, evidence, priority, expected outcome, typed
   action, optional confidence, safe workflow and article citations.
5. Summaries and suggested replies receive the same KB citations. All results
   carry provider provenance, a human-review disclaimer and a label that says
   whether output came from live inference or the local fallback.

## Safety boundaries

- AI output is advisory. There is no endpoint that executes an AI-proposed
  approval, status change, card block, refund or permission change.
- Status-changing actions require an explicit confirmation dialog. The request
  is made only after the agent presses **Confirm**, and the change is audited.
- Sensitive draft actions use safe language: secure uploads, approved identity
  verification, and no requests for passwords, OTPs, PINs, CVVs, full card
  numbers or document links by email.
- Knowledge and ticket text are data, not instructions. No retrieved article can
  add a tool call or change an action's authority.
- Customer APIs are scoped to the caller. Account identifiers are masked; KB
  endpoints are agent/admin-only; cross-tenant application and ticket reads
  return `404`. Customer ticket detail and counts exclude agent internal notes,
  and customer DTOs omit internal sentiment analytics.
- Related application context is injected into AI requests only for
  `Reviewer`/`Administrator` callers. Support agents receive ticket-only context,
  so AI cannot surface application references, statuses or document review states
  that the agent cannot view through normal APIs.

## AI providers

The default `deterministic` provider makes the full demo and CI suites work
offline. Optional `bedrock` and `openai-compatible` / `azure-openai` adapters
sit behind `IAiProvider` and are wrapped by `ResilientAiProvider`, which falls
back to deterministic output on timeout, transport failure or schema mismatch.
No credentials are stored in `appsettings.json`; Bedrock uses the AWS default
credential chain. Live inference was not verified in this environment, so any
live output must be validated separately before it is described as operational.

## Verification strategy

- Domain unit tests cover state-machine and entity invariants.
- Application tests use real EF Core on in-memory SQLite for use cases, KB
  retrieval/ranking, classification, grounded actions, account masking and AI
  provenance.
- API integration tests boot the real host over `WebApplicationFactory` for
  role checks, KB endpoints and masked account data.
- Vitest covers frontend policy mirrors, safe AI draft generation, confidence
  formatting and new endpoint wiring; `tsc --noEmit && vite build` checks the
  whole SPA.
- `scripts/smoke.ps1` exercises the running API end-to-end, including KB
  search/detail, masked account data, grounded citations, assistive
  classification and the absence of privileged AI routes.

Exact, most recent results are recorded in [`testing.md`](testing.md).
