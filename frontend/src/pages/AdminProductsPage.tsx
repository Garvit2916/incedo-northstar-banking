import { useMemo, useState } from 'react';
import { adminApi, productsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { Field } from '../components/forms';
import { Icon } from '../components/icons';
import { Modal } from '../components/Modal';
import { SkeletonTable } from '../components/Skeleton';
import {
  Card,
  EmptyState,
  ErrorBanner,
  StatCard,
  StatGrid,
} from '../components/ui';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { humanise } from '../lib/format';
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
  requiredDocuments: 'ProofOfIdentity, ProofOfAddress',
  terms: '',
  minimumAge: 18,
  isActive: true,
  displayOrder: 0,
};

const REQUIRED_FIELDS: { key: keyof ProductForm; label: string }[] = [
  { key: 'code', label: 'Code' },
  { key: 'name', label: 'Name' },
  { key: 'summary', label: 'Summary' },
  { key: 'description', label: 'Description' },
  { key: 'category', label: 'Category' },
  { key: 'eligibility', label: 'Eligibility' },
  { key: 'terms', label: 'Terms' },
];

export function AdminProductsPage() {
  const { data, loading, error, reload } = useAsync(() => productsApi.list(true), []);
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<ProductForm>(EMPTY_FORM);
  const [busy, setBusy] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [pendingToggle, setPendingToggle] = useState<ProductResponse | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const toast = useToast();

  const products = data ?? [];

  const summary = useMemo(() => {
    const active = products.filter((product) => product.isActive).length;
    const categories = new Set(products.map((product) => product.category)).size;
    return { total: products.length, active, inactive: products.length - active, categories };
  }, [products]);

  function update<K extends keyof ProductForm>(key: K, value: ProductForm[K]) {
    setForm((current) => ({ ...current, [key]: value }));
    setFieldErrors((current) => {
      if (!current[key]) return current;
      const next = { ...current };
      delete next[key];
      return next;
    });
  }

  function closeModal() {
    if (busy) return;
    setOpen(false);
    setForm(EMPTY_FORM);
    setFieldErrors({});
  }

  async function submit() {
    const validation: Record<string, string> = {};
    for (const field of REQUIRED_FIELDS) {
      if (!String(form[field.key] ?? '').trim()) {
        validation[field.key] = `${field.label} is required.`;
      }
    }
    if (form.minimumAge < 0 || form.minimumAge > 100) {
      validation.minimumAge = 'Minimum age must be between 0 and 100.';
    }
    setFieldErrors(validation);
    if (Object.keys(validation).length > 0) {
      toast.error('Please correct the highlighted fields.');
      return;
    }

    setBusy(true);
    try {
      await adminApi.createProduct({
        ...form,
        code: form.code.trim(),
        name: form.name.trim(),
        requiredDocuments: form.requiredDocuments
          .split(',')
          .map((value) => value.trim())
          .filter(Boolean),
      });
      toast.success(`Product ${form.code.trim()} created.`);
      setForm(EMPTY_FORM);
      setFieldErrors({});
      setOpen(false);
      reload();
    } catch (err) {
      if (err instanceof ApiError && err.fields) {
        const mapped: Record<string, string> = {};
        for (const [key, messages] of Object.entries(err.fields)) {
          if (messages[0]) mapped[key] = messages[0];
        }
        setFieldErrors(mapped);
      }
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to create product.');
    } finally {
      setBusy(false);
    }
  }

  async function confirmToggle() {
    const product = pendingToggle;
    setPendingToggle(null);
    if (!product) return;

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
      <div className="page-header">
        <div>
          <p className="eyebrow">Administration</p>
          <h1 className="page-title">Product catalogue</h1>
          <p className="page-description">
            Create products and control what customers can see in the catalogue.
          </p>
        </div>
        <div className="page-actions">
          <button type="button" className="btn btn-primary" onClick={() => setOpen(true)}>
            <Icon name="plus" size={16} />
            New product
          </button>
        </div>
      </div>

      {error ? (
        <ErrorBanner message={error} onRetry={reload} />
      ) : loading ? (
        <>
          <StatGrid>
            <StatCard label="Products" value="—" icon="products" tone="primary" />
            <StatCard label="Active" value="—" icon="checkCircle" tone="success" />
            <StatCard label="Categories" value="—" icon="list" tone="accent" />
          </StatGrid>
          <Card flush>
            <SkeletonTable rows={6} />
          </Card>
        </>
      ) : (
        <>
          <StatGrid>
            <StatCard label="Products" value={summary.total} icon="products" tone="primary" foot="In catalogue" />
            <StatCard label="Active" value={summary.active} icon="checkCircle" tone="success" foot="Visible to customers" />
            <StatCard label="Inactive" value={summary.inactive} icon="close" tone="danger" foot="Hidden from customers" />
            <StatCard label="Categories" value={summary.categories} icon="list" tone="accent" foot="Distinct groups" />
          </StatGrid>

          <Card title="Catalogue" subtitle="Activate or deactivate products without deleting history" flush>
            {products.length === 0 ? (
              <EmptyState
                icon="products"
                title="No products yet"
                message="Create your first product to make it available to customers."
                action={
                  <button type="button" className="btn btn-primary btn-sm" onClick={() => setOpen(true)}>
                    New product
                  </button>
                }
              />
            ) : (
              <div className="table-scroll">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Code</th>
                      <th>Product</th>
                      <th>Category</th>
                      <th>Min age</th>
                      <th>Order</th>
                      <th>Status</th>
                      <th className="right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {products.map((product) => (
                      <tr key={product.id}>
                        <td className="mono">{product.code}</td>
                        <td>
                          <strong>{product.name}</strong>
                          <p className="muted text-xs">{product.summary}</p>
                        </td>
                        <td>{humanise(product.category)}</td>
                        <td>{product.minimumAge}+</td>
                        <td className="tabular">{product.displayOrder}</td>
                        <td>
                          <span className={`badge badge--${product.isActive ? 'success' : 'neutral'}`}>
                            {product.isActive ? 'Active' : 'Inactive'}
                          </span>
                        </td>
                        <td className="right">
                          <button
                            type="button"
                            className={`btn btn-sm ${product.isActive ? 'btn-ghost' : 'btn-secondary'}`}
                            disabled={busyId === product.id}
                            onClick={() => setPendingToggle(product)}
                          >
                            {busyId === product.id
                              ? 'Updating…'
                              : product.isActive
                                ? 'Deactivate'
                                : 'Activate'}
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </>
      )}

      <Modal
        open={open}
        title="Create product"
        onClose={closeModal}
        busy={busy}
        footer={
          <>
            <button type="button" className="btn btn-ghost" onClick={closeModal} disabled={busy}>
              Cancel
            </button>
            <button type="button" className="btn btn-primary" onClick={() => void submit()} disabled={busy}>
              {busy ? 'Creating…' : 'Create product'}
            </button>
          </>
        }
      >
        <div className="create-grid">
          <Field label="Code" htmlFor="product-code" required error={fieldErrors.code}>
            <input
              id="product-code"
              value={form.code}
              onChange={(event) => update('code', event.target.value)}
              placeholder="SAV-002"
              maxLength={50}
            />
          </Field>
          <Field label="Name" htmlFor="product-name" required error={fieldErrors.name}>
            <input
              id="product-name"
              value={form.name}
              onChange={(event) => update('name', event.target.value)}
              maxLength={150}
            />
          </Field>
          <Field label="Category" htmlFor="product-category" required error={fieldErrors.category}>
            <input
              id="product-category"
              value={form.category}
              onChange={(event) => update('category', event.target.value)}
              placeholder="Savings"
              maxLength={50}
            />
          </Field>
          <Field label="Minimum age" htmlFor="product-min-age" required error={fieldErrors.minimumAge}>
            <input
              id="product-min-age"
              type="number"
              min={0}
              max={100}
              value={form.minimumAge}
              onChange={(event) => update('minimumAge', Number(event.target.value))}
            />
          </Field>
          <Field label="Display order" htmlFor="product-order">
            <input
              id="product-order"
              type="number"
              value={form.displayOrder}
              onChange={(event) => update('displayOrder', Number(event.target.value))}
            />
          </Field>
          <Field label="Summary" htmlFor="product-summary" required error={fieldErrors.summary} className="span-2">
            <input
              id="product-summary"
              value={form.summary}
              onChange={(event) => update('summary', event.target.value)}
              maxLength={300}
            />
          </Field>
          <Field label="Description" htmlFor="product-description" required error={fieldErrors.description} className="span-2">
            <textarea
              id="product-description"
              rows={3}
              value={form.description}
              onChange={(event) => update('description', event.target.value)}
              maxLength={2000}
            />
          </Field>
          <Field label="Eligibility" htmlFor="product-eligibility" required error={fieldErrors.eligibility} className="span-2">
            <textarea
              id="product-eligibility"
              rows={2}
              value={form.eligibility}
              onChange={(event) => update('eligibility', event.target.value)}
              maxLength={1000}
            />
          </Field>
          <Field label="Required documents" htmlFor="product-docs" className="span-2" help="Comma separated, e.g. ProofOfIdentity, ProofOfAddress">
            <input
              id="product-docs"
              value={form.requiredDocuments}
              onChange={(event) => update('requiredDocuments', event.target.value)}
            />
          </Field>
          <Field label="Terms" htmlFor="product-terms" required error={fieldErrors.terms} className="span-2">
            <textarea
              id="product-terms"
              rows={3}
              value={form.terms}
              onChange={(event) => update('terms', event.target.value)}
              maxLength={4000}
            />
          </Field>
          <label className="checkbox-card span-2">
            <input
              type="checkbox"
              checked={form.isActive}
              onChange={(event) => update('isActive', event.target.checked)}
            />
            <span>
              <strong>Visible to customers</strong>
              <span className="muted text-xs">
                Active products appear in the customer catalogue.
              </span>
            </span>
          </label>
        </div>
      </Modal>

      <ConfirmDialog
        open={pendingToggle !== null}
        title={pendingToggle?.isActive ? 'Deactivate product' : 'Activate product'}
        message={
          pendingToggle?.isActive
            ? `Deactivate ${pendingToggle?.name}? It will no longer appear for customers, but existing applications are unaffected.`
            : `Activate ${pendingToggle?.name}? It will become available to customers.`
        }
        confirmLabel={pendingToggle?.isActive ? 'Deactivate' : 'Activate'}
        destructive={pendingToggle?.isActive === true}
        busy={busyId === pendingToggle?.id}
        onConfirm={() => void confirmToggle()}
        onCancel={() => setPendingToggle(null)}
      />
    </div>
  );
}
