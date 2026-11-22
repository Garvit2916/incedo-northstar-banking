import { useState } from 'react';
import type { FormEvent } from 'react';
import { applicationsApi, customerTicketsApi } from '../api/endpoints';
import type { TicketCategory, TicketPriority } from '../api/types';
import { Field } from '../components/forms';
import { Icon } from '../components/icons';
import { SkeletonCard, SkeletonTable } from '../components/Skeleton';
import { Badge, Card, EmptyState, ErrorBanner, PageHeader, StatusBadge } from '../components/ui';
import { useToast } from '../components/Toast';
import { formatDate, formatRelative, humanise } from '../lib/format';
import { useAsync } from '../lib/useAsync';

const CATEGORIES: TicketCategory[] = [
  'AccountAccess',
  'ApplicationDelay',
  'MissingDocuments',
  'StatusEnquiry',
  'NotificationIssue',
  'CardTransactionDispute',
  'UnauthorizedTransaction',
  'CardBlockingReplacement',
  'CreditCardBilling',
  'FailedUpiTransaction',
  'BankTransferIssue',
  'KycVerification',
  'PersonalLoan',
  'FixedDeposit',
  'ProductInquiry',
  'Other',
];

const PRIORITIES: TicketPriority[] = ['Low', 'Normal', 'High', 'Urgent'];
const TICKET_PAGE_SIZE = 50;

export function CustomerTicketsPage() {
  const toast = useToast();
  const [page, setPage] = useState(1);
  const list = useAsync(() => customerTicketsApi.list(page, TICKET_PAGE_SIZE), [page]);
  const applications = useAsync(() => applicationsApi.listMine(1, 100), []);

  const [subject, setSubject] = useState('');
  const [description, setDescription] = useState('');
  const [category, setCategory] = useState<TicketCategory>('StatusEnquiry');
  const [priority, setPriority] = useState<TicketPriority>('Normal');
  const [relatedApplicationId, setRelatedApplicationId] = useState('');
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const detail = useAsync(
    () => (selectedId ? customerTicketsApi.get(selectedId) : Promise.resolve(null)),
    [selectedId],
  );

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!subject.trim() || !description.trim()) {
      setFormError('Add both a subject and a description before submitting.');
      return;
    }

    setFormError(null);
    setSubmitting(true);
    try {
      const created = await customerTicketsApi.create({
        subject: subject.trim(),
        description: description.trim(),
        category,
        priority,
        relatedApplicationId: relatedApplicationId || null,
      });

      toast.success(`Ticket ${created.reference} submitted. Our support team will review it.`);
      setSubject('');
      setDescription('');
      setCategory('StatusEnquiry');
      setPriority('Normal');
      setRelatedApplicationId('');
      setSelectedId(created.id);
      if (page === 1) {
        list.reload();
      } else {
        setPage(1);
      }
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'Unable to submit your ticket.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="page">
      <PageHeader
        eyebrow="Support"
        title="My tickets"
        description="Raise a support request and follow its status. Never include passwords, OTPs, PINs or full card numbers."
      />

      <div className="customer-ticket-layout">
        <Card
          title="Raise a ticket"
          subtitle="Your details are attached automatically from your signed-in account."
        >
          <form className="ticket-form" onSubmit={handleSubmit}>
            {formError && <ErrorBanner message={formError} />}

            <Field label="Subject" htmlFor="customer-ticket-subject" required>
              <input
                id="customer-ticket-subject"
                className="input"
                value={subject}
                maxLength={200}
                placeholder="Briefly describe the issue"
                onChange={(event) => setSubject(event.target.value)}
              />
            </Field>

            <Field
              label="Description"
              htmlFor="customer-ticket-description"
              required
              help="Include relevant dates, references and what you have already tried."
            >
              <textarea
                id="customer-ticket-description"
                className="input"
                rows={6}
                value={description}
                maxLength={4000}
                placeholder="Tell us what happened"
                onChange={(event) => setDescription(event.target.value)}
              />
            </Field>

            <div className="ticket-form-grid">
              <Field label="Category" htmlFor="customer-ticket-category" required>
                <select
                  id="customer-ticket-category"
                  className="input"
                  value={category}
                  onChange={(event) => setCategory(event.target.value as TicketCategory)}
                >
                  {CATEGORIES.map((value) => (
                    <option key={value} value={value}>
                      {humanise(value)}
                    </option>
                  ))}
                </select>
              </Field>

              <Field label="Priority" htmlFor="customer-ticket-priority" required>
                <select
                  id="customer-ticket-priority"
                  className="input"
                  value={priority}
                  onChange={(event) => setPriority(event.target.value as TicketPriority)}
                >
                  {PRIORITIES.map((value) => (
                    <option key={value} value={value}>
                      {humanise(value)}
                    </option>
                  ))}
                </select>
              </Field>
            </div>

            <Field
              label="Related application"
              htmlFor="customer-ticket-application"
              help={
                applications.error ? (
                  <>
                    Could not load your applications.{' '}
                    <button type="button" className="link-button" onClick={applications.reload}>
                      Retry
                    </button>
                  </>
                ) : (
                  'Optional. Only your own applications are available.'
                )
              }
            >
              <select
                id="customer-ticket-application"
                className="input"
                value={relatedApplicationId}
                onChange={(event) => setRelatedApplicationId(event.target.value)}
                disabled={applications.loading}
              >
                <option value="">None — general enquiry</option>
                {applications.data?.items.map((application) => (
                  <option key={application.id} value={application.id}>
                    {application.reference} · {application.productName}
                  </option>
                ))}
              </select>
            </Field>

            <div className="ticket-form-actions">
              <button type="submit" className="btn btn-primary" disabled={submitting}>
                <Icon name={submitting ? 'loader' : 'send'} size={16} />
                {submitting ? 'Submitting…' : 'Submit ticket'}
              </button>
            </div>
          </form>
        </Card>

        <div className="ticket-tracking">
          <Card
            title="Your tickets"
            subtitle={list.data ? `${list.data.totalCount} submitted ticket(s)` : 'Loading your tickets…'}
            footer={
              list.data && list.data.totalCount > TICKET_PAGE_SIZE ? (
                <div className="pagination">
                  <button
                    type="button"
                    className="btn btn-ghost btn-sm"
                    onClick={() => setPage((current) => Math.max(1, current - 1))}
                    disabled={page === 1}
                  >
                    Previous
                  </button>
                  <span className="pagination-info">
                    Page {page} of {Math.ceil(list.data.totalCount / TICKET_PAGE_SIZE)}
                  </span>
                  <button
                    type="button"
                    className="btn btn-ghost btn-sm"
                    onClick={() => setPage((current) => current + 1)}
                    disabled={page * TICKET_PAGE_SIZE >= list.data.totalCount}
                  >
                    Next
                  </button>
                </div>
              ) : undefined
            }
            flush
          >
            {list.error ? (
              <ErrorBanner message={list.error} onRetry={list.reload} />
            ) : list.loading ? (
              <SkeletonTable rows={3} />
            ) : !list.data || list.data.items.length === 0 ? (
              <EmptyState
                icon="support"
                title="No tickets yet"
                message="When you submit a support request it will appear here with its current status."
              />
            ) : (
              <div className="my-ticket-list">
                {list.data.items.map((ticket) => (
                  <button
                    key={ticket.id}
                    type="button"
                    className={ticket.id === selectedId ? 'my-ticket-row active' : 'my-ticket-row'}
                    aria-pressed={ticket.id === selectedId}
                    onClick={() => setSelectedId(ticket.id)}
                  >
                    <span className="my-ticket-main">
                      <span className="app-row-ref">
                        <Icon name="support" size={13} />
                        {ticket.reference}
                      </span>
                      <span className="app-row-title">{ticket.subject}</span>
                      <span className="app-row-meta">
                        <span>Updated {formatRelative(ticket.updatedAtUtc)}</span>
                        <span>{humanise(ticket.category)}</span>
                      </span>
                    </span>
                    <span className="my-ticket-side">
                      <StatusBadge value={ticket.status} />
                      <Badge value={ticket.priority} dot={false} />
                      <Icon name="chevronRight" size={16} />
                    </span>
                  </button>
                ))}
              </div>
            )}
          </Card>

          <Card title="Ticket detail" subtitle="Public conversation and current status">
            {!selectedId ? (
              <EmptyState
                icon="message"
                title="Select a ticket"
                message="Choose one of your tickets to see the public conversation and current status."
              />
            ) : detail.loading ? (
              <SkeletonCard />
            ) : detail.error || !detail.data ? (
              <ErrorBanner message={detail.error ?? 'Ticket not found.'} onRetry={detail.reload} />
            ) : (
              <div className="ticket-detail">
                <div className="ticket-detail-head">
                  <span className="app-row-ref">
                    <Icon name="file" size={13} />
                    {detail.data.reference}
                  </span>
                  <div className="ticket-detail-badges">
                    <StatusBadge value={detail.data.status} />
                    <Badge value={detail.data.priority} dot={false} />
                  </div>
                </div>
                <h3>{detail.data.subject}</h3>
                <p className="ticket-detail-meta muted text-xs">
                  Opened {formatDate(detail.data.createdAtUtc)} · Updated{' '}
                  {formatRelative(detail.data.updatedAtUtc)}
                  {detail.data.slaDueAtUtc ? ` · SLA ${formatRelative(detail.data.slaDueAtUtc)}` : ''}
                </p>
                <p className="ticket-description">{detail.data.description}</p>

                <div className="ticket-thread">
                  {detail.data.messages.map((message) => (
                    <div
                      key={message.id}
                      className={message.authorType === 'Customer' ? 'ticket-message own' : 'ticket-message'}
                    >
                      <div className="ticket-message-meta">
                        {message.authorType === 'Customer' ? 'You' : 'Northstar support'} ·{' '}
                        {formatRelative(message.createdAtUtc)}
                      </div>
                      <p>{message.body}</p>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </Card>
        </div>
      </div>
    </div>
  );
}
