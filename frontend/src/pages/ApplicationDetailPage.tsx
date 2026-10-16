import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { applicationsApi, documentsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { Badge, Card, EmptyState, ErrorBanner, InfoBanner, Spinner } from '../components/ui';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { ACCEPT_UPLOAD_TYPES, validateUpload } from '../lib/uploads';
import { formatBytes, formatDate, humanise } from '../lib/format';
import type { KycDocumentType } from '../api/types';

interface AnswerRow {
  key: string;
  value: string;
}

const DOCUMENT_TYPES: KycDocumentType[] = [
  'ProofOfIdentity',
  'ProofOfAddress',
  'ProofOfIncome',
  'Photograph',
  'Other',
];

const EDITABLE: string[] = ['Draft', 'AdditionalInformationRequired'];

export function ApplicationDetailPage() {
  const { id = '' } = useParams();
  const { data, loading, error, reload } = useAsync(() => applicationsApi.getMine(id), [id]);

  const [rows, setRows] = useState<AnswerRow[]>([]);
  const [busy, setBusy] = useState(false);
  const [confirmWithdraw, setConfirmWithdraw] = useState(false);
  const [uploadType, setUploadType] = useState<KycDocumentType>('ProofOfIdentity');
  const [file, setFile] = useState<File | null>(null);
  const [fileInputKey, setFileInputKey] = useState(0);
  const toast = useToast();

  useEffect(() => {
    if (data) {
      const entries = Object.entries(data.answers).map(([key, value]) => ({ key, value }));
      setRows(entries.length > 0 ? entries : [{ key: '', value: '' }]);
    }
  }, [data]);

  if (loading) {
    return <Spinner />;
  }

  if (error || !data) {
    return <ErrorBanner message={error ?? 'Application not found.'} />;
  }

  const editable = EDITABLE.includes(data.status);

  function collectAnswers(): Record<string, string> {
    const answers: Record<string, string> = {};
    for (const row of rows) {
      if (row.key.trim()) {
        answers[row.key.trim()] = row.value;
      }
    }
    return answers;
  }

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

  async function handleWithdraw() {
    setConfirmWithdraw(false);
    await runAction(() => applicationsApi.withdraw(data!.id), 'Application withdrawn.');
  }

  async function handleUpload() {
    if (!file) {
      return;
    }
    const problem = validateUpload(file);
    if (problem) {
      toast.error(problem);
      return;
    }
    await runAction(() => documentsApi.upload(data!.id, uploadType, file), 'Document uploaded.');
    setFile(null);
    setFileInputKey((current) => current + 1);
  }

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

      {data.decisionReason && (
        <InfoBanner message={`Decision: ${humanise(data.status)} — ${data.decisionReason}`} />
      )}

      <Card title="Application details">
        <dl className="meta">
          <div>
            <dt>Created</dt>
            <dd>{formatDate(data.createdAtUtc)}</dd>
          </div>
          <div>
            <dt>Submitted</dt>
            <dd>{formatDate(data.submittedAtUtc)}</dd>
          </div>
          <div>
            <dt>Decided</dt>
            <dd>{formatDate(data.decidedAtUtc)}</dd>
          </div>
        </dl>
      </Card>

      <Card
        title="Form answers"
        actions={
          editable ? (
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => setRows((current) => [...current, { key: '', value: '' }])}
            >
              Add field
            </button>
          ) : undefined
        }
      >
        {editable ? (
          <div className="answer-editor">
            {rows.map((row, index) => (
              <div className="form-row" key={index}>
                <input
                  placeholder="Field"
                  value={row.key}
                  onChange={(e) =>
                    setRows((current) =>
                      current.map((item, i) => (i === index ? { ...item, key: e.target.value } : item)),
                    )
                  }
                />
                <input
                  placeholder="Value"
                  value={row.value}
                  onChange={(e) =>
                    setRows((current) =>
                      current.map((item, i) => (i === index ? { ...item, value: e.target.value } : item)),
                    )
                  }
                />
                <button
                  type="button"
                  className="btn btn-ghost"
                  onClick={() => setRows((current) => current.filter((_, i) => i !== index))}
                >
                  Remove
                </button>
              </div>
            ))}
          </div>
        ) : Object.keys(data.answers).length === 0 ? (
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

      <div className="action-bar">
        {editable && (
          <button
            type="button"
            className="btn btn-primary"
            disabled={busy}
            onClick={() => runAction(() => applicationsApi.update(data.id, collectAnswers()), 'Draft saved.')}
          >
            Save draft
          </button>
        )}
        {editable && (
          <button
            type="button"
            className="btn btn-primary"
            disabled={busy}
            onClick={() =>
              runAction(
                () => applicationsApi.update(data.id, collectAnswers()).then(() => applicationsApi.submit(data.id)),
                'Application submitted.',
              )
            }
          >
            Submit
          </button>
        )}
        {data.status === 'Draft' && (
          <button
            type="button"
            className="btn btn-danger"
            disabled={busy}
            onClick={() => setConfirmWithdraw(true)}
          >
            Withdraw
          </button>
        )}
      </div>

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
                <th>Uploaded</th>
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
                  <td>{formatDate(document.uploadedAtUtc)}</td>
                  <td className="right">
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
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {editable && (
          <div className="upload-row">
            <label className="field">
              <span>Document type</span>
              <select
                value={uploadType}
                onChange={(e) => setUploadType(e.target.value as KycDocumentType)}
              >
                {DOCUMENT_TYPES.map((type) => (
                  <option key={type} value={type}>
                    {humanise(type)}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>File (PDF, JPG or PNG, max 5 MB)</span>
              <input
                key={fileInputKey}
                type="file"
                accept={ACCEPT_UPLOAD_TYPES}
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              />
            </label>
            <button
              type="button"
              className="btn btn-primary"
              disabled={busy || !file}
              onClick={() => void handleUpload()}
            >
              {busy ? 'Uploading…' : 'Upload'}
            </button>
          </div>
        )}
      </Card>

      <Card title="History">
        {data.history.length === 0 ? (
          <EmptyState message="No status changes recorded yet." />
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
        open={confirmWithdraw}
        title="Withdraw application"
        message="Withdrawing this draft is permanent and cannot be undone. Do you want to continue?"
        confirmLabel="Withdraw"
        destructive
        busy={busy}
        onConfirm={handleWithdraw}
        onCancel={() => setConfirmWithdraw(false)}
      />
    </div>
  );
}
