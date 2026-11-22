import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { ticketsApi } from '../api/endpoints';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { SearchInput } from '../components/forms';
import { Icon } from '../components/icons';
import type { IconName } from '../components/icons';
import {
  Badge,
  EmptyState,
  ErrorBanner,
  InfoBanner,
  Spinner,
  StatusBadge,
} from '../components/ui';
import { useToast } from '../components/Toast';
import { confidencePercent, draftForAction } from '../lib/aiDrafts';
import { useAsync } from '../lib/useAsync';
import { formatDate, formatRelative, humanise } from '../lib/format';
import type {
  AiProvenance,
  AiSentimentResponse,
  AiSuggestedResponse,
  AiSummaryResponse,
  IssueClassification,
  KnowledgeReference,
  MessageAuthorType,
  NextBestAction,
  NextBestActionsResponse,
  SentimentLabel,
  TicketPriority,
  TicketStatus,
} from '../api/types';

const STATUS_OPTIONS: TicketStatus[] = [
  'New',
  'Open',
  'PendingCustomer',
  'PendingInternal',
  'Escalated',
  'Resolved',
  'Closed',
];

const PRIORITY_OPTIONS: TicketPriority[] = ['Low', 'Normal', 'High', 'Urgent'];

const SENTIMENT_OPTIONS: { value: SentimentLabel | ''; label: string }[] = [
  { value: '', label: 'All sentiment' },
  { value: 'Positive', label: 'Positive' },
  { value: 'Neutral', label: 'Neutral' },
  { value: 'Negative', label: 'Negative' },
  { value: 'UrgentEscalation', label: 'Urgent / escalation' },
];

type AiFeature = 'summary' | 'suggestion' | 'sentiment' | 'actions';

const AI_ACTIONS: { key: AiFeature; label: string; hint: string; icon: IconName; busyLabel: string }[] = [
  {
    key: 'summary',
    label: 'Summarise ticket',
    hint: 'Structured issue, context and status',
    icon: 'list',
    busyLabel: 'Summarising…',
  },
  {
    key: 'suggestion',
    label: 'Suggest response',
    hint: 'Editable, grounded customer reply',
    icon: 'sparkles',
    busyLabel: 'Drafting…',
  },
  {
    key: 'actions',
    label: 'Next best actions',
    hint: 'Ranked recommendations with outcomes',
    icon: 'trendingUp',
    busyLabel: 'Prioritising…',
  },
];

const SENTIMENT_TONE: Record<SentimentLabel, 'success' | 'neutral' | 'warning' | 'danger'> = {
  Positive: 'success',
  Neutral: 'neutral',
  Negative: 'warning',
  UrgentEscalation: 'danger',
};

function messageClass(authorType: MessageAuthorType, isInternalNote: boolean): string {
  if (isInternalNote) return 'message message--internal';
  if (authorType === 'Customer') return 'message message--customer';
  if (authorType === 'Agent') return 'message message--agent';
  return 'message message--system';
}

function authorLabel(authorType: MessageAuthorType): string {
  if (authorType === 'Customer') return 'Customer';
  if (authorType === 'Agent') return 'Support agent';
  return 'System';
}

export function SupportWorkspacePage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const { user, hasRole } = useAuth();
  const toast = useToast();

  const [status, setStatus] = useState<TicketStatus | ''>('');
  const [priority, setPriority] = useState<TicketPriority | ''>('');
  const [sentimentFilter, setSentimentFilter] = useState<SentimentLabel | ''>('');
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(1);
    }, 300);
    return () => window.clearTimeout(timeout);
  }, [searchInput]);

  const overview = useAsync(() => ticketsApi.overview(), []);
  const list = useAsync(
    () =>
      ticketsApi.list({
        status: status || undefined,
        priority: priority || undefined,
        search: search || undefined,
        page,
        pageSize: 50,
      }),
    [status, priority, search, page],
  );
  const detail = useAsync(
    () => (id ? ticketsApi.get(id) : Promise.resolve(null)),
    [id],
  );

  const [message, setMessage] = useState('');
  const [internalNote, setInternalNote] = useState(false);
  const [statusValue, setStatusValue] = useState<TicketStatus | ''>('');
  const [busy, setBusy] = useState(false);

  const [summary, setSummary] = useState<AiSummaryResponse | null>(null);
  const [suggestion, setSuggestion] = useState<AiSuggestedResponse | null>(null);
  const [draft, setDraft] = useState('');
  const [sentiment, setSentiment] = useState<AiSentimentResponse | null>(null);
  const [actions, setActions] = useState<NextBestActionsResponse | null>(null);
  const [aiBusy, setAiBusy] = useState<AiFeature | null>(null);
  const [autoAnalyzedFor, setAutoAnalyzedFor] = useState<string | null>(null);
  const [pendingAction, setPendingAction] = useState<NextBestAction | null>(null);

  useEffect(() => {
    setSummary(null);
    setSuggestion(null);
    setDraft('');
    setSentiment(null);
    setActions(null);
    setPendingAction(null);
    setAutoAnalyzedFor(null);
    setStatusValue('');
  }, [id]);

  useEffect(() => {
    if (!id && list.data && list.data.items.length > 0) {
      navigate(`/support/${list.data.items[0].id}`, { replace: true });
    }
  }, [id, list.data, navigate]);

  useEffect(() => {
    if (!detail.data || !id || detail.data.latestSentiment || autoAnalyzedFor === id) return;
    setAutoAnalyzedFor(id);
    ticketsApi.ai
      .analyzeSentiment(id)
      .then((result) => {
        setSentiment(result);
        detail.reload();
        overview.reload();
      })
      .catch(() => {
        // Sentiment is advisory; a failed automatic analysis should not block triage.
      });
  }, [detail.data, detail.reload, id, autoAnalyzedFor, overview.reload]);

  const items = (list.data?.items ?? []).filter(
    (ticket) => !sentimentFilter || ticket.latestSentiment === sentimentFilter,
  );
  const detailTicket = detail.data;
  const currentStatus = statusValue || detailTicket?.status || 'New';
  const isAssignedToMe = Boolean(user && detailTicket?.assignedAgentId === user.id);
  const sentimentConfidence = confidencePercent(sentiment?.confidence);

  function refreshAll() {
    overview.reload();
    list.reload();
    detail.reload();
  }

  async function runAi(feature: AiFeature, action: () => Promise<void>) {
    setAiBusy(feature);
    try {
      await action();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'AI request failed.');
    } finally {
      setAiBusy(null);
    }
  }

  async function handleSend() {
    if (!detailTicket) return;
    const body = message.trim();
    if (!body) return;
    setBusy(true);
    try {
      await ticketsApi.addMessage(detailTicket.id, body, internalNote);
      setMessage('');
      setInternalNote(false);
      toast.success(internalNote ? 'Internal note added.' : 'Reply sent.');
      detail.reload();
      list.reload();
      overview.reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to send message.');
    } finally {
      setBusy(false);
    }
  }

  async function handleAssign() {
    if (!detailTicket || !user) return;
    setBusy(true);
    try {
      await ticketsApi.assign(detailTicket.id, user.id);
      toast.success('Ticket assigned to you.');
      refreshAll();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to assign ticket.');
    } finally {
      setBusy(false);
    }
  }

  async function handleStatusChange(nextStatus: TicketStatus) {
    if (!detailTicket) return;
    setBusy(true);
    try {
      await ticketsApi.changeStatus(detailTicket.id, nextStatus);
      setStatusValue('');
      toast.success('Status updated.');
      refreshAll();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to update status.');
    } finally {
      setBusy(false);
    }
  }

  function insertDraft(text: string, note: boolean, source: string) {
    setMessage(text);
    setInternalNote(note);
    toast.info(`${source} inserted into the composer.`);
  }

  async function copyText(text: string) {
    try {
      await navigator.clipboard.writeText(text);
      toast.success('Copied to clipboard.');
    } catch {
      toast.error('Unable to copy to the clipboard.');
    }
  }

  function applyAction(action: NextBestAction) {
    if (action.actionType === 'resolve' || action.actionType === 'escalate') {
      setPendingAction(action);
      return;
    }
    insertDraft(
      draftForAction(action),
      action.actionType === 'review-application',
      action.actionType === 'review-application' ? 'Internal note' : 'Suggested reply',
    );
  }

  async function confirmPendingAction() {
    if (!pendingAction) return;
    setBusy(true);
    try {
      await ticketsApi.changeStatus(
        id,
        pendingAction.actionType === 'resolve' ? 'Resolved' : 'Escalated',
      );
      toast.success(pendingAction.actionType === 'resolve' ? 'Ticket resolved.' : 'Ticket escalated.');
      setPendingAction(null);
      refreshAll();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to update status.');
    } finally {
      setBusy(false);
    }
  }

  function resetFilters() {
    setStatus('');
    setPriority('');
    setSentimentFilter('');
    setSearchInput('');
    setPage(1);
  }

  return (
    <div className="page support-page">
      <header className="support-hero">
        <div>
          <p className="eyebrow">Operations · AI assisted</p>
          <h1 className="page-title">Support workspace</h1>
          <p className="page-description">
            Triage every conversation with grounded summaries, editable replies, sentiment signals and
            next best actions. AI recommends — you decide.
          </p>
        </div>
        <div className="support-hero-actions">
          <span className="badge badge--neutral">
            <Icon name="inbox" size={13} />
            {list.data ? `${list.data.totalCount} ticket${list.data.totalCount === 1 ? '' : 's'}` : 'Loading…'}
          </span>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={refreshAll}
            disabled={list.loading || detail.loading}
          >
            <Icon name="refresh" size={15} />
            Refresh
          </button>
        </div>
      </header>

      <section className="support-kpis" aria-label="Support overview">
        <Kpi label="Open tickets" value={overview.data?.openTickets} icon="inbox" tone="primary" />
        <Kpi label="Unassigned" value={overview.data?.unassignedTickets} icon="userPlus" tone="accent" />
        <Kpi label="Urgent" value={overview.data?.urgentTickets} icon="alertTriangle" tone="danger" />
        <Kpi
          label="Negative sentiment"
          value={overview.data?.negativeSentiment}
          icon="heart"
          tone="warning"
        />
        <Kpi
          label="Awaiting customer"
          value={overview.data?.awaitingCustomer}
          icon="clock"
          tone="primary"
        />
        <Kpi label="Resolved today" value={overview.data?.resolvedToday} icon="checkCircle" tone="success" />
      </section>

      {overview.error && <ErrorBanner message={overview.error} onRetry={overview.reload} />}

      <div className="support-shell">
        <aside className="support-inbox" aria-label="Ticket inbox">
          <div className="support-inbox-head">
            <div className="support-inbox-title">
              <h2>Inbox</h2>
              <span className="muted text-xs">{items.length} shown</span>
            </div>
            <SearchInput
              id="workspace-search"
              label="Search tickets"
              placeholder="Search reference, subject or customer"
              value={searchInput}
              onChange={setSearchInput}
            />
          </div>

          <div className="support-filters">
            <label className="field field-inline">
              <span>Status</span>
              <select
                value={status}
                onChange={(event) => {
                  setStatus(event.target.value as TicketStatus | '');
                  setPage(1);
                }}
              >
                <option value="">All statuses</option>
                {STATUS_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {humanise(option)}
                  </option>
                ))}
              </select>
            </label>
            <label className="field field-inline">
              <span>Priority</span>
              <select
                value={priority}
                onChange={(event) => {
                  setPriority(event.target.value as TicketPriority | '');
                  setPage(1);
                }}
              >
                <option value="">All priorities</option>
                {PRIORITY_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </label>
            <label className="field field-inline">
              <span>Sentiment</span>
              <select
                value={sentimentFilter}
                onChange={(event) => setSentimentFilter(event.target.value as SentimentLabel | '')}
              >
                {SENTIMENT_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
            {(status || priority || sentimentFilter || search) && (
              <button type="button" className="link-button text-xs" onClick={resetFilters}>
                Clear filters
              </button>
            )}
          </div>

          {list.loading ? (
            <div className="support-list-state">
              <Spinner label="Loading tickets…" />
            </div>
          ) : list.error ? (
            <div className="support-list-state">
              <ErrorBanner message={list.error} onRetry={list.reload} />
            </div>
          ) : items.length === 0 ? (
            <EmptyState icon="inbox" message="No tickets match these filters." />
          ) : (
            <ul className="support-ticket-list">
              {items.map((ticket) => (
                <li key={ticket.id}>
                  <button
                    type="button"
                    className={ticket.id === id ? 'support-ticket active' : 'support-ticket'}
                    onClick={() => navigate(`/support/${ticket.id}`)}
                    aria-current={ticket.id === id ? 'true' : undefined}
                  >
                    <span className="support-ticket-top">
                      <span className="support-ticket-ref">{ticket.reference}</span>
                      <span className="muted text-2xs">{formatRelative(ticket.updatedAtUtc)}</span>
                    </span>
                    <span className="support-ticket-subject">{ticket.subject}</span>
                    <span className="support-ticket-meta">
                      <StatusBadge value={ticket.status} />
                      <Badge value={ticket.priority} dot={false} />
                      {ticket.latestSentiment && (
                        <Badge
                          tone={SENTIMENT_TONE[ticket.latestSentiment]}
                          dot={false}
                          icon="heart"
                        >
                          {humanise(ticket.latestSentiment)}
                        </Badge>
                      )}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}

          {list.data && list.data.totalCount > 50 && (
            <div className="support-pager">
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                disabled={page <= 1}
                onClick={() => setPage((value) => Math.max(1, value - 1))}
              >
                <Icon name="chevronLeft" size={14} />
                Previous
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                disabled={page >= (list.data?.totalPages ?? 1)}
                onClick={() => setPage((value) => value + 1)}
              >
                Next
                <Icon name="chevronRight" size={14} />
              </button>
            </div>
          )}
        </aside>

        <section className="support-conversation" aria-label="Selected conversation">
          {!id ? (
            <div className="support-placeholder">
              <EmptyState icon="support" message="Select a ticket from the inbox to start triaging." />
            </div>
          ) : detail.loading ? (
            <div className="support-placeholder">
              <Spinner label="Loading conversation…" />
            </div>
          ) : detail.error || !detailTicket ? (
            <div className="support-placeholder">
              <ErrorBanner message={detail.error ?? 'Ticket not found.'} onRetry={detail.reload} />
            </div>
          ) : (
            <>
              <header className="support-ticket-head">
                <div className="support-ticket-heading">
                  <div className="support-ticket-title">
                    <h2>{detailTicket.subject}</h2>
                    <StatusBadge value={detailTicket.status} />
                  </div>
                  <p className="muted text-sm">
                    {detailTicket.reference} · {humanise(detailTicket.category)} · Opened{' '}
                    {formatDate(detailTicket.createdAtUtc)}
                  </p>
                  <div className="support-ticket-head-facts">
                    <Badge value={detailTicket.priority} dot={false} />
                    {detailTicket.latestSentiment && (
                      <Badge tone={SENTIMENT_TONE[detailTicket.latestSentiment]} dot={false} icon="heart">
                        {humanise(detailTicket.latestSentiment)}
                      </Badge>
                    )}
                    {detailTicket.slaDueAtUtc && (
                      <Badge tone="neutral" dot={false} icon="clock">
                        SLA {formatRelative(detailTicket.slaDueAtUtc)}
                      </Badge>
                    )}
                    <span className="muted text-xs">
                      {isAssignedToMe ? 'Assigned to you' : detailTicket.assignedAgentId ? 'Assigned to another agent' : 'Unassigned'}
                    </span>
                  </div>
                </div>
                <div className="support-ticket-actions">
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    disabled={busy || isAssignedToMe || !user}
                    onClick={() => void handleAssign()}
                  >
                    <Icon name="userPlus" size={15} />
                    {isAssignedToMe ? 'Assigned' : 'Assign to me'}
                  </button>
                  {hasRole('Administrator') && detailTicket.relatedApplicationId && (
                    <button
                      type="button"
                      className="btn btn-ghost btn-sm"
                      onClick={() => navigate(`/reviews/${detailTicket.relatedApplicationId}`)}
                    >
                      Application
                      <Icon name="arrowUpRight" size={14} />
                    </button>
                  )}
                </div>
              </header>

              <div className="support-conversation-body">
                {detailTicket.messages.length === 0 ? (
                  <EmptyState icon="message" message="No messages yet." />
                ) : (
                  <div className="conversation support-thread">
                    {detailTicket.messages.map((item) => (
                      <div key={item.id} className={messageClass(item.authorType, item.isInternalNote)}>
                        <span className="message-avatar" aria-hidden="true">
                          <Icon
                            name={
                              item.isInternalNote
                                ? 'lock'
                                : item.authorType === 'Customer'
                                  ? 'profile'
                                  : 'support'
                            }
                            size={15}
                          />
                        </span>
                        <div className="message-bubble">
                          <div className="message-head">
                            <span className="message-author">
                              {item.isInternalNote ? 'Internal note' : authorLabel(item.authorType)}
                            </span>
                            <span className="message-time">{formatDate(item.createdAtUtc)}</span>
                            {item.isInternalNote && <span className="badge badge--warning">Internal</span>}
                          </div>
                          <div className="message-body">{item.body}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <div className="composer support-composer">
                <label className="sr-only" htmlFor="workspace-reply">
                  Reply
                </label>
                <textarea
                  id="workspace-reply"
                  rows={3}
                  placeholder="Write a reply or internal note. Press Ctrl + Enter to send."
                  value={message}
                  onChange={(event) => setMessage(event.target.value)}
                  onKeyDown={(event) => {
                    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
                      event.preventDefault();
                      void handleSend();
                    }
                  }}
                />
                <div className="composer-actions">
                  <label className="checkbox">
                    <input
                      type="checkbox"
                      checked={internalNote}
                      onChange={(event) => setInternalNote(event.target.checked)}
                    />
                    Internal note
                  </label>
                  <div className="support-composer-actions">
                    <label className="field field-inline support-status-select">
                      <span>Status</span>
                      <select
                        value={currentStatus}
                        onChange={(event) => setStatusValue(event.target.value as TicketStatus)}
                      >
                        {STATUS_OPTIONS.map((option) => (
                          <option key={option} value={option}>
                            {humanise(option)}
                          </option>
                        ))}
                      </select>
                    </label>
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      disabled={busy || currentStatus === detailTicket.status}
                      onClick={() => void handleStatusChange(currentStatus)}
                    >
                      Update
                    </button>
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={busy || message.trim().length === 0}
                      onClick={() => void handleSend()}
                    >
                      <Icon name={internalNote ? 'lock' : 'send'} size={15} />
                      {internalNote ? 'Add note' : 'Send reply'}
                    </button>
                  </div>
                </div>
                <p className="support-sensitive-note">
                  <Icon name="shieldCheck" size={13} />
                  Never action account changes from an AI suggestion without verifying identity.
                </p>
              </div>
            </>
          )}
        </section>

        <aside className="support-ai" aria-label="AI assistance">
          <div className="ai-card support-ai-card">
            <div className="ai-card-head">
              <span className="ai-glyph" aria-hidden="true">
                <Icon name="sparkles" size={18} />
              </span>
              <div>
                <h3>Northstar Copilot</h3>
                <p>Grounded on this ticket · advisory only</p>
              </div>
            </div>

            {!detailTicket ? (
              <div className="ai-empty">
                <Icon name="sparkles" size={18} />
                Select a ticket to generate a summary, reply, sentiment and next best actions.
              </div>
            ) : (
              <>
                <div className="ai-actions support-ai-actions">
                  {AI_ACTIONS.map((feature) => (
                    <button
                      key={feature.key}
                      type="button"
                      className="ai-action"
                      disabled={aiBusy !== null}
                      onClick={() =>
                        void runAi(feature.key, async () => {
                          if (feature.key === 'summary') setSummary(await ticketsApi.ai.summarize(id));
                          if (feature.key === 'suggestion') {
                            const result = await ticketsApi.ai.suggestResponse(id);
                            setSuggestion(result);
                            setDraft(result.body);
                          }
                          if (feature.key === 'actions') setActions(await ticketsApi.ai.nextBestActions(id));
                        })
                      }
                    >
                      <span className="ai-action-top">
                        <Icon name={aiBusy === feature.key ? 'loader' : feature.icon} size={16} />
                        {feature.label}
                      </span>
                      <span className="muted text-xs">
                        {aiBusy === feature.key ? feature.busyLabel : feature.hint}
                      </span>
                    </button>
                  ))}
                </div>

                <div className="ai-output support-ai-output" aria-live="polite">
                  {sentiment && !summary && !suggestion && !actions && (
                    <div className="ai-result">
                      <div className="ai-result-head">
                        <h4>Sentiment</h4>
                        <Provenance provenance={sentiment.provenance} />
                      </div>
                      <div className="ai-result-body">
                        <SentimentResult sentiment={sentiment} confidence={sentimentConfidence} />
                      </div>
                    </div>
                  )}

                  {!sentiment && detailTicket.latestSentiment && (
                    <div className="ai-result">
                      <div className="ai-result-head">
                        <h4>Stored sentiment</h4>
                        <Badge tone={SENTIMENT_TONE[detailTicket.latestSentiment]} dot={false} icon="heart">
                          {humanise(detailTicket.latestSentiment)}
                        </Badge>
                      </div>
                      <div className="ai-result-body">
                        <p className="muted text-xs">
                          Last analysed{' '}
                          {detailTicket.sentimentAnalyzedAtUtc
                            ? formatRelative(detailTicket.sentimentAnalyzedAtUtc)
                            : 'earlier'}
                          . Re-run sentiment after new customer messages.
                        </p>
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          disabled={aiBusy !== null}
                          onClick={() =>
                            void runAi('sentiment', async () => {
                              setSentiment(await ticketsApi.ai.analyzeSentiment(id));
                              detail.reload();
                              overview.reload();
                            })
                          }
                        >
                          <Icon name="refresh" size={14} />
                          Re-analyse sentiment
                        </button>
                      </div>
                    </div>
                  )}

                  {summary && (
                    <div className="ai-result">
                      <div className="ai-result-head">
                        <h4>Structured summary</h4>
                        <Provenance provenance={summary.provenance} />
                      </div>
                      <div className="ai-result-body">
                        <AiKv title="Issue overview" text={summary.mainIssue} />
                        <AiList title="Customer context" items={summary.keyContext} />
                        <AiList title="Actions taken" items={summary.actionsTaken} />
                        <AiKv title="Current status" text={summary.currentState} />
                        <AiList title="Pending resolution" items={summary.outstandingItems} />
                        <AiList title="Uncertainties" items={summary.uncertainties} />
                        <KnowledgeSources sources={summary.sources ?? []} />
                        <div className="support-ai-inline-actions">
                          <button
                            type="button"
                            className="link-button text-xs"
                            onClick={() => void copyText(summaryText(summary))}
                          >
                            <Icon name="copy" size={13} />
                            Copy summary
                          </button>
                          <button
                            type="button"
                            className="link-button text-xs"
                            disabled={aiBusy !== null}
                            onClick={() =>
                              void runAi('summary', async () => {
                                setSummary(await ticketsApi.ai.summarize(id));
                              })
                            }
                          >
                            <Icon name="refresh" size={13} />
                            Regenerate
                          </button>
                        </div>
                      </div>
                    </div>
                  )}

                  {suggestion && (
                    <div className="ai-result">
                      <div className="ai-result-head">
                        <h4>Suggested response</h4>
                        <Provenance provenance={suggestion.provenance} />
                      </div>
                      <div className="ai-result-body">
                        <p className="muted text-xs">Subject: {suggestion.subject}</p>
                        <div className="ai-suggestion">
                          <label className="sr-only" htmlFor="ai-draft">
                            Suggested response draft
                          </label>
                          <textarea
                            id="ai-draft"
                            value={draft}
                            onChange={(event) => setDraft(event.target.value)}
                          />
                          <div className="support-ai-inline-actions">
                            <button
                              type="button"
                              className="link-button text-xs"
                              onClick={() => void copyText(draft)}
                            >
                              <Icon name="copy" size={13} />
                              Copy
                            </button>
                            <button
                              type="button"
                              className="link-button text-xs"
                              disabled={aiBusy !== null}
                              onClick={() =>
                                void runAi('suggestion', async () => {
                                  const result = await ticketsApi.ai.suggestResponse(id);
                                  setSuggestion(result);
                                  setDraft(result.body);
                                })
                              }
                            >
                              <Icon name="refresh" size={13} />
                              Regenerate
                            </button>
                          </div>
                          <div className="support-ai-inline-actions">
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              onClick={() => insertDraft(draft, false, 'Suggested reply')}
                            >
                              <Icon name="mail" size={14} />
                              Insert into composer
                            </button>
                            <button
                              type="button"
                              className="btn btn-ghost btn-sm"
                              onClick={() => insertDraft(draft, true, 'Internal note')}
                            >
                              <Icon name="lock" size={14} />
                              Use as note
                            </button>
                          </div>
                        </div>
                        {suggestion.notes.length > 0 && <AiList title="Notes" items={suggestion.notes} />}
                        <KnowledgeSources sources={suggestion.sources ?? []} />
                      </div>
                    </div>
                  )}

                  {actions && (
                    <div className="ai-result">
                      <div className="ai-result-head">
                        <h4>Next best actions</h4>
                        <Provenance provenance={actions.provenance} />
                      </div>
                      <div className="ai-result-body">
                        {actions.classification && (
                          <ClassificationPanel classification={actions.classification} />
                        )}
                        <div className="support-ai-kb-link">
                          <Icon name="file" size={13} />
                          Recommendations cite approved demo articles.{' '}
                          <Link to="/knowledge">Open knowledge base</Link>
                        </div>
                        <ol className="nba-list">
                          {actions.actions.map((action, index) => (
                            <li className="nba-item support-nba-item" key={`${action.title}-${index}`}>
                              <span className="nba-rank">{index + 1}</span>
                              <div className="support-nba-body">
                                <div className="nba-head">
                                  <strong>{action.title || action.action}</strong>
                                  <StatusBadge value={action.priority} dot={false} />
                                  {action.requiresHumanApproval && (
                                    <span className="badge badge--warning">Needs approval</span>
                                  )}
                                  {action.confidence != null && (
                                    <span className="badge badge--neutral">
                                      {confidencePercent(action.confidence)}% match
                                    </span>
                                  )}
                                </div>
                                {action.explanation && action.explanation !== action.reason && (
                                  <p className="support-nba-explanation">{action.explanation}</p>
                                )}
                                <p>{action.reason}</p>
                                <p className="nba-evidence">
                                  <Icon name="info" size={12} /> Evidence: {action.evidence}
                                </p>
                                {action.suggestedWorkflow && (
                                  <p className="support-nba-workflow">
                                    <Icon name="shieldCheck" size={12} /> Safe demo workflow:{' '}
                                    <code>{action.suggestedWorkflow}</code> — human confirmation required
                                  </p>
                                )}
                                <p className="support-nba-outcome">
                                  <strong>Expected outcome</strong> {action.expectedOutcome}
                                </p>
                                <KnowledgeSources sources={action.sources ?? []} />
                                <button
                                  type="button"
                                  className="btn btn-secondary btn-sm"
                                  disabled={busy}
                                  onClick={() => applyAction(action)}
                                >
                                  <Icon
                                    name={
                                      action.actionType === 'resolve'
                                        ? 'checkCircle'
                                        : action.actionType === 'escalate'
                                          ? 'alertTriangle'
                                          : 'arrowRight'
                                    }
                                    size={14}
                                  />
                                  {action.actionType === 'resolve'
                                    ? 'Review & resolve'
                                    : action.actionType === 'escalate'
                                      ? 'Review & escalate'
                                      : 'Draft this action'}
                                </button>
                              </div>
                            </li>
                          ))}
                        </ol>
                      </div>
                    </div>
                  )}

                  {sentiment && (summary || suggestion || actions) && (
                    <div className="ai-result">
                      <div className="ai-result-head">
                        <h4>Sentiment</h4>
                        <Provenance provenance={sentiment.provenance} />
                      </div>
                      <div className="ai-result-body">
                        <SentimentResult sentiment={sentiment} confidence={sentimentConfidence} />
                      </div>
                    </div>
                  )}

                  {!summary && !suggestion && !actions && !sentiment && !detailTicket.latestSentiment && (
                    <div className="ai-empty">
                      <Icon name="sparkles" size={18} />
                      Generating sentiment guidance. Run a summary or suggested response to begin.
                    </div>
                  )}

                  <div className="ai-disclaimer">
                    <Icon name="shieldCheck" size={14} />
                    <span>
                      {summary?.provenance.disclaimer ??
                        'AI output is advisory and must be reviewed by a support agent before it is sent or acted on.'}
                    </span>
                  </div>
                </div>
              </>
            )}
          </div>
        </aside>
      </div>

      <ConfirmDialog
        open={pendingAction !== null}
        title={
          pendingAction?.actionType === 'resolve' ? 'Resolve this ticket?' : 'Escalate this ticket?'
        }
        message={
          pendingAction
            ? `${pendingAction.action}\n\nExpected outcome: ${pendingAction.expectedOutcome}\n\nThis status change is recorded in the audit trail.`
            : ''
        }
        confirmLabel={pendingAction?.actionType === 'resolve' ? 'Resolve ticket' : 'Escalate ticket'}
        destructive={pendingAction?.actionType === 'escalate'}
        busy={busy}
        onConfirm={() => void confirmPendingAction()}
        onCancel={() => setPendingAction(null)}
      />
    </div>
  );
}

function Kpi({
  label,
  value,
  icon,
  tone,
}: {
  label: string;
  value: number | undefined;
  icon: IconName;
  tone: 'primary' | 'success' | 'warning' | 'danger' | 'accent';
}) {
  return (
    <div className={`support-kpi support-kpi--${tone}`}>
      <span className={`stat-icon stat-icon--${tone}`} aria-hidden="true">
        <Icon name={icon} size={16} />
      </span>
      <div>
        <span className="support-kpi-label">{label}</span>
        <strong className="support-kpi-value">{value ?? '—'}</strong>
      </div>
    </div>
  );
}

function AiKv({ title, text }: { title: string; text: string }) {
  return (
    <div className="ai-kv">
      <h5>{title}</h5>
      <p>{text}</p>
    </div>
  );
}

function AiList({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) return null;
  return (
    <div className="ai-kv">
      <h5>{title}</h5>
      <ul>
        {items.map((item, index) => (
          <li key={index}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

function SentimentResult({
  sentiment,
  confidence,
}: {
  sentiment: AiSentimentResponse;
  confidence: number | null;
}) {
  return (
    <div className="sentiment-meter">
      <div className="sentiment-head">
        <span className="sentiment-label">{humanise(sentiment.label)}</span>
        {confidence !== null && <span className="muted text-xs">{confidence}% confidence</span>}
      </div>
      {confidence !== null && (
        <div className="confidence">
          <div className="confidence-track">
            <div
              className="confidence-bar"
              style={{ width: `${Math.min(100, Math.max(0, confidence))}%` }}
            />
          </div>
        </div>
      )}
      <p>{sentiment.explanation}</p>
      {sentiment.frustrationIndicators.length > 0 && (
        <div className="ai-kv">
          <h5>Frustration indicators</h5>
          <div className="chip-row">
            {sentiment.frustrationIndicators.map((signal, index) => (
              <span className="chip" key={index}>
                {signal}
              </span>
            ))}
          </div>
        </div>
      )}
      {sentiment.urgencyIndicators.length > 0 && (
        <div className="ai-kv">
          <h5>Urgency indicators</h5>
          <div className="chip-row">
            {sentiment.urgencyIndicators.map((signal, index) => (
              <span className="chip" key={index}>
                {signal}
              </span>
            ))}
          </div>
        </div>
      )}
      {sentiment.signals.length > 0 && (
        <div className="ai-kv">
          <h5>Signals</h5>
          <div className="chip-row">
            {sentiment.signals.map((signal, index) => (
              <span className="chip" key={index}>
                {signal}
              </span>
            ))}
          </div>
        </div>
      )}
      {sentiment.escalationRecommended && (
        <InfoBanner
          tone="warning"
          title="Escalation recommended"
          message="The customer appears frustrated or the issue is high risk. Consider escalating this ticket."
        />
      )}
    </div>
  );
}

function KnowledgeSources({ sources }: { sources: KnowledgeReference[] }) {
  if (sources.length === 0) {
    return null;
  }

  return (
    <div className="ai-kb-sources">
      <h5>
        <Icon name="file" size={13} /> Approved knowledge
      </h5>
      <ul>
        {sources.map((source) => (
          <li key={source.id}>
            <span className="kb-ref-code">{source.articleCode}</span>
            <span className="kb-ref-title">{source.title}</span>
            <span className="kb-ref-score" title={source.matchReason}>
              {source.score.toFixed(1)} · {source.matchReason}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function ClassificationPanel({ classification }: { classification: IssueClassification }) {
  const percent = confidencePercent(classification.confidence) ?? 0;
  return (
    <div className="ai-classification">
      <div className="ai-classification-head">
        <Badge tone="accent" icon="sparkles">
          Assistive classification
        </Badge>
        <strong>{humanise(classification.issueType)}</strong>
      </div>
      <p className="muted text-xs">
        Suggested category: {humanise(classification.suggestedCategory)} · {percent}% confidence
      </p>
      <p className="text-xs">{classification.explanation}</p>
      {classification.signals.length > 0 && (
        <div className="chip-row">
          {classification.signals.map((signal) => (
            <span className="chip" key={signal}>
              {signal}
            </span>
          ))}
        </div>
      )}
      <p className="muted text-2xs">The recorded ticket category is not changed by this suggestion.</p>
    </div>
  );
}

function Provenance({ provenance }: { provenance: AiProvenance }) {
  return (
    <span className="badge badge--neutral" title={provenance.disclaimer}>
      <Icon name={provenance.isLiveInference ? 'sparkles' : 'info'} size={12} />
      {provenance.isLiveInference ? 'Live model' : 'Development fallback'}
      {provenance.model ? ` · ${provenance.model}` : ''}
    </span>
  );
}

function summaryText(summary: AiSummaryResponse): string {
  return [
    `Issue overview: ${summary.mainIssue}`,
    `Customer context: ${summary.keyContext.join('; ') || 'None'}`,
    `Actions taken: ${summary.actionsTaken.join('; ') || 'None'}`,
    `Current status: ${summary.currentState}`,
    `Pending resolution: ${summary.outstandingItems.join('; ') || 'None'}`,
    `Uncertainties: ${summary.uncertainties.join('; ') || 'None'}`,
  ].join('\n');
}
