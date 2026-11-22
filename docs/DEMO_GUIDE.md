# Demo guide

This walkthrough uses the seeded development data. All names, products,
balances, tickets, articles and decisions are fictitious.

## Start the demo

```powershell
# Terminal 1 — API
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5199"
dotnet run --project backend/src/Northstar.Api --no-launch-profile

# Terminal 2 — SPA
cd frontend
npm install
npm run dev
```

Open <http://localhost:5173>. All seeded accounts use the development-only
password `Northstar#Dev01`; the sign-in page exposes a picker in `npm run dev`.
Do not enable the demo picker in a publicly exposed build.

## 1. Public landing page

1. Visit `/` while signed out.
2. Review the four product cards, journey steps and safety messaging.
3. Select **Enter the workspace** to open `/login`.

`/` and `/login` are the only public pages. Every other route requires a token
and, where applicable, a specific role.

## 2. Customer journey

Sign in as `customer@northstar.example`.

1. On `/dashboard`, inspect the stat tiles and the **Account overview** card.
   The balance, transactions and active products are deterministic demo data;
   the account number and IFSC are masked with `••••`.
2. Open `/products` and compare `SAV-001`, `CC-001`, `PERS-001` and `FD-001`.
   Each product uses the same auditable application state model.
3. Start an application, complete the profile if prompted, then submit it.
4. Use **My applications** to open the detail view, upload a sample PDF, and
   follow the status history.
5. Check `/notifications` for updates created by the state transitions.
6. Open `/my-tickets` and submit a support request. The customer id is taken
   from your signed-in session, not from the form. Select the ticket to see its
   status and public conversation; agent internal notes are never shown here.
   You can optionally attach one of your own applications.

The demo cannot move money, create a real account, run a credit check or expose
a document URL outside the authenticated content endpoint.

## 3. Reviewer journey

Sign in as `reviewer@northstar.example` and open `/reviews`.

1. Pick a submitted application and start review.
2. Approve, reject or request information. Each choice writes an audit event and
   a status-history entry.
3. Return as the customer to see the decision and, where applicable, resubmit.

Decisions are always made by an authorised human through an explicit endpoint;
AI output never approves or rejects an application.

## 4. Support agent + grounded copilot

Sign in as `agent@northstar.example` and open `/support`.

1. Filter the queue by category, priority, status, sentiment or assignee. Use
   the pager; changing a filter resets it to page 1.
2. Select ticket **NS-TKT-1006** (*Card charge I do not recognise*) or
   **NS-TKT-1007** (*UPI payment failed but money was deducted*).
3. Add a public reply or an internal note. Reassign or change the status using
   the explicit controls.
4. Run each Copilot helper:
   - **Summary** and **Suggested reply** show cited article codes and match
     reasons.
   - **Sentiment** shows a label, confidence and signals, and persists on the
     ticket.
   - **Next best actions** show an assistive classification, each action's
     explanation, matched KB articles, confidence, expected outcome and a safe
     demo workflow.
5. Select **Draft this action** to copy an editable suggestion into the
   composer. Sensitive drafts use secure-upload and approved identity-verification
   language and never ask for a password, OTP, PIN or full card number.
6. Use **Review & resolve** or **Review & escalate** only after reading the
   confirmation dialog. The dialog explains that the status change is audited;
   nothing is submitted without pressing **Confirm**.

The recorded ticket category is never auto-changed by the assistive classifier.
If a suggestion disagrees with it, the agent decides whether to investigate.

## 5. Knowledge workspace

From the support navigation, open `/knowledge`.

1. Search by issue term (for example `unauthorized`, `UPI`, `KYC` or `minimum
   due`) and filter by category.
2. Open an article to read its resolution steps, customer explanation, agent
   guidance and escalation criteria.
3. Copy guidance into a reply only after reviewing it against the live ticket.

The page is read-only. Article codes shown here are the same codes cited by the
Copilot panel, so an agent can verify every recommendation.

## 6. Administration

Sign in as `admin@northstar.example` to manage user roles, activate/deactivate
products and inspect the audit trail. Products `SAV-001`, `CC-001`, `FD-001`
and `PERS-001` are active; `WM-001` is retained but deactivated.

## Troubleshooting

- **API not reachable** — confirm the API is on port 5199, or set
  `VITE_API_BASE_URL` in `frontend/.env` and restart Vite.
- **Stale seeded data** — stop the API and reset the local SQLite files as
  described in `README.md`. Do not delete a database that contains data you
  need.
- **AI provider** — the default `deterministic` provider works offline. A live
  Bedrock or OpenAI-compatible provider is optional and falls back to the
  deterministic implementation on failure; check `provenance.isLiveInference`
  before describing any output as live inference.
