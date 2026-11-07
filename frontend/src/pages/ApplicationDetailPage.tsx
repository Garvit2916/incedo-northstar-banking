import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { applicationsApi, customersApi, documentsApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { Field } from '../components/forms';
import { Icon } from '../components/icons';
import type { IconName } from '../components/icons';
import { Spinner } from '../components/ui';
import {
  Card,
  EmptyState,
  ErrorBanner,
  InfoBanner,
  ProgressBar,
  StatusBadge,
  Timeline,
} from '../components/ui';
import type { TimelineEntry } from '../components/ui';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { ACCEPT_UPLOAD_TYPES, validateUpload } from '../lib/uploads';
import { formatBytes, formatDate, humanise } from '../lib/format';
import type {
  DocumentResponse,
  KycDocumentType,
  ProfileResponse,
} from '../api/types';

const DOCUMENT_TYPES: KycDocumentType[] = [
  'ProofOfIdentity',
  'ProofOfAddress',
  'ProofOfIncome',
  'Photograph',
  'Other',
];

const EMPLOYMENT_STATUSES = ['Employed', 'Self-employed', 'Retired', 'Student', 'Unemployed', 'Other'];
const INCOME_BANDS = [
  'Under 25,000',
  '25,000 – 49,999',
  '50,000 – 99,999',
  '100,000 – 149,999',
  '150,000 or more',
];
const PURPOSES = [
  'Everyday banking',
  'Savings',
  'Home purchase',
  'Business',
  'Investment',
  'Other',
];

const EDITABLE_STATUSES = ['Draft', 'AdditionalInformationRequired'];
const WITHDRAWABLE_STATUSES = ['Draft', 'Submitted', 'AdditionalInformationRequired'];

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

type StepKey = 'personal' | 'address' | 'employment' | 'documents' | 'review';

interface StepDefinition {
  key: StepKey;
  title: string;
  short: string;
  description: string;
  icon: IconName;
}

const STEPS: StepDefinition[] = [
  {
    key: 'personal',
    title: 'Personal details',
    short: 'Personal',
    description: 'Tell us who you are. We use this to verify your identity.',
    icon: 'profile',
  },
  {
    key: 'address',
    title: 'Address',
    short: 'Address',
    description: 'Where should we send correspondence about this account?',
    icon: 'mapPin',
  },
  {
    key: 'employment',
    title: 'Employment & income',
    short: 'Employment',
    description: 'This helps us understand your circumstances and eligibility.',
    icon: 'briefcase',
  },
  {
    key: 'documents',
    title: 'Documents',
    short: 'Documents',
    description: 'Upload the documents needed for your KYC checks.',
    icon: 'file',
  },
  {
    key: 'review',
    title: 'Review & submit',
    short: 'Review',
    description: 'Check everything looks right before you submit.',
    icon: 'checkCircle',
  },
];

interface AnswerField {
  key: string;
  label: string;
}

const SECTIONS: { title: string; step: StepKey; fields: AnswerField[] }[] = [
  {
    title: 'Personal details',
    step: 'personal',
    fields: [
      { key: 'firstName', label: 'First name' },
      { key: 'lastName', label: 'Last name' },
      { key: 'dateOfBirth', label: 'Date of birth' },
      { key: 'email', label: 'Email' },
      { key: 'phoneNumber', label: 'Phone number' },
    ],
  },
  {
    title: 'Address',
    step: 'address',
    fields: [
      { key: 'addressLine1', label: 'Address line 1' },
      { key: 'addressLine2', label: 'Address line 2' },
      { key: 'city', label: 'City' },
      { key: 'state', label: 'County / state' },
      { key: 'postalCode', label: 'Postal code' },
      { key: 'country', label: 'Country' },
    ],
  },
  {
    title: 'Employment & income',
    step: 'employment',
    fields: [
      { key: 'employmentStatus', label: 'Employment status' },
      { key: 'occupation', label: 'Occupation' },
      { key: 'employerName', label: 'Employer' },
      { key: 'annualIncomeBand', label: 'Annual income' },
      { key: 'purpose', label: 'Purpose of account' },
    ],
  },
];

const KNOWN_KEYS = new Set(SECTIONS.flatMap((section) => section.fields.map((field) => field.key)));

function profileFallback(profile: ProfileResponse | null): Record<string, string> {
  if (!profile) return {};
  const map: Record<string, string> = {
    firstName: profile.firstName,
    lastName: profile.lastName,
    dateOfBirth: profile.dateOfBirth ?? '',
    email: profile.email,
    phoneNumber: profile.phoneNumber ?? '',
    addressLine1: profile.addressLine1 ?? '',
    addressLine2: profile.addressLine2 ?? '',
    city: profile.city ?? '',
    state: profile.state ?? '',
    postalCode: profile.postalCode ?? '',
    country: profile.country ?? '',
    occupation: profile.occupation ?? '',
    annualIncomeBand: profile.annualIncomeBand ?? '',
  };
  return Object.fromEntries(Object.entries(map).filter(([, value]) => Boolean(value)));
}

export function ApplicationDetailPage() {
  const { id = '' } = useParams();
  const { hasRole } = useAuth();
  const toast = useToast();

  const { data, loading, error, reload } = useAsync(() => applicationsApi.getMine(id), [id]);
  const profile = useAsync<ProfileResponse | null>(
    () => (hasRole('Customer') ? customersApi.me() : Promise.resolve(null)),
    [hasRole],
  );

  const [values, setValues] = useState<Record<string, string>>({});
  const [documents, setDocuments] = useState<DocumentResponse[]>([]);
  const [step, setStep] = useState(0);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [declaration, setDeclaration] = useState(false);
  const [saving, setSaving] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [confirmWithdraw, setConfirmWithdraw] = useState(false);
  const [withdrawing, setWithdrawing] = useState(false);
  const [lastSaved, setLastSaved] = useState<Date | null>(null);

  const [uploadType, setUploadType] = useState<KycDocumentType>('ProofOfIdentity');
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const initialisedFor = useRef<string | null>(null);

  useEffect(() => {
    if (!data || profile.loading || initialisedFor.current === data.id) return;

    const merged: Record<string, string> = { ...data.answers };
    const fallback = profileFallback(profile.data);
    for (const [key, value] of Object.entries(fallback)) {
      if (!merged[key]) merged[key] = value;
    }

    setValues(merged);
    setDocuments(data.documents);
    initialisedFor.current = data.id;
  }, [data, profile.loading, profile.data]);

  const editable = data ? EDITABLE_STATUSES.includes(data.status) : false;
  const currentStep = STEPS[step];

  const extraAnswers = useMemo(() => {
    if (!data) return [];
    return Object.entries(data.answers).filter(([key]) => !KNOWN_KEYS.has(key));
  }, [data]);

  function update(key: string, value: string) {
    setValues((current) => ({ ...current, [key]: value }));
    setErrors((current) => {
      if (!current[key]) return current;
      const next = { ...current };
      delete next[key];
      return next;
    });
  }

  function computeStepErrors(index: number): Record<string, string> {
    const next: Record<string, string> = {};
    const required = (key: string, label: string) => {
      if (!(values[key] ?? '').trim()) next[key] = `${label} is required.`;
    };

    if (index === 0) {
      required('firstName', 'First name');
      required('lastName', 'Last name');
      required('dateOfBirth', 'Date of birth');
      required('email', 'Email');
      required('phoneNumber', 'Phone number');
      if (values.email && !EMAIL_PATTERN.test(values.email.trim())) {
        next.email = 'Enter a valid email address.';
      }
      if (values.dateOfBirth && new Date(values.dateOfBirth) > new Date()) {
        next.dateOfBirth = 'Date of birth cannot be in the future.';
      }
    }

    if (index === 1) {
      required('addressLine1', 'Address line 1');
      required('city', 'City');
      required('postalCode', 'Postal code');
      required('country', 'Country');
    }

    if (index === 2) {
      required('employmentStatus', 'Employment status');
      required('occupation', 'Occupation');
      required('annualIncomeBand', 'Annual income band');
      required('purpose', 'Purpose of account');
    }

    if (index === 4 && !declaration) {
      next.declaration = 'Please confirm the declaration before submitting.';
    }

    return next;
  }

  function validateStep(index: number): boolean {
    const stepErrors = computeStepErrors(index);
    setErrors(stepErrors);
    return Object.keys(stepErrors).length === 0;
  }

  function validateAll(): number | null {
    let all: Record<string, string> = {};
    let firstInvalid: number | null = null;
    for (let index = 0; index < STEPS.length; index += 1) {
      const stepErrors = computeStepErrors(index);
      if (Object.keys(stepErrors).length > 0 && firstInvalid === null) firstInvalid = index;
      all = { ...all, ...stepErrors };
    }
    setErrors(all);
    return firstInvalid;
  }

  function goNext() {
    if (!validateStep(step)) return;
    setStep((current) => Math.min(current + 1, STEPS.length - 1));
  }

  function goBack() {
    setStep((current) => Math.max(current - 1, 0));
  }

  async function saveDraft() {
    if (!data) return;
    setSaving(true);
    try {
      await applicationsApi.update(data.id, values);
      setLastSaved(new Date());
      toast.success('Draft saved.');
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to save draft.');
    } finally {
      setSaving(false);
    }
  }

  async function submitApplication() {
    if (!data) return;
    const firstInvalid = validateAll();
    if (firstInvalid !== null) {
      setStep(firstInvalid);
      toast.error('Please complete the highlighted fields before submitting.');
      return;
    }

    if (profile.data && !profile.data.isComplete) {
      toast.error('Complete your profile before submitting your application.');
      return;
    }

    setSubmitting(true);
    try {
      await applicationsApi.update(data.id, values);
      await applicationsApi.submit(data.id);
      toast.success('Application submitted for review.');
      initialisedFor.current = null;
      reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to submit application.');
    } finally {
      setSubmitting(false);
    }
  }

  async function withdrawApplication() {
    if (!data) return;
    setWithdrawing(true);
    try {
      await applicationsApi.withdraw(data.id);
      toast.success('Application withdrawn.');
      setConfirmWithdraw(false);
      initialisedFor.current = null;
      reload();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to withdraw application.');
    } finally {
      setWithdrawing(false);
    }
  }

  async function uploadDocument() {
    if (!data || !file) return;
    const problem = validateUpload(file);
    if (problem) {
      toast.error(problem);
      return;
    }

    setUploading(true);
    try {
      const uploaded = await documentsApi.upload(data.id, uploadType, file);
      setDocuments((current) => [...current, uploaded]);
      setFile(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
      toast.success('Document uploaded.');
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to upload document.');
    } finally {
      setUploading(false);
    }
  }

  async function openDocument(document: DocumentResponse) {
    try {
      await documentsApi.open(document.id);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to open document.');
    }
  }

  async function copyReference() {
    if (!data) return;
    try {
      await navigator.clipboard.writeText(data.reference);
      toast.info('Application reference copied.');
    } catch {
      toast.error('Unable to copy the reference.');
    }
  }

  function pickFile(next: File | null) {
    setFile(next);
  }

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
        <Link to="/applications" className="btn btn-secondary section-gap">
          Back to applications
        </Link>
      </div>
    );
  }

  const rail = Array.from(new Set(['Draft', ...data.history.map((entry) => entry.toStatus), data.status]));

  const historyItems: TimelineEntry[] = [...data.history]
    .reverse()
    .map((entry) => ({
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

  const documentList = (
    <>
      {documents.length === 0 ? (
        <EmptyState
          icon="upload"
          title="No documents yet"
          message={
            editable
              ? 'Upload the documents required for your application. You can add more at any time before submission.'
              : 'No documents were uploaded with this application.'
          }
        />
      ) : (
        <div className="doc-list">
          {documents.map((document) => (
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
              </div>
            </div>
          ))}
        </div>
      )}
    </>
  );

  return (
    <div className="page">
      <header className="detail-hero">
        <div className="detail-hero-main">
          <p className="eyebrow">Application</p>
          <div className="detail-reference">
            <h1>{data.reference}</h1>
            <button
              type="button"
              className="icon-btn"
              onClick={() => void copyReference()}
              aria-label="Copy application reference"
              title="Copy reference"
            >
              <Icon name="copy" size={15} />
            </button>
            <StatusBadge value={data.status} />
          </div>
          <p className="muted text-sm">
            {data.productName} · Started {formatDate(data.createdAtUtc)}
            {data.submittedAtUtc ? ` · Submitted ${formatDate(data.submittedAtUtc)}` : ''}
            {data.decidedAtUtc ? ` · Decided ${formatDate(data.decidedAtUtc)}` : ''}
          </p>
        </div>
        <div className="detail-hero-actions">
          {editable && (
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => void saveDraft()}
              disabled={saving || submitting}
            >
              {saving ? 'Saving…' : 'Save draft'}
            </button>
          )}
          {WITHDRAWABLE_STATUSES.includes(data.status) && (
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => setConfirmWithdraw(true)}
              disabled={submitting || withdrawing}
            >
              Withdraw
            </button>
          )}
        </div>
      </header>

      <div className="status-rail">
        <div className="status-rail-steps">
          {rail.map((status, index) => (
            <span
              key={`${status}-${index}`}
              className={`status-chip ${index < rail.length - 1 ? 'done' : 'current'}`}
            >
              {index < rail.length - 1 && <Icon name="check" size={13} />}
              {humanise(status)}
            </span>
          ))}
        </div>
      </div>

      {data.decisionReason && (
        <InfoBanner
          tone={data.status === 'AdditionalInformationRequired' ? 'warning' : 'info'}
          title={
            data.status === 'AdditionalInformationRequired'
              ? 'More information needed'
              : `Decision: ${humanise(data.status)}`
          }
          message={data.decisionReason}
        />
      )}

      {editable && profile.data && !profile.data.isComplete && (
        <InfoBanner
          tone="warning"
          title="Your profile is incomplete"
          message={
            <>
              Complete your profile before submitting this application.{' '}
              <Link to="/profile" className="link-button">
                Update profile
              </Link>
            </>
          }
        />
      )}

      {editable ? (
        <div className="wizard">
          <div>
            <div className="wizard-progress-head">
              <h2>{currentStep.title}</h2>
              <span className="muted text-xs">
                Step {step + 1} of {STEPS.length}
                {lastSaved ? ` · Saved at ${lastSaved.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}` : ''}
              </span>
            </div>
            <ProgressBar value={((step + 1) / STEPS.length) * 100} label="Application progress" showValue={false} />
          </div>

          <ol className="wizard-steps">
            {STEPS.map((definition, index) => (
              <li key={definition.key} style={{ display: 'contents' }}>
                <button
                  type="button"
                  className={`wizard-step ${index === step ? 'active' : ''} ${index < step ? 'complete' : ''}`}
                  aria-current={index === step ? 'step' : undefined}
                  onClick={() => setStep(index)}
                >
                  <span className="wizard-step-top">
                    <span className="wizard-step-marker">
                      {index < step ? <Icon name="check" size={13} /> : index + 1}
                    </span>
                    <span className="wizard-step-label">{definition.short}</span>
                  </span>
                  <span className="wizard-step-bar" />
                </button>
              </li>
            ))}
          </ol>

          <div className="card wizard-card">
            <div className="wizard-section-head">
              <h3>
                <Icon name={currentStep.icon} size={18} /> {currentStep.title}
              </h3>
              <p>{currentStep.description}</p>
            </div>

            {step === 0 && (
              <div className="form-grid">
                <Field label="First name" htmlFor="app-firstName" required error={errors.firstName}>
                  <input
                    id="app-firstName"
                    value={values.firstName ?? ''}
                    onChange={(event) => update('firstName', event.target.value)}
                    autoComplete="given-name"
                  />
                </Field>
                <Field label="Last name" htmlFor="app-lastName" required error={errors.lastName}>
                  <input
                    id="app-lastName"
                    value={values.lastName ?? ''}
                    onChange={(event) => update('lastName', event.target.value)}
                    autoComplete="family-name"
                  />
                </Field>
                <Field label="Date of birth" htmlFor="app-dob" required error={errors.dateOfBirth}>
                  <input
                    id="app-dob"
                    type="date"
                    value={values.dateOfBirth ?? ''}
                    onChange={(event) => update('dateOfBirth', event.target.value)}
                    max={new Date().toISOString().slice(0, 10)}
                  />
                </Field>
                <Field label="Phone number" htmlFor="app-phone" required error={errors.phoneNumber}>
                  <input
                    id="app-phone"
                    value={values.phoneNumber ?? ''}
                    onChange={(event) => update('phoneNumber', event.target.value)}
                    autoComplete="tel"
                  />
                </Field>
                <Field label="Email" htmlFor="app-email" required error={errors.email} className="span-2">
                  <input
                    id="app-email"
                    type="email"
                    value={values.email ?? ''}
                    onChange={(event) => update('email', event.target.value)}
                    autoComplete="email"
                  />
                </Field>
              </div>
            )}

            {step === 1 && (
              <div className="form-grid">
                <Field label="Address line 1" htmlFor="app-address1" required error={errors.addressLine1} className="span-2">
                  <input
                    id="app-address1"
                    value={values.addressLine1 ?? ''}
                    onChange={(event) => update('addressLine1', event.target.value)}
                    autoComplete="address-line1"
                  />
                </Field>
                <Field label="Address line 2" htmlFor="app-address2" className="span-2">
                  <input
                    id="app-address2"
                    value={values.addressLine2 ?? ''}
                    onChange={(event) => update('addressLine2', event.target.value)}
                    autoComplete="address-line2"
                  />
                </Field>
                <Field label="City" htmlFor="app-city" required error={errors.city}>
                  <input
                    id="app-city"
                    value={values.city ?? ''}
                    onChange={(event) => update('city', event.target.value)}
                    autoComplete="address-level2"
                  />
                </Field>
                <Field label="County / state" htmlFor="app-state">
                  <input
                    id="app-state"
                    value={values.state ?? ''}
                    onChange={(event) => update('state', event.target.value)}
                    autoComplete="address-level1"
                  />
                </Field>
                <Field label="Postal code" htmlFor="app-postal" required error={errors.postalCode}>
                  <input
                    id="app-postal"
                    value={values.postalCode ?? ''}
                    onChange={(event) => update('postalCode', event.target.value)}
                    autoComplete="postal-code"
                  />
                </Field>
                <Field label="Country" htmlFor="app-country" required error={errors.country}>
                  <input
                    id="app-country"
                    value={values.country ?? ''}
                    onChange={(event) => update('country', event.target.value)}
                    autoComplete="country-name"
                  />
                </Field>
              </div>
            )}

            {step === 2 && (
              <div className="form-grid">
                <Field label="Employment status" htmlFor="app-employment" required error={errors.employmentStatus}>
                  <select
                    id="app-employment"
                    value={values.employmentStatus ?? ''}
                    onChange={(event) => update('employmentStatus', event.target.value)}
                  >
                    <option value="">Select…</option>
                    {EMPLOYMENT_STATUSES.map((status) => (
                      <option key={status} value={status}>
                        {status}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Occupation" htmlFor="app-occupation" required error={errors.occupation}>
                  <input
                    id="app-occupation"
                    value={values.occupation ?? ''}
                    onChange={(event) => update('occupation', event.target.value)}
                  />
                </Field>
                <Field label="Employer" htmlFor="app-employer" className="span-2">
                  <input
                    id="app-employer"
                    value={values.employerName ?? ''}
                    onChange={(event) => update('employerName', event.target.value)}
                  />
                </Field>
                <Field label="Annual income" htmlFor="app-income" required error={errors.annualIncomeBand}>
                  <select
                    id="app-income"
                    value={values.annualIncomeBand ?? ''}
                    onChange={(event) => update('annualIncomeBand', event.target.value)}
                  >
                    <option value="">Select…</option>
                    {INCOME_BANDS.map((band) => (
                      <option key={band} value={band}>
                        {band}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Purpose of account" htmlFor="app-purpose" required error={errors.purpose}>
                  <select
                    id="app-purpose"
                    value={values.purpose ?? ''}
                    onChange={(event) => update('purpose', event.target.value)}
                  >
                    <option value="">Select…</option>
                    {PURPOSES.map((purpose) => (
                      <option key={purpose} value={purpose}>
                        {purpose}
                      </option>
                    ))}
                  </select>
                </Field>
              </div>
            )}

            {step === 3 && (
              <div className="stack-lg">
                <InfoBanner
                  tone="info"
                  title="Accepted formats"
                  message="PDF, JPG or PNG files up to 5 MB. Make sure documents are clear, in date and show all four corners."
                />
                {documentList}
                <div>
                  <label
                    className={`dropzone ${dragging ? 'dragging' : ''}`}
                    onDragOver={(event) => {
                      event.preventDefault();
                      setDragging(true);
                    }}
                    onDragLeave={() => setDragging(false)}
                    onDrop={(event) => {
                      event.preventDefault();
                      setDragging(false);
                      pickFile(event.dataTransfer.files?.[0] ?? null);
                    }}
                    onClick={() => fileInputRef.current?.click()}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        fileInputRef.current?.click();
                      }
                    }}
                    role="button"
                    tabIndex={0}
                  >
                    <span className="dropzone-icon" aria-hidden="true">
                      <Icon name="upload" size={22} />
                    </span>
                    <strong>{file ? file.name : 'Drag & drop a document, or browse'}</strong>
                    <span className="muted text-xs">
                      {file
                        ? `${formatBytes(file.size)} selected — choose a type and upload below.`
                        : 'PDF, JPG or PNG · maximum 5 MB'}
                    </span>
                    <input
                      ref={fileInputRef}
                      type="file"
                      className="sr-only"
                      accept={ACCEPT_UPLOAD_TYPES}
                      onChange={(event) => pickFile(event.target.files?.[0] ?? null)}
                    />
                  </label>

                  <div className="upload-row section-gap">
                    <Field label="Document type" htmlFor="app-doctype">
                      <select
                        id="app-doctype"
                        value={uploadType}
                        onChange={(event) => setUploadType(event.target.value as KycDocumentType)}
                      >
                        {DOCUMENT_TYPES.map((type) => (
                          <option key={type} value={type}>
                            {humanise(type)}
                          </option>
                        ))}
                      </select>
                    </Field>
                    <div className="upload-actions">
                      <button
                        type="button"
                        className="btn btn-secondary"
                        onClick={() => pickFile(null)}
                        disabled={!file || uploading}
                      >
                        Clear
                      </button>
                      <button
                        type="button"
                        className="btn btn-primary"
                        onClick={() => void uploadDocument()}
                        disabled={!file || uploading}
                      >
                        {uploading ? 'Uploading…' : 'Upload document'}
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            )}

            {step === 4 && (
              <div className="stack-lg">
                {SECTIONS.map((section) => (
                  <div className="review-block" key={section.title}>
                    <div className="section-head">
                      <h3 className="section-title">{section.title}</h3>
                      <button
                        type="button"
                        className="link-button text-xs"
                        onClick={() => setStep(STEPS.findIndex((item) => item.key === section.step))}
                      >
                        Edit
                      </button>
                    </div>
                    <dl className="review-list">
                      {section.fields.map((field) => (
                        <div key={field.key}>
                          <dt>{field.label}</dt>
                          <dd>{values[field.key]?.trim() || <span className="muted">Not provided</span>}</dd>
                        </div>
                      ))}
                    </dl>
                  </div>
                ))}

                <div className="review-block">
                  <div className="section-head">
                    <h3 className="section-title">Documents</h3>
                    <button type="button" className="link-button text-xs" onClick={() => setStep(3)}>
                      Edit
                    </button>
                  </div>
                  <p className="muted text-sm">
                    {documents.length === 0
                      ? 'No documents uploaded yet.'
                      : `${documents.length} document${documents.length === 1 ? '' : 's'} uploaded.`}
                  </p>
                </div>

                {extraAnswers.length > 0 && (
                  <div className="review-block">
                    <h3 className="section-title">Additional information</h3>
                    <dl className="review-list">
                      {extraAnswers.map(([key, value]) => (
                        <div key={key}>
                          <dt>{humanise(key)}</dt>
                          <dd>{value}</dd>
                        </div>
                      ))}
                    </dl>
                  </div>
                )}

                <label className={`checkbox ${errors.declaration ? 'checkbox--error' : ''}`}>
                  <input
                    type="checkbox"
                    checked={declaration}
                    onChange={(event) => {
                      setDeclaration(event.target.checked);
                      setErrors((current) => {
                        if (!current.declaration) return current;
                        const next = { ...current };
                        delete next.declaration;
                        return next;
                      });
                    }}
                  />
                  <span>
                    I confirm the information provided is accurate and complete, and I understand that
                    providing false information may affect this application.
                  </span>
                </label>
                {errors.declaration && <p className="field-error">{errors.declaration}</p>}
              </div>
            )}

            <div className="wizard-nav">
              <button
                type="button"
                className="btn btn-ghost"
                onClick={goBack}
                disabled={step === 0 || submitting || saving}
              >
                <Icon name="arrowLeft" size={16} />
                Back
              </button>
              <div className="wizard-nav-right">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => void saveDraft()}
                  disabled={saving || submitting}
                >
                  {saving ? 'Saving…' : 'Save draft'}
                </button>
                {step < STEPS.length - 1 ? (
                  <button type="button" className="btn btn-primary" onClick={goNext}>
                    Continue
                    <Icon name="arrowRight" size={16} />
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={() => void submitApplication()}
                    disabled={submitting || saving}
                  >
                    {submitting ? 'Submitting…' : 'Submit application'}
                    {!submitting && <Icon name="check" size={16} />}
                  </button>
                )}
              </div>
            </div>
          </div>
        </div>
      ) : (
        <>
          <Card title="Application answers" subtitle="Information provided with this application">
            <div className="stack-lg">
              {SECTIONS.map((section) => (
                <div key={section.title}>
                  <h3 className="section-title">{section.title}</h3>
                  <dl className="answer-grid">
                    {section.fields.map((field) => (
                      <div className="answer-item" key={field.key}>
                        <dt>{field.label}</dt>
                        <dd>{values[field.key]?.trim() || <span className="muted">Not provided</span>}</dd>
                      </div>
                    ))}
                  </dl>
                </div>
              ))}
              {extraAnswers.length > 0 && (
                <div>
                  <h3 className="section-title">Additional information</h3>
                  <dl className="answer-grid">
                    {extraAnswers.map(([key, value]) => (
                      <div className="answer-item" key={key}>
                        <dt>{humanise(key)}</dt>
                        <dd>{value}</dd>
                      </div>
                    ))}
                  </dl>
                </div>
              )}
            </div>
          </Card>

          <Card title="Documents" subtitle="Uploaded KYC evidence">
            {documentList}
          </Card>
        </>
      )}

      <Card title="History" subtitle="Every status change is recorded for audit">
        {historyItems.length === 0 ? (
          <EmptyState icon="clock" message="No status changes recorded yet." />
        ) : (
          <Timeline items={historyItems} />
        )}
      </Card>

      <div className="action-bar">
        <Link to="/applications" className="btn btn-ghost">
          <Icon name="arrowLeft" size={16} />
          Back to applications
        </Link>
      </div>

      <ConfirmDialog
        open={confirmWithdraw}
        title="Withdraw application"
        message="Withdrawing this application is permanent and cannot be undone. Do you want to continue?"
        confirmLabel="Withdraw"
        destructive
        busy={withdrawing}
        onConfirm={() => void withdrawApplication()}
        onCancel={() => setConfirmWithdraw(false)}
      />
    </div>
  );
}
