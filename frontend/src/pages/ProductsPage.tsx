import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiError } from '../api/client';
import { applicationsApi, productsApi } from '../api/endpoints';
import { Modal } from '../components/Modal';
import { Icon } from '../components/icons';
import { Skeleton } from '../components/Skeleton';
import { Card, EmptyState, ErrorBanner } from '../components/ui';
import { humanise } from '../lib/format';
import { useAsync } from '../lib/useAsync';
import { useToast } from '../components/Toast';
import type { ProductResponse } from '../api/types';

function Fact({ icon, label, value }: { icon: 'shieldCheck' | 'file'; label: string; value: string }) {
  return (
    <div className="product-fact">
      <span className="product-fact-icon" aria-hidden="true">
        <Icon name={icon} size={15} />
      </span>
      <span>
        <span className="product-fact-label">{label}</span>
        <strong>{value}</strong>
      </span>
    </div>
  );
}

export function ProductsPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const { data, loading, error, reload } = useAsync(() => productsApi.list(), []);

  const [query, setQuery] = useState('');
  const [category, setCategory] = useState<string>('all');
  const [selected, setSelected] = useState<ProductResponse | null>(null);
  const [creatingId, setCreatingId] = useState<string | null>(null);

  const categories = useMemo(() => {
    if (!data) return [];
    return Array.from(new Set(data.map((product) => product.category))).sort();
  }, [data]);

  const filtered = useMemo(() => {
    if (!data) return [];
    const term = query.trim().toLowerCase();
    return data.filter((product) => {
      const matchesCategory = category === 'all' || product.category === category;
      const matchesTerm =
        !term ||
        product.name.toLowerCase().includes(term) ||
        product.summary.toLowerCase().includes(term) ||
        product.category.toLowerCase().includes(term);
      return matchesCategory && matchesTerm;
    });
  }, [data, query, category]);

  async function startApplication(product: ProductResponse) {
    setCreatingId(product.id);
    try {
      const created = await applicationsApi.create(product.id);
      toast.success(`Application ${created.reference} started.`);
      navigate(`/applications/${created.id}`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to start application.');
    } finally {
      setCreatingId(null);
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <p className="eyebrow">Product catalogue</p>
          <h1 className="page-title">Find the right account</h1>
          <p className="page-description">
            Compare savings, deposits and everyday accounts, then start an application — you can save
            your progress at any time.
          </p>
        </div>
        <div className="page-actions">
          <div className="input-group">
            <span className="input-icon" aria-hidden="true">
              <Icon name="search" size={15} />
            </span>
            <input
              className="input"
              type="search"
              placeholder="Search products"
              aria-label="Search products"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
          </div>
        </div>
      </div>

      {!loading && !error && data && data.length > 0 && (
        <div className="filter-bar">
          <div className="chip-row" role="group" aria-label="Filter by category">
            {['all', ...categories].map((value) => (
              <button
                key={value}
                type="button"
                className={`chip ${category === value ? 'chip--active' : ''}`}
                aria-pressed={category === value}
                onClick={() => setCategory(value)}
              >
                {value === 'all' ? 'All products' : humanise(value)}
              </button>
            ))}
          </div>
          <span className="muted text-xs">
            {filtered.length} of {data.length} products
          </span>
        </div>
      )}

      {error ? (
        <ErrorBanner message={error} onRetry={reload} />
      ) : loading ? (
        <div className="product-grid" aria-busy="true" aria-label="Loading products">
          {Array.from({ length: 6 }).map((_, index) => (
            <div className="product-card skeleton-card" key={index}>
              <Skeleton width="35%" height="0.8rem" />
              <Skeleton width="65%" height="1.3rem" />
              <Skeleton height="0.85rem" />
              <Skeleton width="80%" height="0.85rem" />
              <Skeleton width="45%" height="0.85rem" />
            </div>
          ))}
        </div>
      ) : filtered.length === 0 ? (
        <Card>
          <EmptyState
            icon="products"
            title={data && data.length > 0 ? 'No matching products' : 'No products available'}
            message={
              data && data.length > 0
                ? 'Try a different search term or category filter.'
                : 'The catalogue is currently empty. Please check back soon.'
            }
            action={
              data && data.length > 0 ? (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={() => {
                    setQuery('');
                    setCategory('all');
                  }}
                >
                  Clear filters
                </button>
              ) : undefined
            }
          />
        </Card>
      ) : (
        <div className="product-grid">
          {filtered.map((product) => (
            <article className="product-card" key={product.id}>
              <div className="product-card-head">
                <span className="product-cat">{humanise(product.category)}</span>
                <span className="badge badge--neutral">From age {product.minimumAge}</span>
              </div>
              <h2>{product.name}</h2>
              <p className="product-summary">{product.summary}</p>
              <div className="product-facts">
                <Fact icon="file" label="Documents" value={`${product.requiredDocuments.length} required`} />
                <Fact icon="shieldCheck" label="Minimum age" value={`${product.minimumAge}+ years`} />
              </div>
              <div className="product-card-footer">
                <button type="button" className="btn btn-ghost btn-sm" onClick={() => setSelected(product)}>
                  View details
                </button>
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  onClick={() => startApplication(product)}
                  disabled={creatingId !== null}
                >
                  {creatingId === product.id ? (
                    <>
                      <span className="spinner" aria-hidden="true" />
                      Starting…
                    </>
                  ) : (
                    <>
                      Start application
                      <Icon name="arrowRight" size={15} />
                    </>
                  )}
                </button>
              </div>
            </article>
          ))}
        </div>
      )}

      <p className="illustrative-note">
        <Icon name="info" size={15} />
        Rates, limits and terms shown on this demonstration application are illustrative only and do not
        represent a real financial offer.
      </p>

      <Modal
        open={selected !== null}
        title={selected?.name ?? 'Product details'}
        onClose={() => setSelected(null)}
        footer={
          <>
            <button type="button" className="btn btn-ghost" onClick={() => setSelected(null)}>
              Close
            </button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={creatingId !== null || !selected}
              onClick={() => selected && startApplication(selected)}
            >
              {selected && creatingId === selected.id ? (
                <>
                  <span className="spinner" aria-hidden="true" />
                  Starting…
                </>
              ) : (
                <>
                  Start application
                  <Icon name="arrowRight" size={16} />
                </>
              )}
            </button>
          </>
        }
      >
        {selected && (
          <div className="product-detail">
            <div className="product-detail-hero">
              <span className="product-detail-icon" aria-hidden="true">
                <Icon name="wallet" size={22} />
              </span>
              <div>
                <span className="product-cat">{humanise(selected.category)}</span>
                <p>{selected.summary}</p>
              </div>
            </div>

            <section className="product-detail-section">
              <h3>About this product</h3>
              <p>{selected.description}</p>
            </section>

            <section className="product-detail-section">
              <h3>Eligibility</h3>
              <p>{selected.eligibility}</p>
              <p className="muted text-sm">Minimum age: {selected.minimumAge} years.</p>
            </section>

            <section className="product-detail-section">
              <h3>Documents you will need</h3>
              <ul className="doc-requirements">
                {selected.requiredDocuments.map((doc) => (
                  <li key={doc}>
                    <Icon name="checkCircle" size={15} />
                    {humanise(doc)}
                  </li>
                ))}
              </ul>
            </section>

            <section className="product-detail-section">
              <h3>Terms</h3>
              <p>{selected.terms}</p>
            </section>

            <p className="illustrative-note">
              <Icon name="info" size={15} />
              Starting an application creates a draft you can complete at your own pace.
            </p>
          </div>
        )}
      </Modal>
    </div>
  );
}
