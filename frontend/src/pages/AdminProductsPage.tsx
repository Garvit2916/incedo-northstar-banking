import { useState } from 'react';
import { adminApi, productsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { Card, ErrorBanner } from '../components/ui';
import { SkeletonTable } from '../components/Skeleton';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import type { ProductResponse } from '../api/types';

interface ProductForm {
  code: string;
  name: string;
  summary: string;
  description: string;
  category: string;
  eligibility: string;
  requiredDocuments: string;
  terms: string;
  minimumAge: number;
  isActive: boolean;
  displayOrder: number;
}

const EMPTY_FORM: ProductForm = {
  code: '',
  name: '',
  summary: '',
  description: '',
  category: '',
  eligibility: '',
  requiredDocuments: 'ProofOfIdentity,ProofOfAddress',
  terms: '',
  minimumAge: 18,
  isActive: true,
  displayOrder: 0,
};

export function AdminProductsPage() {
  const { data, loading, error, reload } = useAsync(() => productsApi.list(true), []);
  const [form, setForm] = useState<ProductForm>(EMPTY_FORM);
  const [busy, setBusy] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [pendingToggle, setPendingToggle] = useState<ProductResponse | null>(null);
  const [showErrors, setShowErrors] = useState(false);
  const toast = useToast();

  const codeError = showErrors && form.code.trim().length === 0 ? 'Code is required.' : null;
  const nameError = showErrors && form.name.trim().length === 0 ? 'Name is required.' : null;

  async function submit() {
    if (form.code.trim().length === 0 || form.name.trim().length === 0) {
      setShowErrors(true);
      toast.error('Please correct the highlighted fields.');
      return;
    }
    setBusy(true);
    try {
      await adminApi.createProduct({
        ...form,
        requiredDocuments: form.requiredDocuments
          .split(',')
          .map((value) => value.trim())
          .filter(Boolean),
      });
      toast.success(`Product ${form.code} created.`);
      setForm(EMPTY_FORM);
      setShowErrors(false);
      reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to create product.');
    } finally {
      setBusy(false);
    }
  }

  async function confirmToggle() {
    const product = pendingToggle;
    setPendingToggle(null);
    if (!product) {
      return;
    }
    const nextActive = !product.isActive;
    setBusyId(product.id);
    try {
      await adminApi.updateProduct(product.id, {
        code: product.code,
        name: product.name,
        summary: product.summary,
        description: product.description,
        category: product.category,
        eligibility: product.eligibility,
        requiredDocuments: product.requiredDocuments,
        terms: product.terms,
        minimumAge: product.minimumAge,
        displayOrder: product.displayOrder,
        isActive: nextActive,
      });
      toast.success(nextActive ? 'Product activated.' : 'Product deactivated.');
      reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to update product.');
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>Product administration</h1>
          <p className="muted">Create and review the product catalogue.</p>
        </div>
      </header>

      {error && <ErrorBanner message={error} />}

      <Card title="Existing products">
        {loading || !data ? (
          <SkeletonTable rows={6} />
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Code</th>
                <th>Name</th>
                <th>Category</th>
                <th>Min age</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {data.map((product) => (
                <tr key={product.id}>
                  <td>{product.code}</td>
                  <td>{product.name}</td>
                  <td>{product.category}</td>
                  <td>{product.minimumAge}</td>
                  <td>
                    <span className={product.isActive ? 'badge badge-approved' : 'badge badge-rejected'}>
                      {product.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td>
                    <button
                      type="button"
                      className="btn btn-ghost"
                      disabled={busyId === product.id}
                      onClick={() => setPendingToggle(product)}
                    >
                      {product.isActive ? 'Deactivate' : 'Activate'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <Card title="Create product">
        <div className="form-grid">
          <Field
            label="Code"
            value={form.code}
            error={codeError}
            onChange={(v) => setForm({ ...form, code: v })}
          />
          <Field
            label="Name"
            value={form.name}
            error={nameError}
            onChange={(v) => setForm({ ...form, name: v })}
          />
          <Field label="Category" value={form.category} onChange={(v) => setForm({ ...form, category: v })} />
          <Field
            label="Minimum age"
            type="number"
            value={String(form.minimumAge)}
            onChange={(v) => setForm({ ...form, minimumAge: Number(v) })}
          />
          <Field
            label="Display order"
            type="number"
            value={String(form.displayOrder)}
            onChange={(v) => setForm({ ...form, displayOrder: Number(v) })}
          />
          <Field
            label="Required documents (comma separated)"
            value={form.requiredDocuments}
            onChange={(v) => setForm({ ...form, requiredDocuments: v })}
          />
          <Field label="Summary" value={form.summary} onChange={(v) => setForm({ ...form, summary: v })} />
          <Field label="Description" value={form.description} onChange={(v) => setForm({ ...form, description: v })} />
          <Field label="Eligibility" value={form.eligibility} onChange={(v) => setForm({ ...form, eligibility: v })} />
          <Field label="Terms" value={form.terms} onChange={(v) => setForm({ ...form, terms: v })} />
        </div>
        <div className="action-bar">
          <button type="button" className="btn btn-primary" disabled={busy} onClick={submit}>
            {busy ? 'Creating…' : 'Create product'}
          </button>
        </div>
      </Card>

      <ConfirmDialog
        open={pendingToggle !== null}
        title={pendingToggle?.isActive ? 'Deactivate product' : 'Activate product'}
        message={
          pendingToggle?.isActive
            ? `Deactivate ${pendingToggle?.name}? It will no longer appear for customers.`
            : `Activate ${pendingToggle?.name}? It will become available to customers.`
        }
        confirmLabel={pendingToggle?.isActive ? 'Deactivate' : 'Activate'}
        destructive={pendingToggle?.isActive === true}
        busy={busyId === pendingToggle?.id}
        onConfirm={confirmToggle}
        onCancel={() => setPendingToggle(null)}
      />
    </div>
  );
}

function Field({
  label,
  value,
  onChange,
  error,
  type = 'text',
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string | null;
  type?: string;
}) {
  const id = `product-${label.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`;
  return (
    <label className="field" htmlFor={id}>
      <span>{label}</span>
      <input
        id={id}
        type={type}
        value={value}
        aria-invalid={error ? 'true' : undefined}
        aria-describedby={error ? `${id}-error` : undefined}
        onChange={(e) => onChange(e.target.value)}
      />
      {error && (
        <span className="field-error" id={`${id}-error`} role="alert">
          {error}
        </span>
      )}
    </label>
  );
}
