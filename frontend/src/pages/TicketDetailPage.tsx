import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { ticketsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { Badge, Card, EmptyState, ErrorBanner, InfoBanner, Spinner } from '../components/ui';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { formatDate, humanise } from '../lib/format';
import type {
  AiSentimentResponse,
  AiSuggestedResponse,
  AiSummaryResponse,
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

export function TicketDetailPage() {
  const { id = '' } = useParams();
  const { user } = useAuth();
  const { data, loading, error, reload } = useAsync(() => ticketsApi.get(id), [id]);

  const [message, setMessage] = useState('');
  const [internalNote, setInternalNote] = useState(false);
  const [status, setStatus] = useState<TicketStatus | ''>('');
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const [summary, setSummary] = useState<AiSummaryResponse | null>(null);
  const [suggestion, setSuggestion] = useState<AiSuggestedResponse | null>(null);
  const [sentiment, setSentiment] = useState<AiSentimentResponse | null>(null);
  const [actions, setActions] = useState<NextBestActionsResponse | null>(null);
  const [aiBusy, setAiBusy] = useState<string | null>(null);

  if (loading) {
    return <Spinner />;
  }

  if (error || !data) {
    return <ErrorBanner message={error ?? 'Ticket not found.'} />;
  }

  const currentStatus = status || data.status;

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

  async function runAi(label: string, action: () => Promise<void>) {
    setAiBusy(label);
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
    if (!body) {
      return;
    }
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

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>{data.reference}</h1>
          <p className="muted">
            {data.subject} · <Badge value={data.status} /> · <Badge value={data.priority} />
          </p>
        </div>
      </header>

      <div className="ticket-layout">
        <div className="ticket-main">
          <Card title="Conversation">
            {data.messages.length === 0 ? (
              <EmptyState message="No messages yet." />
            ) : (
              <ul className="message-list">
                {data.messages.map((item) => (
                  <li
                    key={item.id}
                    className={item.isInternalNote ? 'message internal' : 'message'}
                  >
                    <div className="message-head">
                      <strong>{humanise(item.authorType)}</strong>
                      {item.isInternalNote && <span className="badge badge-internal">Internal note</span>}
                      <span className="muted">{formatDate(item.createdAtUtc)}</span>
                    </div>
                    <p>{item.body}</p>
                  </li>
                ))}
              </ul>
            )}

            <div className="composer">
              <textarea
                rows={3}
                placeholder="Write a reply or internal note…"
                value={message}
                onChange={(e) => setMessage(e.target.value)}
              />
              <div className="composer-actions">
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={internalNote}
                    onChange={(e) => setInternalNote(e.target.checked)}
                  />
                  Internal note
                </label>
                <button
                  type="button"
                  className="btn btn-primary"
                  disabled={busy || message.trim().length === 0}
                  onClick={() => void handleSend()}
                >
                  Send
                </button>
              </div>
            </div>
          </Card>
        </div>

        <aside className="ticket-side">
          <Card title="Assignment & status">
            <dl className="meta">
              <div>
                <dt>Assigned agent</dt>
                <dd>{data.assignedAgentId ?? 'Unassigned'}</dd>
              </div>
              <div>
                <dt>SLA due</dt>
                <dd>{formatDate(data.slaDueAtUtc)}</dd>
              </div>
            </dl>
            <div className="stack">
              <button
                type="button"
                className="btn btn-primary"
                disabled={busy || !user}
                onClick={() => user && runAction(() => ticketsApi.assign(data.id, user.id), 'Ticket assigned to you.')}
              >
                Assign to me
              </button>
              <label className="field">
                <span>Status</span>
                <select value={currentStatus} onChange={(e) => setStatus(e.target.value as TicketStatus)}>
                  {STATUS_OPTIONS.map((option) => (
                    <option key={option} value={option}>
                      {humanise(option)}
                    </option>
                  ))}
                </select>
              </label>
              <button
                type="button"
                className="btn btn-primary"
                disabled={busy || currentStatus === data.status}
                onClick={() =>
                  runAction(() => ticketsApi.changeStatus(data.id, currentStatus as TicketStatus), 'Status updated.')
                }
              >
                Update status
              </button>
            </div>
          </Card>

          <Card title="AI assistance">
            <div className="stack">
              <button
                type="button"
                className="btn btn-ghost"
                disabled={aiBusy !== null}
                onClick={() => runAi('summarise', async () => setSummary(await ticketsApi.ai.summarize(data.id)))}
              >
                {aiBusy === 'summarise' ? 'Summarising…' : 'Summarise'}
              </button>
              <button
                type="button"
                className="btn btn-ghost"
                disabled={aiBusy !== null}
                onClick={() => runAi('suggest', async () => setSuggestion(await ticketsApi.ai.suggestResponse(data.id)))}
              >
                {aiBusy === 'suggest' ? 'Thinking…' : 'Suggest response'}
              </button>
              <button
                type="button"
                className="btn btn-ghost"
                disabled={aiBusy !== null}
                onClick={() => runAi('sentiment', async () => setSentiment(await ticketsApi.ai.analyzeSentiment(data.id)))}
              >
                {aiBusy === 'sentiment' ? 'Analysing…' : 'Analyse sentiment'}
              </button>
              <button
                type="button"
                className="btn btn-ghost"
                disabled={aiBusy !== null}
                onClick={() => runAi('actions', async () => setActions(await ticketsApi.ai.nextBestActions(data.id)))}
              >
                {aiBusy === 'actions' ? 'Working…' : 'Next best actions'}
              </button>
            </div>

            <div aria-live="polite" className="ai-results">
              {summary && (
                <div className="ai-panel">
                  <h4>Summary</h4>
                  <p>{summary.mainIssue}</p>
                  <AiList title="Key context" items={summary.keyContext} />
                  <AiList title="Outstanding" items={summary.outstandingItems} />
                  <Provenance note={summary.provenance.disclaimer} live={summary.provenance.isLiveInference} />
                </div>
              )}

              {suggestion && (
                <div className="ai-panel">
                  <h4>Suggested response</h4>
                  <p>{suggestion.body}</p>
                  <button
                    type="button"
                    className="btn btn-ghost"
                    onClick={() => setMessage(suggestion.body)}
                  >
                    Use in composer
                  </button>
                  <Provenance note={suggestion.provenance.disclaimer} live={suggestion.provenance.isLiveInference} />
                </div>
              )}

              {sentiment && (
                <div className="ai-panel">
                  <h4>Sentiment: {humanise(sentiment.label)}</h4>
                  <p>{sentiment.explanation}</p>
                  {sentiment.escalationRecommended && <InfoBanner message="Escalation recommended." />}
                  <Provenance note={sentiment.provenance.disclaimer} live={sentiment.provenance.isLiveInference} />
                </div>
              )}

              {actions && (
                <div className="ai-panel">
                  <h4>Next best actions</h4>
                  <ul className="ai-actions">
                    {actions.actions.map((action, index) => (
                      <li key={index}>
                        <strong>{action.action}</strong>
                        <span className="muted"> ({action.priority})</span>
                        <p>{action.reason}</p>
                      </li>
                    ))}
                  </ul>
                  <Provenance note={actions.provenance.disclaimer} live={actions.provenance.isLiveInference} />
                </div>
              )}
            </div>
          </Card>
        </aside>
      </div>
    </div>
  );
}

function AiList({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) {
    return null;
  }
  return (
    <div>
      <h5>{title}</h5>
      <ul>
        {items.map((item, index) => (
          <li key={index}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

function Provenance({ note, live }: { note: string; live: boolean }) {
  return (
    <p className="provenance">
      <span className={live ? 'badge badge-approved' : 'badge badge-pending'}>
        {live ? 'Live model' : 'Development fallback'}
      </span>{' '}
      {note}
    </p>
  );
}
