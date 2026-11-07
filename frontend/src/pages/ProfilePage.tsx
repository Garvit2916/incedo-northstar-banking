import { useEffect, useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import { customersApi } from '../api/endpoints';
import { ApiError } from '../api/client';
import { Field } from '../components/forms';
import { Icon } from '../components/icons';
import type { IconName } from '../components/icons';
import { SkeletonCard } from '../components/Skeleton';
import { Avatar, Card, ErrorBanner, InfoBanner, ProgressBar } from '../components/ui';
import { useToast } from '../components/Toast';
import { useAsync } from '../lib/useAsync';
import { formatDate } from '../lib/format';
import type { ProfileResponse } from '../api/types';

const INCOME_BANDS = [
  'Under 25,000',
  '25,000 – 49,999',
  '50,000 – 99,999',
  '100,000 – 149,999',
  '150,000 or more',
];

const COMPLETION_KEYS: (keyof ProfileResponse)[] = [
  'firstName',
  'lastName',
  'phoneNumber',
  'dateOfBirth',
  'addressLine1',
  'city',
  'state',
  'postalCode',
  'country',
  'occupation',
  'annualIncomeBand',
];

interface FormState {
  firstName: string;
  lastName: string;
  phoneNumber: string;
  dateOfBirth: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  occupation: string;
  annualIncomeBand: string;
}

const EMPTY_FORM: FormState = {
  firstName: '',
  lastName: '',
  phoneNumber: '',
  dateOfBirth: '',
  addressLine1: '',
  addressLine2: '',
  city: '',
  state: '',
  postalCode: '',
  country: '',
  occupation: '',
  annualIncomeBand: '',
};

function toForm(profile: ProfileResponse): FormState {
  return {
    firstName: profile.firstName ?? '',
    lastName: profile.lastName ?? '',
    phoneNumber: profile.phoneNumber ?? '',
    dateOfBirth: profile.dateOfBirth ?? '',
    addressLine1: profile.addressLine1 ?? '',
    addressLine2: profile.addressLine2 ?? '',
    city: profile.city ?? '',
    state: profile.state ?? '',
    postalCode: profile.postalCode ?? '',
    country: profile.country ?? '',
    occupation: profile.occupation ?? '',
    annualIncomeBand: profile.annualIncomeBand ?? '',
  };
}

function SectionHead({ icon, title, description }: { icon: IconName; title: string; description: string }) {
  return (
    <div className="form-section-head">
      <span className="form-section-icon" aria-hidden="true">
        <Icon name={icon} size={17} />
      </span>
      <div>
        <h3>{title}</h3>
        <p>{description}</p>
      </div>
    </div>
  );
}

export function ProfilePage() {
  const { data, loading, error, reload } = useAsync(() => customersApi.me(), []);
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [busy, setBusy] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const toast = useToast();

  useEffect(() => {
    if (data) setForm(toForm(data));
  }, [data]);

  const completion = useMemo(() => {
    if (!data) return 0;
    const filled = COMPLETION_KEYS.filter((key) => Boolean(data[key])).length;
    return Math.round((filled / COMPLETION_KEYS.length) * 100);
  }, [data]);

  if (error || (!loading && !data)) {
    return (
      <div className="page">
        <ErrorBanner message={error ?? 'Profile unavailable.'} onRetry={reload} />
      </div>
    );
  }

  if (loading || !data) {
    return (
      <div className="page">
        <div className="page-header">
          <div>
            <p className="eyebrow">Account</p>
            <h1 className="page-title">Profile & KYC</h1>
          </div>
        </div>
        <Card>
          <SkeletonCard />
        </Card>
      </div>
    );
  }

  function update<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }));
    setFieldErrors((current) => {
      if (!current[key]) return current;
      const next = { ...current };
      delete next[key];
      return next;
    });
  }

  async function save() {
    const validation: Record<string, string> = {};
    if (!form.firstName.trim()) validation.firstName = 'First name is required.';
    if (!form.lastName.trim()) validation.lastName = 'Last name is required.';
    if (form.phoneNumber.trim().length > 30) validation.phoneNumber = 'Phone number must be 30 characters or fewer.';
    if (form.dateOfBirth && new Date(form.dateOfBirth) > new Date()) {
      validation.dateOfBirth = 'Date of birth cannot be in the future.';
    }

    setFieldErrors(validation);
    if (Object.keys(validation).length > 0) {
      toast.error('Please correct the highlighted fields.');
      return;
    }

    setBusy(true);
    try {
      await customersApi.update({
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        phoneNumber: form.phoneNumber.trim() || null,
        dateOfBirth: form.dateOfBirth || null,
        addressLine1: form.addressLine1.trim() || null,
        addressLine2: form.addressLine2.trim() || null,
        city: form.city.trim() || null,
        state: form.state.trim() || null,
        postalCode: form.postalCode.trim() || null,
        country: form.country.trim() || null,
        occupation: form.occupation.trim() || null,
        annualIncomeBand: form.annualIncomeBand || null,
      } as Partial<ProfileResponse>);
      toast.success('Profile updated.');
      setFieldErrors({});
      reload();
    } catch (err) {
      if (err instanceof ApiError && err.fields) {
        const mapped: Record<string, string> = {};
        for (const [key, messages] of Object.entries(err.fields)) {
          if (messages[0]) mapped[key] = messages[0];
        }
        setFieldErrors(mapped);
      }
      toast.error(err instanceof ApiError ? err.detail ?? err.message : 'Unable to update profile.');
    } finally {
      setBusy(false);
    }
  }

  function discard() {
    setForm(toForm(data!));
    setFieldErrors({});
    toast.info('Unsaved changes discarded.');
  }

  const displayName = `${data.firstName ?? ''} ${data.lastName ?? ''}`.trim() || data.email;

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <p className="eyebrow">Account</p>
          <h1 className="page-title">Profile & KYC</h1>
          <p className="page-description">
            Keep your personal and financial details up to date. Complete profiles are required before
            submitting an application.
          </p>
        </div>
        <div className="page-actions">
          <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={busy}>
            {busy ? 'Saving…' : 'Save changes'}
            {!busy && <Icon name="save" size={16} />}
          </button>
        </div>
      </div>

      <section className="profile-hero">
        <div className="profile-identity">
          <Avatar name={displayName} size="lg" />
          <div>
            <h1>{displayName}</h1>
            <p>{data.email}</p>
            <div className="profile-badges">
              <span className={`badge badge--${data.isComplete ? 'success' : 'warning'}`}>
                {data.isComplete ? 'Profile complete' : 'Profile incomplete'}
              </span>
              <span className={`badge badge--${data.kycVerifiedAtUtc ? 'success' : 'neutral'}`}>
                {data.kycVerifiedAtUtc ? 'KYC verified' : 'KYC pending'}
              </span>
              <span className={`badge badge--${data.isContactVerified ? 'success' : 'neutral'}`}>
                {data.isContactVerified ? 'Contact verified' : 'Contact not verified'}
              </span>
            </div>
          </div>
        </div>
        <div className="completion-card" style={{ padding: 0, border: 0, background: 'transparent' }}>
          <div className="completion-ring" style={{ ['--value' as string]: completion } as CSSProperties}>
            <span>{completion}%</span>
          </div>
          <div className="completion-copy">
            <strong>Profile strength</strong>
            <ProgressBar value={completion} showValue={false} label="Profile completeness" />
          </div>
        </div>
      </section>

      {!data.isComplete && (
        <InfoBanner
          tone="warning"
          title="Complete your profile to apply"
          message="All key profile fields must be completed before an application can be submitted. Your progress is saved when you press Save changes."
        />
      )}

      <Card className="profile-section">
        <SectionHead
          icon="profile"
          title="Personal details"
          description="Your legal name, date of birth and contact number."
        />
        <div className="form-grid">
          <Field label="First name" htmlFor="profile-firstName" required error={fieldErrors.firstName}>
            <input
              id="profile-firstName"
              value={form.firstName}
              onChange={(event) => update('firstName', event.target.value)}
              autoComplete="given-name"
            />
          </Field>
          <Field label="Last name" htmlFor="profile-lastName" required error={fieldErrors.lastName}>
            <input
              id="profile-lastName"
              value={form.lastName}
              onChange={(event) => update('lastName', event.target.value)}
              autoComplete="family-name"
            />
          </Field>
          <Field label="Date of birth" htmlFor="profile-dateOfBirth" error={fieldErrors.dateOfBirth}>
            <input
              id="profile-dateOfBirth"
              type="date"
              value={form.dateOfBirth}
              max={new Date().toISOString().slice(0, 10)}
              onChange={(event) => update('dateOfBirth', event.target.value)}
              autoComplete="bday"
            />
          </Field>
          <Field label="Phone" htmlFor="profile-phoneNumber" error={fieldErrors.phoneNumber}>
            <input
              id="profile-phoneNumber"
              type="tel"
              value={form.phoneNumber}
              onChange={(event) => update('phoneNumber', event.target.value)}
              autoComplete="tel"
            />
          </Field>
        </div>
      </Card>

      <Card className="profile-section">
        <SectionHead icon="mapPin" title="Address" description="Your current residential address." />
        <div className="form-grid">
          <Field label="Address line 1" htmlFor="profile-addressLine1" className="span-2">
            <input
              id="profile-addressLine1"
              value={form.addressLine1}
              onChange={(event) => update('addressLine1', event.target.value)}
              autoComplete="address-line1"
            />
          </Field>
          <Field label="Address line 2" htmlFor="profile-addressLine2" className="span-2">
            <input
              id="profile-addressLine2"
              value={form.addressLine2}
              onChange={(event) => update('addressLine2', event.target.value)}
              autoComplete="address-line2"
            />
          </Field>
          <Field label="City" htmlFor="profile-city">
            <input
              id="profile-city"
              value={form.city}
              onChange={(event) => update('city', event.target.value)}
              autoComplete="address-level2"
            />
          </Field>
          <Field label="County / state" htmlFor="profile-state">
            <input
              id="profile-state"
              value={form.state}
              onChange={(event) => update('state', event.target.value)}
              autoComplete="address-level1"
            />
          </Field>
          <Field label="Postal code" htmlFor="profile-postalCode">
            <input
              id="profile-postalCode"
              value={form.postalCode}
              onChange={(event) => update('postalCode', event.target.value)}
              autoComplete="postal-code"
            />
          </Field>
          <Field label="Country" htmlFor="profile-country">
            <input
              id="profile-country"
              value={form.country}
              onChange={(event) => update('country', event.target.value)}
              autoComplete="country-name"
            />
          </Field>
        </div>
      </Card>

      <Card className="profile-section">
        <SectionHead
          icon="briefcase"
          title="Employment & financial"
          description="Used to assess eligibility for products and services."
        />
        <div className="form-grid">
          <Field label="Occupation" htmlFor="profile-occupation">
            <input
              id="profile-occupation"
              value={form.occupation}
              onChange={(event) => update('occupation', event.target.value)}
            />
          </Field>
          <Field label="Annual income band" htmlFor="profile-income">
            <select
              id="profile-income"
              value={form.annualIncomeBand}
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
        </div>
      </Card>

      <Card className="profile-section">
        <SectionHead
          icon="shieldCheck"
          title="Identity & verification"
          description="Managed by Northstar — contact support to change these details."
        />
        <div className="form-grid">
          <div className="field-readonly">
            <span className="muted text-xs">Email address</span>
            <span className="value">{data.email}</span>
          </div>
          <div className="field-readonly">
            <span className="muted text-xs">National ID</span>
            <span className="value">{data.nationalIdMasked || 'Not provided'}</span>
          </div>
          <div className="field-readonly">
            <span className="muted text-xs">KYC status</span>
            <span className="value">
              {data.kycVerifiedAtUtc ? `Verified ${formatDate(data.kycVerifiedAtUtc)}` : 'Pending verification'}
            </span>
          </div>
          <div className="field-readonly">
            <span className="muted text-xs">Contact verification</span>
            <span className="value">{data.isContactVerified ? 'Verified' : 'Not verified'}</span>
          </div>
        </div>
        {data.kycVerifiedAtUtc && (
          <p className="muted text-xs section-gap">
            <Icon name="checkCircle" size={13} /> Identity verified on {formatDate(data.kycVerifiedAtUtc)}.
          </p>
        )}
      </Card>

      <div className="action-bar">
        <button type="button" className="btn btn-ghost" onClick={discard} disabled={busy}>
          Discard changes
        </button>
        <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={busy}>
          {busy ? 'Saving…' : 'Save changes'}
        </button>
      </div>
    </div>
  );
}
