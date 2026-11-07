import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ticketsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { Icon } from '../components/icons';
import type { IconName } from '../components/icons';
import {
  Card,
  EmptyState,
  ErrorBanner,
  InfoBanner,
  Spinner,
  StatusBadge,
} from '../components/ui';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { formatDate, formatRelative, humanise } from '../lib/format';
import type {
  AiProvenance,
  AiSentimentResponse,
  AiSuggestedResponse,
  AiSummaryResponse,
  MessageAuthorType,
  NextBestActionsResponse,
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

type AiFeature = 'summary' | 'suggestion' | 'sentiment' | 'actions';

const AI_ACTIONS: { key: AiFeature; label: string; icon: IconName; busyLabel: string }[] = [
  { key: 'summary', label: 'Summarise ticket', icon: 'list', busyLabel: 'Summarising…' },
  { key: 'suggestion', label: 'Suggest reply', icon: 'sparkles', busyLabel: 'Drafting…' },
  { key: 'sentiment', label: 'Read sentiment', icon: 'heart', busyLabel: 'Analysing…' },
  { key: 'actions', label: 'Next best actions', icon: 'trendingUp', busyLabel: 'Thinking…' },
];

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

export function TicketDetailPage() {
  const { id = '' } = useParams();
  const { user, hasRole } = useAuth();
  const { data, loading, error, reload } = useAsync(() => ticketsApi.get(id), [id]);
  const toast = useToast();

  const [message, setMessage] = useState('');
  const [internalNote, setInternalNote] = useState(false);
  const [status, setStatus] = useState<TicketStatus | ''>('');
  const [busy, setBusy] = useState(false);

  const [summary, setSummary] = useState<AiSummaryResponse | null>(null);
  const [suggestion, setSuggestion] = useState<AiSuggestedResponse | null>(null);
  const [sentiment, setSentiment] = useState<AiSentimentResponse | null>(null);
  const [actions, setActions] = useState<NextBestActionsResponse | null>(null);
  const [aiBusy, setAiBusy] = useState<AiFeature | null>(null);

  if (loading) {
    return (
      <div className="page">
        <Spinner label="Loading ticket…" />
      </div>
    );
  }

  if (error || !data) {
    return (
      <div className="page">
        <ErrorBanner message={error ?? 'Ticket not found.'} onRetry={reload} />
        <Link to="/tickets" className="btn btn-secondary section-gap">
          Back to tickets
        </Link>
      </div>
    );
  }

  const currentStatus = status || data.status;
  const isAssignedToMe = Boolean(user && data.assignedAgentId === user.id);

  async function runAction(action: () => Promise<unknown>, successMessage: string) {
    setBusy(true);
    try {
      await action();
      toast.success(successMessage);
      reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Action failed.');
    } finally {
      setBusy(false);
    }
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
    const body = message.trim();
    if (!body) return;
    setBusy(true);
    try {
      await ticketsApi.addMessage(data!.id, body, internalNote);
      setMessage('');
      setInternalNote(false);
      toast.success(internalNote ? 'Internal note added.' : 'Reply sent.');
      reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to send message.');
    } finally {
      setBusy(false);
    }
  }

  function useSuggestion(text: string) {
    setMessage(text);
    setInternalNote(false);
    toast.info('Suggestion inserted into the composer.');
  }

  function clearAi() {
    setSummary(null);
    setSuggestion(null);
    setSentiment(null);
    setActions(null);
  }

  const hasAiOutput = Boolean(summary || suggestion || sentiment || actions);
  const sentimentConfidence =
    sentiment?.confidence == null
      ? null
      : Math.round(sentiment.confidence <= 1 ? sentiment.confidence * 100 : sentiment.confidence);

  return (
    <div className="page">
      <header className="detail-hero">
        <div className="detail-hero-main">
          <p className="eyebrow">Support ticket</p>
          <div className="detail-reference">
            <h1>{data.reference}</h1>
            <StatusBadge value={data.status} />
            <StatusBadge value={data.priority} dot={false} />
          </div>
          <p className="text-strong" style={{ fontWeight: 600 }}>{data.subject}</p>
          <p className="muted text-sm">
            {humanise(data.category)} · Opened {formatDate(data.createdAtUtc)} · Updated{' '}
            {formatRelative(data.updatedAtUtc)}
          </p>
        </div>
        <div className="detail-hero-actions">
          <Link to="/tickets" className="btn btn-ghost">
            <Icon name="arrowLeft" size={16} />
            All tickets
          </Link>
          <button
            type="button"
            className="btn btn-primary"
            disabled={busy || isAssignedToMe || !user}
            onClick={() =>
              user && void runAction(() => ticketsApi.assign(data.id, user.id), 'Ticket assigned to you.')
            }
          >
            <Icon name="userPlus" size={16} />
            {isAssignedToMe ? 'Assigned to you' : 'Assign to me'}
          </button>
        </div>
      </header>

      <div className="workspace">
        <div className="workspace-main">
          <Card title="Conversation" subtitle={`${data.messages.length} message${data.messages.length === 1 ? '' : 's'}`} flush>
            {data.messages.length === 0 ? (
              <EmptyState icon="message" message="No messages yet." />
            ) : (
              <div className="conversation">
                {data.messages.map((item) => (
                  <div key={item.id} className={messageClass(item.authorType, item.isInternalNote)}>
                    <span className="message-avatar" aria-hidden="true">
                      <Icon
                        name={item.isInternalNote ? 'lock' : item.authorType === 'Customer' ? 'profile' : 'support'}
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

            <div className="composer">
              <label className="sr-only" htmlFor="ticket-reply">
                Reply
              </label>
              <textarea
                id="ticket-reply"
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
                  Internal note (not visible to the customer)
                </label>
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
          </Card>

          <Card title="AI assistance" subtitle="Grounded on this ticket's conversation">
            <div className="ai-card" style={{ border: 0, padding: 0, background: 'transparent', boxShadow: 'none' }}>
              <div className="ai-actions">
                {AI_ACTIONS.map((feature) => (
                  <button
                    key={feature.key}
                    type="button"
                    className="ai-action"
                    disabled={aiBusy !== null}
                    onClick={() =>
                      void runAi(feature.key, async () => {
                        if (feature.key === 'summary') setSummary(await ticketsApi.ai.summarize(data.id));
                        if (feature.key === 'suggestion') setSuggestion(await ticketsApi.ai.suggestResponse(data.id));
                        if (feature.key === 'sentiment') setSentiment(await ticketsApi.ai.analyzeSentiment(data.id));
                        if (feature.key === 'actions') setActions(await ticketsApi.ai.nextBestActions(data.id));
                      })
                    }
                  >
                    <span className="ai-action-top">
                      <Icon name={aiBusy === feature.key ? 'loader' : feature.icon} size={16} />
                      {feature.label}
                    </span>
                    <span className="muted text-xs">
                      {aiBusy === feature.key ? feature.busyLabel : 'Run analysis'}
                    </span>
                  </button>
                ))}
              </div>

              <div className="ai-output" aria-live="polite">
                {!hasAiOutput ? (
                  <div className="ai-empty">
                    <Icon name="sparkles" size={18} />
                    Run an AI action to see a summary, suggested reply, sentiment analysis or next best
                    actions. AI output is advisory and always requires human review.
                  </div>
                ) : (
                  <>
                    {summary && (
                      <div className="ai-result">
                        <div className="ai-result-head">
                          <h3>Summary</h3>
                          <Provenance provenance={summary.provenance} />
                        </div>
                        <div className="ai-result-body">
                          <p>{summary.mainIssue}</p>
                          <div className="ai-kv">
                            <span>Current state</span>
                            <strong>{summary.currentState}</strong>
                          </div>
                          <AiList title="Key context" items={summary.keyContext} />
                          <AiList title="Actions taken" items={summary.actionsTaken} />
                          <AiList title="Outstanding" items={summary.outstandingItems} />
                          <AiList title="Uncertainties" items={summary.uncertainties} />
                        </div>
                      </div>
                    )}

                    {suggestion && (
                      <div className="ai-result">
                        <div className="ai-result-head">
                          <h3>Suggested reply</h3>
                          <Provenance provenance={suggestion.provenance} />
                        </div>
                        <div className="ai-result-body">
                          <p className="text-xs muted">Subject: {suggestion.subject}</p>
                          <div className="ai-suggestion">
                            <pre>{suggestion.body}</pre>
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              onClick={() => useSuggestion(suggestion.body)}
                            >
                              <Icon name="copy" size={14} />
                              Use in composer
                            </button>
                          </div>
                          {suggestion.notes.length > 0 && <AiList title="Notes" items={suggestion.notes} />}
                        </div>
                      </div>
                    )}

                    {sentiment && (
                      <div className="ai-result">
                        <div className="ai-result-head">
                          <h3>Sentiment</h3>
                          <Provenance provenance={sentiment.provenance} />
                        </div>
                        <div className="ai-result-body">
                          <div className="sentiment-meter">
                            <div className="sentiment-head">
                              <span className="sentiment-label">{humanise(sentiment.label)}</span>
                              {sentimentConfidence !== null && (
                                <span className="muted text-xs">{sentimentConfidence}% confidence</span>
                              )}
                            </div>
                            {sentimentConfidence !== null && (
                              <div className="confidence">
                                <div className="confidence-track">
                                  <div
                                    className="confidence-bar"
                                    style={{ width: `${Math.min(100, Math.max(0, sentimentConfidence))}%` }}
                                  />
                                </div>
                              </div>
                            )}
                            <p>{sentiment.explanation}</p>
                          </div>
                          {sentiment.signals.length > 0 && (
                            <div className="chip-row section-gap">
                              {sentiment.signals.map((signal, index) => (
                                <span className="chip" key={index}>
                                  {signal}
                                </span>
                              ))}
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
                      </div>
                    )}

                    {actions && (
                      <div className="ai-result">
                        <div className="ai-result-head">
                          <h3>Next best actions</h3>
                          <Provenance provenance={actions.provenance} />
                        </div>
                        <div className="ai-result-body">
                          <ol className="nba-list">
                            {actions.actions.map((action, index) => (
                              <li className="nba-item" key={index}>
                                <span className="nba-rank">{index + 1}</span>
                                <div>
                                  <div className="nba-head">
                                    <strong>{action.action}</strong>
                                    <StatusBadge value={action.priority} dot={false} />
                                    {action.requiresHumanApproval && (
                                      <span className="badge badge--warning">Needs approval</span>
                                    )}
                                  </div>
                                  <p>{action.reason}</p>
                                  <p className="nba-evidence">
                                    <Icon name="info" size={12} /> Evidence: {action.evidence}
                                  </p>
                                </div>
                              </li>
                            ))}
                          </ol>
                        </div>
                      </div>
                    )}

                    <div className="section-head">
                      <span className="muted text-xs">{summary?.provenance.disclaimer ?? 'AI output requires human review.'}</span>
                      <button type="button" className="link-button text-xs" onClick={clearAi}>
                        Clear results
                      </button>
                    </div>
                  </>
                )}
              </div>
            </div>
          </Card>
        </div>

        <aside className="workspace-side">
          <Card title="Ticket details">
            <dl className="meta">
              <div>
                <dt>Status</dt>
                <dd>
                  <StatusBadge value={data.status} />
                </dd>
              </div>
              <div>
                <dt>Priority</dt>
                <dd>
                  <StatusBadge value={data.priority} dot={false} />
                </dd>
              </div>
              <div>
                <dt>Category</dt>
                <dd>{humanise(data.category)}</dd>
              </div>
              <div>
                <dt>Assigned agent</dt>
                <dd>{isAssignedToMe ? 'You' : data.assignedAgentId ? 'Another agent' : 'Unassigned'}</dd>
              </div>
              <div>
                <dt>SLA due</dt>
                <dd>
                  {data.slaDueAtUtc
                    ? `${formatRelative(data.slaDueAtUtc)} · ${formatDate(data.slaDueAtUtc)}`
                    : 'Not set'}
                </dd>
              </div>
              {data.resolvedAtUtc && (
                <div>
                  <dt>Resolved</dt>
                  <dd>{formatDate(data.resolvedAtUtc)}</dd>
                </div>
              )}
            </dl>
            {data.relatedApplicationId && (hasRole('Reviewer') || hasRole('Administrator')) && (
              <Link
                to={`/reviews/${data.relatedApplicationId}`}
                className="btn btn-secondary btn-sm btn-block section-gap"
              >
                View related application
                <Icon name="arrowUpRight" size={14} />
              </Link>
            )}
          </Card>

          <Card title="Update status" subtitle="Manual status changes are audited">
            <div className="stack">
              <label className="field" htmlFor="ticket-status">
                <span>New status</span>
                <select
                  id="ticket-status"
                  value={currentStatus}
                  onChange={(event) => setStatus(event.target.value as TicketStatus)}
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
                className="btn btn-primary btn-block"
                disabled={busy || currentStatus === data.status}
                onClick={() =>
                  void runAction(
                    () => ticketsApi.changeStatus(data.id, currentStatus as TicketStatus),
                    'Status updated.',
                  )
                }
              >
                {busy ? 'Updating…' : 'Update status'}
              </button>
            </div>
          </Card>

          <Card title="Customer" subtitle="Conversation participant">
            <div className="review-applicant">
              <span className="stat-icon stat-icon--primary" aria-hidden="true">
                <Icon name="profile" size={17} />
              </span>
              <div>
                <strong>Customer</strong>
                <p className="muted text-xs">
                  {data.customerUserId ? `ID ${data.customerUserId.slice(0, 8)}` : 'Not linked'}
                </p>
              </div>
            </div>
          </Card>
        </aside>
      </div>
    </div>
  );
}

function AiList({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) return null;
  return (
    <div className="ai-kv">
      <span>{title}</span>
      <ul>
        {items.map((item, index) => (
          <li key={index}>{item}</li>
        ))}
      </ul>
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
