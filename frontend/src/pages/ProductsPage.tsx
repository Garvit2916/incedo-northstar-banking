import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { applicationsApi, productsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { Card, ErrorBanner } from '../components/ui';
import { SkeletonCard } from '../components/Skeleton';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import type { ProductResponse } from '../api/types';

export function ProductsPage() {
  const navigate = useNavigate();
  const { data, loading, error } = useAsync(() => productsApi.list(), []);
  const [startingId, setStartingId] = useState<string | null>(null);
  const toast = useToast();

  async function startApplication(product: ProductResponse) {
    setStartingId(product.id);
    try {
      const application = await applicationsApi.create(product.id);
      toast.success('Application started.');
      navigate(`/applications/${application.id}`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to start application.');
    } finally {
      setStartingId(null);
    }
  }

  if (error) {
    return <ErrorBanner message={error} />;
  }

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>Product catalogue</h1>
          <p className="muted">Illustrative banking products. All terms are fictitious.</p>
        </div>
      </header>

      <div className="product-grid">
        {loading || !data
          ? Array.from({ length: 4 }).map((_, index) => <SkeletonCard key={index} />)
          : data.map((product) => (
          <Card key={product.id} title={product.name}>
            <p className="muted">{product.category}</p>
            <p>{product.summary}</p>
            <dl className="meta">
              <div>
                <dt>Minimum age</dt>
                <dd>{product.minimumAge}</dd>
              </div>
              <div>
                <dt>Eligibility</dt>
                <dd>{product.eligibility}</dd>
              </div>
              <div>
                <dt>Documents</dt>
                <dd>{product.requiredDocuments.join(', ') || 'None'}</dd>
              </div>
            </dl>
            <button
              type="button"
              className="btn btn-primary"
              disabled={startingId === product.id}
              onClick={() => startApplication(product)}
            >
              {startingId === product.id ? 'Starting…' : 'Start application'}
            </button>
          </Card>
        ))}
      </div>
    </div>
  );
}
