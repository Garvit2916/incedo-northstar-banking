import { useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ApiError } from '../api/client';
import {
  Field,
  PasswordInput,
  PasswordRequirements,
  PasswordStrengthMeter,
} from '../components/forms';
import { isPasswordValid, passwordsMatch } from '../lib/passwordPolicy';

type Mode = 'login' | 'register';

const DEMO_ACCOUNTS = [
  { label: 'Customer', email: 'customer@northstar.example' },
  { label: 'Support agent', email: 'agent@northstar.example' },
  { label: 'Reviewer', email: 'reviewer@northstar.example' },
  { label: 'Administrator', email: 'admin@northstar.example' },
];

const DEMO_PASSWORD = 'Northstar#Dev01';
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Demo credentials are a local-development convenience only. */
const DEMO_ACCOUNTS_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_ENABLE_DEMO_ACCOUNTS === 'true';

interface RegisterValues {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  confirmPassword: string;
  phoneNumber: string;
}

function validateRegistration(values: RegisterValues): Record<string, string> {
  const errors: Record<string, string> = {};
  if (!values.firstName.trim()) errors.firstName = 'First name is required.';
  if (!values.lastName.trim()) errors.lastName = 'Last name is required.';

  if (!values.email.trim()) errors.email = 'Email is required.';
  else if (!EMAIL_PATTERN.test(values.email.trim())) errors.email = 'Enter a valid email address.';

  if (!values.password) errors.password = 'Password is required.';
  else if (!isPasswordValid(values.password))
    errors.password = 'Password does not meet all the requirements below.';

  if (!values.confirmPassword) errors.confirmPassword = 'Please confirm your password.';
  else if (!passwordsMatch(values.password, values.confirmPassword))
    errors.confirmPassword = 'Passwords do not match.';

  if (values.phoneNumber.trim().length > 30) errors.phoneNumber = 'Phone number must be 30 characters or fewer.';

  return errors;
}

export function LoginPage() {
  const { login, register } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const redirectTo = (location.state as { from?: string } | null)?.from ?? '/dashboard';

  const [mode, setMode] = useState<Mode>('login');

  const [loginEmail, setLoginEmail] = useState('');
  const [loginPassword, setLoginPassword] = useState('');

  const [values, setValues] = useState<RegisterValues>({
    firstName: '',
    lastName: '',
    email: '',
    password: '',
    confirmPassword: '',
    phoneNumber: '',
  });

  const [touched, setTouched] = useState<Record<string, boolean>>({});
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const clientErrors = useMemo(() => validateRegistration(values), [values]);

  function update<K extends keyof RegisterValues>(key: K, value: RegisterValues[K]) {
    setValues((current) => ({ ...current, [key]: value }));
  }

  function markTouched(key: string) {
    setTouched((current) => ({ ...current, [key]: true }));
  }

  function errorFor(key: string): string | null {
    if (fieldErrors[key]) return fieldErrors[key];
    if (touched[key] && clientErrors[key]) return clientErrors[key];
    return null;
  }

  function switchMode(next: Mode) {
    setMode(next);
    setFormError(null);
    setFieldErrors({});
    setTouched({});
  }

  async function handleLogin(event: FormEvent) {
    event.preventDefault();
    setFormError(null);

    if (!loginEmail.trim() || !loginPassword) {
      setFormError('Enter your email address and password.');
      return;
    }

    setBusy(true);
    try {
      await login(loginEmail.trim(), loginPassword);
      navigate(redirectTo, { replace: true });
    } catch (error) {
      const message =
        error instanceof ApiError
          ? error.detail ?? 'Invalid email address or password.'
          : 'Unable to sign in. Please try again.';
      setFormError(message);
    } finally {
      setBusy(false);
    }
  }

  async function handleRegister(event: FormEvent) {
    event.preventDefault();
    setFormError(null);

    setTouched({
      firstName: true,
      lastName: true,
      email: true,
      password: true,
      confirmPassword: true,
      phoneNumber: true,
    });

    if (Object.keys(clientErrors).length > 0) {
      setFieldErrors(clientErrors);
      setFormError('Please correct the highlighted fields.');
      return;
    }

    setFieldErrors({});
    setBusy(true);
    try {
      await register({
        email: values.email.trim(),
        password: values.password,
        confirmPassword: values.confirmPassword,
        firstName: values.firstName.trim(),
        lastName: values.lastName.trim(),
        phoneNumber: values.phoneNumber.trim() || undefined,
      });
      navigate(redirectTo, { replace: true });
    } catch (error) {
      applyServerError(error);
    } finally {
      setBusy(false);
    }
  }

  function applyServerError(error: unknown) {
    if (!(error instanceof ApiError)) {
      setFormError('Something went wrong. Please try again.');
      return;
    }

    if (error.fields) {
      const mapped: Record<string, string> = {};
      for (const [field, messages] of Object.entries(error.fields)) {
        if (messages[0]) mapped[field] = messages[0];
      }
      setFieldErrors(mapped);
      setFormError('Please review the highlighted fields.');
      return;
    }

    const detail = error.detail ?? error.message;
    if (/already exists/i.test(detail)) {
      setFieldErrors({ email: detail });
      setFormError(null);
      return;
    }

    setFormError(detail);
  }

  function useDemo(demoEmail: string) {
    setMode('login');
    setLoginEmail(demoEmail);
    setLoginPassword(DEMO_PASSWORD);
    setFormError(null);
  }

  return (
    <div className="auth-page">
      <div className="auth-card">
        <div className="auth-brand">
          <span className="brand-mark">★</span>
          <div>
            <h1>Northstar Banking</h1>
            <p className="muted">Onboarding &amp; AI-assisted support workspace</p>
          </div>
        </div>

        <div className="auth-tabs" role="tablist">
          <button
            type="button"
            role="tab"
            aria-selected={mode === 'login'}
            className={mode === 'login' ? 'tab active' : 'tab'}
            onClick={() => switchMode('login')}
          >
            Sign in
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={mode === 'register'}
            className={mode === 'register' ? 'tab active' : 'tab'}
            onClick={() => switchMode('register')}
          >
            Register
          </button>
        </div>

        {formError && (
          <div className="alert alert-error" role="alert">
            {formError}
          </div>
        )}

        {mode === 'login' ? (
          <form onSubmit={handleLogin} className="form" noValidate>
            <Field label="Email" htmlFor="login-email" required>
              <input
                id="login-email"
                type="email"
                autoComplete="username"
                value={loginEmail}
                onChange={(e) => setLoginEmail(e.target.value)}
                required
              />
            </Field>

            <Field label="Password" htmlFor="login-password" required>
              <PasswordInput
                id="login-password"
                value={loginPassword}
                onChange={(e) => setLoginPassword(e.target.value)}
                autoComplete="current-password"
              />
            </Field>

            <button type="submit" className="btn btn-primary btn-block" disabled={busy}>
              {busy ? 'Signing in…' : 'Sign in'}
            </button>

            <p className="form-footer">
              New to Northstar?{' '}
              <button type="button" className="link-button" onClick={() => switchMode('register')}>
                Create an account
              </button>
            </p>
          </form>
        ) : (
          <form onSubmit={handleRegister} className="form" noValidate>
            <div className="form-row">
              <Field label="First name" htmlFor="reg-firstName" required error={errorFor('firstName')}>
                <input
                  id="reg-firstName"
                  value={values.firstName}
                  onChange={(e) => update('firstName', e.target.value)}
                  onBlur={() => markTouched('firstName')}
                  autoComplete="given-name"
                  aria-invalid={!!errorFor('firstName') || undefined}
                  required
                />
              </Field>
              <Field label="Last name" htmlFor="reg-lastName" required error={errorFor('lastName')}>
                <input
                  id="reg-lastName"
                  value={values.lastName}
                  onChange={(e) => update('lastName', e.target.value)}
                  onBlur={() => markTouched('lastName')}
                  autoComplete="family-name"
                  aria-invalid={!!errorFor('lastName') || undefined}
                  required
                />
              </Field>
            </div>

            <Field label="Email" htmlFor="reg-email" required error={errorFor('email')}>
              <input
                id="reg-email"
                type="email"
                value={values.email}
                onChange={(e) => update('email', e.target.value)}
                onBlur={() => markTouched('email')}
                autoComplete="username"
                aria-invalid={!!errorFor('email') || undefined}
                required
              />
            </Field>

            <Field
              label="Password"
              htmlFor="reg-password"
              required
              error={errorFor('password')}
              help={<span id="reg-password-help">Choose a strong password — see the requirements below.</span>}
            >
              <PasswordInput
                id="reg-password"
                value={values.password}
                onChange={(e) => update('password', e.target.value)}
                onBlur={() => markTouched('password')}
                autoComplete="new-password"
                describedBy="reg-password-help reg-password-requirements"
                invalid={!!errorFor('password')}
              />
              <PasswordStrengthMeter password={values.password} />
              <PasswordRequirements
                id="reg-password-requirements"
                password={values.password}
                confirmation={values.confirmPassword}
              />
            </Field>

            <Field label="Confirm password" htmlFor="reg-confirm" required error={errorFor('confirmPassword')}>
              <PasswordInput
                id="reg-confirm"
                value={values.confirmPassword}
                onChange={(e) => update('confirmPassword', e.target.value)}
                onBlur={() => markTouched('confirmPassword')}
                autoComplete="new-password"
                invalid={!!errorFor('confirmPassword')}
              />
            </Field>

            <Field label="Phone" htmlFor="reg-phone" error={errorFor('phoneNumber')}>
              <input
                id="reg-phone"
                value={values.phoneNumber}
                onChange={(e) => update('phoneNumber', e.target.value)}
                onBlur={() => markTouched('phoneNumber')}
                autoComplete="tel"
                aria-invalid={!!errorFor('phoneNumber') || undefined}
              />
            </Field>

            <button type="submit" className="btn btn-primary btn-block" disabled={busy}>
              {busy ? 'Creating account…' : 'Create account'}
            </button>

            <p className="form-footer">
              Already have an account?{' '}
              <button type="button" className="link-button" onClick={() => switchMode('login')}>
                Sign in
              </button>
            </p>
          </form>
        )}

        {DEMO_ACCOUNTS_ENABLED && (
          <div className="demo-accounts">
            <p className="muted">
              Local development only — demonstration accounts (password <code>{DEMO_PASSWORD}</code>)
            </p>
            <div className="demo-grid">
              {DEMO_ACCOUNTS.map((account) => (
                <button
                  key={account.email}
                  type="button"
                  className="btn btn-ghost"
                  onClick={() => useDemo(account.email)}
                >
                  {account.label}
                </button>
              ))}
            </div>
            <p className="disclaimer">
              These accounts exist only in local development seeding and are never enabled in
              production. All data is fictitious; no real customer information is used.
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
