import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { documentsApi, reviewsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { Icon } from '../components/icons';
import {
  Card,
  EmptyState,
  ErrorBanner,
  InfoBanner,
  Spinner,
  StatusBadge,
  Timeline,
} from '../components/ui';
import type { TimelineEntry } from '../components/ui';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { formatBytes, formatDate, humanise } from '../lib/format';
import type { DocumentResponse, DocumentReviewStatus } from '../api/types';

type Decision = 'approve' | 'reject' | 'requestInformation';

const REASON_PRESETS: Record<Decision, string[]> = {
  approve: [
    'Identity and income evidence verified. All required documents are in order.',
    'Eligibility confirmed and KYC checks passed.',
  ],
  reject: [
    'Income evidence is insufficient for the selected product.',
    'Identity documents could not be verified.',
  ],
  requestInformation: [
    'Please upload a recent proof of address dated within the last three months.',
    'Please provide a clearer copy of your proof of income.',
  ],
};

export function ReviewDetailPage() {
  const { id = '' } = useParams();
  const { data, loading, error, reload } = useAsync(() => reviewsApi.get(id), [id]);
  const toast = useToast();

  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState<Decision | null>(null);

  if (loading) {
    return (
      <div className="page">
        <Spinner label="Loading application…" />
      </div>
    );
  }

  if (error || !data) {
    return (
      <div className="page">
        <ErrorBanner message={error ?? 'Application not found.'} onRetry={reload} />
        <Link to="/reviews" className="btn btn-secondary section-gap">
          Back to review queue
        </Link>
      </div>
    );
  }

  async function runAction(action: () => Promise<unknown>, successMessage: string) {
    setBusy(true);
    try {
      await action();
      toast.success(successMessage);
      setReason('');
      reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Action failed.');
    } finally {
      setBusy(false);
    }
  }

  async function confirmDecision() {
    const decision = pending;
    setPending(null);
    if (!decision) return;

    const calls: Record<Decision, () => Promise<unknown>> = {
      approve: () => reviewsApi.approve(data!.id, reason),
      reject: () => reviewsApi.reject(data!.id, reason),
      requestInformation: () => reviewsApi.requestInformation(data!.id, reason),
    };
    const messages: Record<Decision, string> = {
      approve: 'Application approved.',
      reject: 'Application rejected.',
      requestInformation: 'Additional information requested.',
    };
    await runAction(calls[decision], messages[decision]);
  }

  async function reviewDocument(documentId: string, decision: DocumentReviewStatus) {
    await runAction(
      () => documentsApi.review(documentId, decision),
      `Document marked ${humanise(decision).toLowerCase()}.`,
    );
  }

  async function openDocument(document: DocumentResponse) {
    try {
      await documentsApi.open(document.id);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to open document.');
    }
  }

  const canStart = data.status === 'Submitted';
  const canDecide = data.status === 'UnderReview';
  const decided = ['Approved', 'Rejected'].includes(data.status);

  const historyItems: TimelineEntry[] = [...data.history].reverse().map((entry) => ({
    title: `${entry.fromStatus ? `${humanise(entry.fromStatus)} → ` : ''}${humanise(entry.toStatus)}`,
    meta: formatDate(entry.changedAtUtc),
    note: entry.reason ?? undefined,
    tone:
      entry.toStatus === 'Approved'
        ? 'success'
        : entry.toStatus === 'Rejected' || entry.toStatus === 'Withdrawn'
          ? 'danger'
          : 'active',
  }));

  return (
    <div className="page">
      <header className="detail-hero">
        <div className="detail-hero-main">
          <p className="eyebrow">Application review</p>
          <div className="detail-reference">
            <h1>{data.reference}</h1>
            <StatusBadge value={data.status} />
          </div>
          <p className="muted text-sm">
            {data.productName} · Submitted {formatDate(data.submittedAtUtc)}
            {data.decidedAtUtc ? ` · Decided ${formatDate(data.decidedAtUtc)}` : ''}
          </p>
        </div>
        <div className="detail-hero-actions">
          <Link to="/reviews" className="btn btn-ghost">
            <Icon name="arrowLeft" size={16} />
            Queue
          </Link>
          {canStart && (
            <button
              type="button"
              className="btn btn-primary"
              disabled={busy}
              onClick={() => void runAction(() => reviewsApi.start(data.id), 'Review started.')}
            >
              <Icon name="lock" size={15} />
              {busy ? 'Starting…' : 'Start review'}
            </button>
          )}
        </div>
      </header>

      {data.decisionReason && (
        <InfoBanner
          tone={data.status === 'AdditionalInformationRequired' ? 'warning' : 'info'}
          title={`Decision: ${humanise(data.status)}`}
          message={data.decisionReason}
        />
      )}

      <div className="split">
        <div className="stack-lg">
          <Card title="Applicant answers" subtitle="Information provided in the application">
            {Object.keys(data.answers).length === 0 ? (
              <EmptyState icon="inbox" message="No answers recorded." />
            ) : (
              <dl className="answer-grid">
                {Object.entries(data.answers).map(([key, value]) => (
                  <div className="answer-item" key={key}>
                    <dt>{humanise(key)}</dt>
                    <dd>{value || <span className="muted">Not provided</span>}</dd>
                  </div>
                ))}
              </dl>
            )}
          </Card>

          <Card
            title="Documents"
            subtitle={`${data.documents.length} uploaded · review each document before deciding`}
          >
            {data.documents.length === 0 ? (
              <EmptyState icon="file" message="No documents uploaded." />
            ) : (
              <div className="doc-list">
                {data.documents.map((document) => (
                  <div className="doc-row" key={document.id}>
                    <div className="doc-row-main">
                      <span className="doc-row-name">
                        <Icon name="file" size={16} />
                        {document.originalFileName}
                      </span>
                      <span className="doc-row-meta">
                        <span>{humanise(document.documentType)}</span>
                        <span>{formatBytes(document.sizeBytes)}</span>
                        <span>Uploaded {formatDate(document.uploadedAtUtc)}</span>
                      </span>
                      {document.reviewNotes && (
                        <span className="doc-row-meta">
                          <Icon name="message" size={13} />
                          {document.reviewNotes}
                        </span>
                      )}
                    </div>
                    <div className="doc-row-actions">
                      <StatusBadge value={document.scanStatus} dot={false} />
                      <StatusBadge value={document.reviewStatus} />
                      <button
                        type="button"
                        className="btn btn-ghost btn-sm"
                        onClick={() => void openDocument(document)}
                      >
                        View
                      </button>
                      {document.isReviewable && (
                        <>
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            disabled={busy}
                            onClick={() => void reviewDocument(document.id, 'Approved')}
                          >
                            Approve
                          </button>
                          <button
                            type="button"
                            className="btn btn-ghost btn-sm"
                            disabled={busy}
                            onClick={() => void reviewDocument(document.id, 'NeedsReplacement')}
                          >
                            Replace
                          </button>
                          <button
                            type="button"
                            className="btn btn-ghost btn-sm"
                            disabled={busy}
                            onClick={() => void reviewDocument(document.id, 'Rejected')}
                          >
                            Reject
                          </button>
                        </>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Card>

          <Card title="History" subtitle="Complete audit trail">
            {historyItems.length === 0 ? (
              <EmptyState icon="clock" message="No status changes recorded." />
            ) : (
              <Timeline items={historyItems} />
            )}
          </Card>
        </div>

        <aside className="workspace-side">
          <Card title="Decision" subtitle="Every decision requires a written reason">
            {canStart && (
              <div className="stack">
                <p className="muted text-sm">
                  Starting the review locks this application to you while you assess it.
                </p>
                <button
                  type="button"
                  className="btn btn-primary btn-block"
                  disabled={busy}
                  onClick={() => void runAction(() => reviewsApi.start(data.id), 'Review started.')}
                >
                  {busy ? 'Starting…' : 'Start review'}
                </button>
              </div>
            )}

            {canDecide && (
              <div className="decision-panel" style={{ border: 0, padding: 0 }}>
                <div className="decision-panel-head">
                  <span className="badge badge--info">Under review</span>
                  <span className="muted text-xs">
                    {data.assignedReviewerId
                      ? `Reviewer ${data.assignedReviewerId.slice(0, 8)}`
                      : 'Unassigned'}
                  </span>
                </div>
                <div className="decision-panel-body">
                  <label className="field" htmlFor="review-reason">
                    <span>Reason / notes</span>
                    <textarea
                      id="review-reason"
                      rows={4}
                      value={reason}
                      onChange={(event) => setReason(event.target.value)}
                      placeholder="Explain the decision for the audit trail (minimum 5 characters)."
                    />
                  </label>
                  <div className="chip-row">
                    {REASON_PRESETS.approve.slice(0, 1).map((preset) => (
                      <button
                        key={preset}
                        type="button"
                        className="chip"
                        onClick={() => setReason(preset)}
                      >
                        Approve preset
                      </button>
                    ))}
                    {REASON_PRESETS.requestInformation.slice(0, 1).map((preset) => (
                      <button
                        key={preset}
                        type="button"
                        className="chip"
                        onClick={() => setReason(preset)}
                      >
                        Info preset
                      </button>
                    ))}
                    {REASON_PRESETS.reject.slice(0, 1).map((preset) => (
                      <button
                        key={preset}
                        type="button"
                        className="chip"
                        onClick={() => setReason(preset)}
                      >
                        Reject preset
                      </button>
                    ))}
                  </div>

                  <div className="decision-actions">
                    <button
                      type="button"
                      className="btn btn-primary btn-block"
                      disabled={busy || reason.trim().length < 5}
                      onClick={() => setPending('approve')}
                    >
                      <Icon name="checkCircle" size={16} />
                      Approve application
                    </button>
                    <button
                      type="button"
                      className="btn btn-secondary btn-block"
                      disabled={busy || reason.trim().length < 5}
                      onClick={() => setPending('requestInformation')}
                    >
                      <Icon name="mail" size={16} />
                      Request information
                    </button>
                    <button
                      type="button"
                      className="btn btn-danger btn-block"
                      disabled={busy || reason.trim().length < 5}
                      onClick={() => setPending('reject')}
                    >
                      <Icon name="close" size={16} />
                      Reject application
                    </button>
                  </div>
                  {reason.trim().length > 0 && reason.trim().length < 5 && (
                    <p className="field-error">Reason must be at least 5 characters.</p>
                  )}
                </div>
              </div>
            )}

            {!canStart && !canDecide && (
              <div className="stack">
                <div className="review-applicant">
                  <span
                    className={`stat-icon stat-icon--${decided ? (data.status === 'Approved' ? 'success' : 'danger') : 'primary'}`}
                    aria-hidden="true"
                  >
                    <Icon name={decided ? 'checkCircle' : 'clock'} size={17} />
                  </span>
                  <div>
                    <strong>{humanise(data.status)}</strong>
                    <p className="muted text-xs">
                      {data.decidedAtUtc
                        ? `Decided ${formatDate(data.decidedAtUtc)}`
                        : 'No decision recorded'}
                    </p>
                  </div>
                </div>
                {data.decisionReason && <p className="muted text-sm">{data.decisionReason}</p>}
              </div>
            )}
          </Card>

          <Card title="Application" subtitle="Summary">
            <dl className="meta">
              <div>
                <dt>Product</dt>
                <dd>{data.productName}</dd>
              </div>
              <div>
                <dt>Reference</dt>
                <dd className="mono">{data.reference}</dd>
              </div>
              <div>
                <dt>Created</dt>
                <dd>{formatDate(data.createdAtUtc)}</dd>
              </div>
              <div>
                <dt>Submitted</dt>
                <dd>{formatDate(data.submittedAtUtc)}</dd>
              </div>
              <div>
                <dt>Documents</dt>
                <dd>{data.documents.length}</dd>
              </div>
            </dl>
          </Card>
        </aside>
      </div>

      <ConfirmDialog
        open={pending !== null}
        title={
          pending === 'approve'
            ? 'Approve application'
            : pending === 'reject'
              ? 'Reject application'
              : 'Request additional information'
        }
        message={`"${reason.trim()}" — this decision will be recorded in the audit trail.`}
        confirmLabel={
          pending === 'approve' ? 'Approve' : pending === 'reject' ? 'Reject' : 'Request information'
        }
        destructive={pending === 'reject'}
        busy={busy}
        onConfirm={() => void confirmDecision()}
        onCancel={() => setPending(null)}
      />
    </div>
  );
}
