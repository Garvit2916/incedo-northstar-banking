import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { documentsApi, reviewsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { Badge, Card, EmptyState, ErrorBanner, Spinner } from '../components/ui';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { formatBytes, formatDate, humanise } from '../lib/format';
import type { DocumentReviewStatus } from '../api/types';

type Decision = 'approve' | 'reject' | 'requestInformation';

export function ReviewDetailPage() {
  const { id = '' } = useParams();
  const { data, loading, error, reload } = useAsync(() => reviewsApi.get(id), [id]);

  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState<Decision | null>(null);
  const toast = useToast();

  if (loading) {
    return <Spinner />;
  }

  if (error || !data) {
    return <ErrorBanner message={error ?? 'Application not found.'} />;
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
    if (!decision) {
      return;
    }
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
      `Document marked ${humanise(decision)}.`,
    );
  }

  const canStart = data.status === 'Submitted';
  const canDecide = data.status === 'UnderReview';

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>{data.reference}</h1>
          <p className="muted">
            {data.productName} · <Badge value={data.status} />
          </p>
        </div>
      </header>

      <div className="action-bar">
        {canStart && (
          <button
            type="button"
            className="btn btn-primary"
            disabled={busy}
            onClick={() => runAction(() => reviewsApi.start(data.id), 'Review started.')}
          >
            Start review
          </button>
        )}
      </div>

      {canDecide && (
        <Card title="Decision">
          <label className="field">
            <span>Reason / notes (required, min 5 characters)</span>
            <textarea
              rows={3}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Explain the decision for the audit trail."
            />
          </label>
          <div className="action-bar">
            <button
              type="button"
              className="btn btn-primary"
              disabled={busy || reason.trim().length < 5}
              onClick={() => setPending('approve')}
            >
              Approve
            </button>
            <button
              type="button"
              className="btn btn-ghost"
              disabled={busy || reason.trim().length < 5}
              onClick={() => setPending('requestInformation')}
            >
              Request information
            </button>
            <button
              type="button"
              className="btn btn-danger"
              disabled={busy || reason.trim().length < 5}
              onClick={() => setPending('reject')}
            >
              Reject
            </button>
          </div>
        </Card>
      )}

      <Card title="Applicant answers">
        {Object.keys(data.answers).length === 0 ? (
          <EmptyState message="No answers recorded." />
        ) : (
          <dl className="meta">
            {Object.entries(data.answers).map(([key, value]) => (
              <div key={key}>
                <dt>{key}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
        )}
      </Card>

      <Card title="Documents">
        {data.documents.length === 0 ? (
          <EmptyState message="No documents uploaded." />
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>File</th>
                <th>Type</th>
                <th>Size</th>
                <th>Scan</th>
                <th>Review</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {data.documents.map((document) => (
                <tr key={document.id}>
                  <td>{document.originalFileName}</td>
                  <td>{humanise(document.documentType)}</td>
                  <td>{formatBytes(document.sizeBytes)}</td>
                  <td>
                    <Badge value={document.scanStatus} />
                  </td>
                  <td>
                    <Badge value={document.reviewStatus} />
                  </td>
                  <td className="right action-cell">
                    <button
                      type="button"
                      className="btn btn-ghost"
                      onClick={async () => {
                        try {
                          await documentsApi.open(document.id);
                        } catch (err) {
                          toast.error(
                            err instanceof ApiError ? err.detail ?? err.message : 'Unable to open document.',
                          );
                        }
                      }}
                    >
                      View
                    </button>
                    <button
                      type="button"
                      className="btn btn-ghost"
                      disabled={busy}
                      onClick={() => reviewDocument(document.id, 'Approved')}
                    >
                      Approve
                    </button>
                    <button
                      type="button"
                      className="btn btn-ghost"
                      disabled={busy}
                      onClick={() => reviewDocument(document.id, 'Rejected')}
                    >
                      Reject
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <Card title="History">
        {data.history.length === 0 ? (
          <EmptyState message="No status changes recorded." />
        ) : (
          <ol className="timeline">
            {data.history.map((entry, index) => (
              <li key={index}>
                <span className="timeline-status">
                  {entry.fromStatus ? `${humanise(entry.fromStatus)} → ` : ''}
                  {humanise(entry.toStatus)}
                </span>
                <span className="muted">{formatDate(entry.changedAtUtc)}</span>
                {entry.reason && <span className="timeline-reason">{entry.reason}</span>}
              </li>
            ))}
          </ol>
        )}
      </Card>

      <ConfirmDialog
        open={pending !== null}
        title={
          pending === 'approve'
            ? 'Approve application'
            : pending === 'reject'
              ? 'Reject application'
              : 'Request additional information'
        }
        message={`${reason.trim()} — this decision will be recorded in the audit trail.`}
        confirmLabel={
          pending === 'approve' ? 'Approve' : pending === 'reject' ? 'Reject' : 'Request information'
        }
        destructive={pending === 'reject'}
        busy={busy}
        onConfirm={confirmDecision}
        onCancel={() => setPending(null)}
      />
    </div>
  );
}
