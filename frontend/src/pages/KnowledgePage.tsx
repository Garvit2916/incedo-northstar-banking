import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { knowledgeApi } from '../api/endpoints';
import { SearchInput } from '../components/forms';
import { Icon } from '../components/icons';
import { Badge, Card, EmptyState, ErrorBanner, Spinner, StatusBadge } from '../components/ui';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { formatDate, humanise } from '../lib/format';
import type { KnowledgeArticle, PagedResult } from '../api/types';

export function KnowledgePage() {
  const toast = useToast();
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      setSearch(searchInput.trim());
    }, 300);
    return () => window.clearTimeout(timeout);
  }, [searchInput]);

  const categories = useAsync(() => knowledgeApi.categories(), []);
  const articles = useAsync<PagedResult<KnowledgeArticle>>(
    () => knowledgeApi.search({ search, category, page: 1, pageSize: 50 }),
    [search, category],
  );
  const selected = useAsync<KnowledgeArticle | null>(
    () => (selectedId ? knowledgeApi.get(selectedId) : Promise.resolve(null)),
    [selectedId],
  );

  useEffect(() => {
    if (!selectedId && articles.data && articles.data.items.length > 0) {
      setSelectedId(articles.data.items[0].id);
    }
  }, [articles.data, selectedId]);

  const article = selected.data;

  async function copy(label: string, text: string) {
    try {
      await navigator.clipboard.writeText(text);
      toast.success(`${label} copied.`);
    } catch {
      toast.error('Unable to copy to the clipboard.');
    }
  }

  return (
    <div className="page knowledge-page">
      <section className="page-heading">
        <div>
          <p className="eyebrow">Support enablement</p>
          <h1>Knowledge base</h1>
          <p className="muted text-sm">
            Approved demonstration articles used to ground Copilot summaries, suggested replies and
            next best actions. Read-only in this prototype.
          </p>
        </div>
        <Link to="/support" className="btn btn-secondary">
          <Icon name="support" size={16} />
          Open support workspace
        </Link>
      </section>

      <div className="knowledge-toolbar">
        <SearchInput
          id="knowledge-search"
          value={searchInput}
          onChange={setSearchInput}
          placeholder="Search title, tags, keywords or guidance…"
          label="Search knowledge articles"
        />
        <label className="field field-inline">
          <span>Category</span>
          <select value={category} onChange={(event) => setCategory(event.target.value)}>
            <option value="">All categories</option>
            {(categories.data ?? []).map((item) => (
              <option key={item} value={item}>
                {item}
              </option>
            ))}
          </select>
        </label>
        <button type="button" className="btn btn-ghost btn-sm" onClick={() => { setSearchInput(''); setCategory(''); }}>
          <Icon name="refresh" size={14} />
          Reset
        </button>
      </div>

      <div className="knowledge-layout">
        <Card
          title="Approved articles"
          subtitle={articles.data ? `${articles.data.totalCount} result(s)` : 'Search the catalogue'}
          flush
          className="knowledge-list-card"
        >
          {articles.loading ? (
            <div className="support-list-state">
              <Spinner label="Loading articles…" />
            </div>
          ) : articles.error ? (
            <ErrorBanner message={articles.error} onRetry={articles.reload} />
          ) : articles.data && articles.data.items.length === 0 ? (
            <EmptyState icon="file" message="No articles match this search." />
          ) : (
            <ul className="knowledge-list">
              {articles.data?.items.map((item) => (
                <li key={item.id}>
                  <button
                    type="button"
                    className={item.id === selectedId ? 'knowledge-item active' : 'knowledge-item'}
                    onClick={() => setSelectedId(item.id)}
                    aria-current={item.id === selectedId ? 'true' : undefined}
                  >
                    <span className="knowledge-item-top">
                      <span className="kb-ref-code">{item.articleCode}</span>
                      <StatusBadge value={item.priority} dot={false} />
                    </span>
                    <strong>{item.title}</strong>
                    <span className="muted text-xs">
                      {item.category} · {item.applicableProduct}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card
          title={article ? `${article.articleCode} · ${article.title}` : 'Article detail'}
          subtitle={article ? `Last updated ${formatDate(article.updatedAtUtc)}` : 'Select an article to read its guidance'}
          actions={
            article && (
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => void copy('Article', `${article.title}\n\n${article.content}`)}
              >
                <Icon name="copy" size={14} />
                Copy article
              </button>
            )
          }
          className="knowledge-detail-card"
        >
          {selected.loading ? (
            <Spinner label="Loading article…" />
          ) : selected.error ? (
            <ErrorBanner message={selected.error} onRetry={selected.reload} />
          ) : !article ? (
            <EmptyState icon="file" message="Choose an article from the list." />
          ) : (
            <div className="knowledge-detail">
              <div className="chip-row">
                <Badge value={article.category} icon="file" />
                <Badge value={article.issueType} />
                <Badge value={article.applicableProduct} />
                {article.tags.map((tag) => (
                  <span className="chip" key={tag}>
                    {tag}
                  </span>
                ))}
              </div>

              <section>
                <h3>Article content</h3>
                <p>{article.content}</p>
              </section>

              <section>
                <h3>Resolution steps</h3>
                <ol className="knowledge-steps">
                  {article.resolutionSteps.map((step, index) => (
                    <li key={index}>{step}</li>
                  ))}
                </ol>
              </section>

              <div className="knowledge-guidance-grid">
                <section>
                  <h3>Customer explanation</h3>
                  <p>{article.customerExplanation}</p>
                  <button
                    type="button"
                    className="link-button text-xs"
                    onClick={() => void copy('Customer explanation', article.customerExplanation)}
                  >
                    <Icon name="copy" size={12} /> Copy
                  </button>
                </section>
                <section>
                  <h3>Agent guidance</h3>
                  <p>{article.agentGuidance}</p>
                  <button
                    type="button"
                    className="link-button text-xs"
                    onClick={() => void copy('Agent guidance', article.agentGuidance)}
                  >
                    <Icon name="copy" size={12} /> Copy
                  </button>
                </section>
              </div>

              <section className="knowledge-escalation">
                <h3>
                  <Icon name="alertTriangle" size={15} /> Escalation criteria
                </h3>
                <p>{article.escalationCriteria || 'No specific escalation criteria recorded.'}</p>
              </section>

              <p className="muted text-2xs">
                {humanise(article.issueType)} · {article.articleCode} · Demonstration content only
              </p>
            </div>
          )}
        </Card>
      </div>
    </div>
  );
}
