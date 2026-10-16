import { useEffect, useState } from 'react';
import { customersApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { Card, ErrorBanner } from '../components/ui';
import { SkeletonCard } from '../components/Skeleton';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import type { ProfileResponse } from '../api/types';

const FIELDS: Array<{ key: keyof ProfileResponse; label: string; type?: string; required?: boolean }> = [
  { key: 'firstName', label: 'First name', required: true },
  { key: 'lastName', label: 'Last name', required: true },
  { key: 'phoneNumber', label: 'Phone', type: 'tel' },
  { key: 'dateOfBirth', label: 'Date of birth', type: 'date' },
  { key: 'addressLine1', label: 'Address line 1' },
  { key: 'addressLine2', label: 'Address line 2' },
  { key: 'city', label: 'City' },
  { key: 'state', label: 'State / region' },
  { key: 'postalCode', label: 'Postal code' },
  { key: 'country', label: 'Country' },
  { key: 'occupation', label: 'Occupation' },
  { key: 'annualIncomeBand', label: 'Annual income band' },
];

type FormState = Record<string, string>;

export function ProfilePage() {
  const { data, loading, error, reload } = useAsync(() => customersApi.me(), []);
  const [form, setForm] = useState<FormState>({});
  const [busy, setBusy] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const toast = useToast();

  useEffect(() => {
    if (data) {
      const next: FormState = {};
      for (const field of FIELDS) {
        const value = data[field.key];
        next[field.key as string] = value === null || value === undefined ? '' : String(value);
      }
      setForm(next);
    }
  }, [data]);

  if (error || (!loading && !data)) {
    return <ErrorBanner message={error ?? 'Profile unavailable.'} />;
  }

  if (loading || !data) {
    return (
      <div className="page">
        <header className="page-header">
          <div>
            <h1>My profile</h1>
          </div>
        </header>
        <Card>
          <SkeletonCard />
        </Card>
      </div>
    );
  }

  async function save() {
    const trimmed: FormState = { ...form, firstName: form.firstName.trim(), lastName: form.lastName.trim() };
    const validation: Record<string, string> = {};
    if (!trimmed.firstName) {
      validation.firstName = 'First name is required.';
    }
    if (!trimmed.lastName) {
      validation.lastName = 'Last name is required.';
    }
    setFieldErrors(validation);
    if (Object.keys(validation).length > 0) {
      toast.error('Please correct the highlighted fields.');
      return;
    }

    setBusy(true);
    try {
      await customersApi.update({
        firstName: trimmed.firstName,
        lastName: trimmed.lastName,
        phoneNumber: trimmed.phoneNumber || null,
        dateOfBirth: trimmed.dateOfBirth || null,
        addressLine1: trimmed.addressLine1 || null,
        addressLine2: trimmed.addressLine2 || null,
        city: trimmed.city || null,
        state: trimmed.state || null,
        postalCode: trimmed.postalCode || null,
        country: trimmed.country || null,
        occupation: trimmed.occupation || null,
        annualIncomeBand: trimmed.annualIncomeBand || null,
      } as Partial<ProfileResponse>);
      toast.success('Profile updated.');
      setFieldErrors({});
      reload();
    } catch (err) {
      if (err instanceof ApiError && err.fields) {
        const mapped: Record<string, string> = {};
        for (const [key, messages] of Object.entries(err.fields)) {
          mapped[key] = messages[0];
        }
        setFieldErrors(mapped);
      }
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to update profile.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="page">
      <header className="page-header">
        <div>
          <h1>My profile</h1>
          <p className="muted">
            {data.email} · {data.isComplete ? 'Complete' : 'Incomplete'} ·{' '}
            {data.isContactVerified ? 'Contact verified' : 'Contact not verified'}
          </p>
        </div>
      </header>

      <Card title="Personal details">
        <div className="form-grid">
          {FIELDS.map((field) => {
            const key = field.key as string;
            const fieldError = fieldErrors[key];
            return (
              <label className="field" key={key} htmlFor={`profile-${key}`}>
                <span>
                  {field.label}
                  {field.required && <span className="required-mark" aria-hidden="true"> *</span>}
                </span>
                <input
                  id={`profile-${key}`}
                  type={field.type ?? 'text'}
                  value={form[key] ?? ''}
                  aria-invalid={fieldError ? 'true' : undefined}
                  aria-describedby={fieldError ? `profile-${key}-error` : undefined}
                  onChange={(e) => {
                    setForm((current) => ({ ...current, [key]: e.target.value }));
                    setFieldErrors((current) => ({ ...current, [key]: '' }));
                  }}
                />
                {fieldError && (
                  <span className="field-error" id={`profile-${key}-error`} role="alert">
                    {fieldError}
                  </span>
                )}
              </label>
            );
          })}
        </div>
        <div className="action-bar">
          <button type="button" className="btn btn-primary" onClick={save} disabled={busy}>
            {busy ? 'Saving…' : 'Save changes'}
          </button>
        </div>
      </Card>
    </div>
  );
}
