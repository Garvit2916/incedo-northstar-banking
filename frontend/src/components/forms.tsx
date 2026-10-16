import { useId, useState } from 'react';
import type { ChangeEvent, ReactNode } from 'react';
import { PASSWORD_REQUIREMENTS, evaluatePassword, passwordStrength } from '../lib/passwordPolicy';

export function Field({
  label,
  htmlFor,
  required,
  error,
  help,
  children,
  className,
}: {
  label: string;
  htmlFor: string;
  required?: boolean;
  error?: string | null;
  help?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  const helpId = help ? `${htmlFor}-help` : undefined;
  const errorId = error ? `${htmlFor}-error` : undefined;

  return (
    <div className={['field', className].filter(Boolean).join(' ')}>
      <label htmlFor={htmlFor}>
        {label}
        {required ? (
          <span className="required-mark" aria-label="required">
            {' '}
            *
          </span>
        ) : (
          <span className="optional-mark"> (optional)</span>
        )}
      </label>
      {children}
      {help && (
        <p className="field-help" id={helpId}>
          {help}
        </p>
      )}
      {error && (
        <p className="field-error" id={errorId} role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

export function PasswordInput({
  id,
  value,
  onChange,
  onBlur,
  autoComplete = 'current-password',
  describedBy,
  invalid,
  disabled,
  placeholder,
}: {
  id: string;
  value: string;
  onChange: (event: ChangeEvent<HTMLInputElement>) => void;
  onBlur?: () => void;
  autoComplete?: string;
  describedBy?: string;
  invalid?: boolean;
  disabled?: boolean;
  placeholder?: string;
}) {
  const [visible, setVisible] = useState(false);

  return (
    <div className="password-input">
      <input
        id={id}
        type={visible ? 'text' : 'password'}
        value={value}
        onChange={onChange}
        onBlur={onBlur}
        autoComplete={autoComplete}
        aria-describedby={describedBy}
        aria-invalid={invalid || undefined}
        disabled={disabled}
        placeholder={placeholder}
        spellCheck={false}
      />
      <button
        type="button"
        className="password-toggle"
        aria-pressed={visible}
        aria-label={visible ? 'Hide password' : 'Show password'}
        onClick={() => setVisible((current) => !current)}
      >
        {visible ? 'Hide' : 'Show'}
      </button>
    </div>
  );
}

export function PasswordRequirements({
  id,
  password,
  confirmation,
  showConfirmation = true,
}: {
  id?: string;
  password: string;
  confirmation?: string;
  showConfirmation?: boolean;
}) {
  const results = evaluatePassword(password);
  const confirmationMet = showConfirmation ? confirmation !== undefined && confirmation.length > 0 && confirmation === password : true;

  return (
    <ul id={id} className="password-requirements" aria-label="Password requirements">
      {PASSWORD_REQUIREMENTS.map((requirement) => {
        const met = results[requirement.id];
        return (
          <li key={requirement.id} className={met ? 'requirement met' : 'requirement'}>
            <span className="requirement-icon" aria-hidden="true">
              {met ? '✓' : '○'}
            </span>
            <span>{requirement.label}</span>
            <span className="sr-only">{met ? ' — met' : ' — not yet met'}</span>
          </li>
        );
      })}
      {showConfirmation && (
        <li className={confirmationMet ? 'requirement met' : 'requirement'}>
          <span className="requirement-icon" aria-hidden="true">
            {confirmationMet ? '✓' : '○'}
          </span>
          <span>Password confirmation matches</span>
          <span className="sr-only">{confirmationMet ? ' — met' : ' — not yet met'}</span>
        </li>
      )}
    </ul>
  );
}

export function PasswordStrengthMeter({ password }: { password: string }) {
  if (password.length === 0) {
    return null;
  }

  const strength = passwordStrength(password);

  return (
    <div className="strength">
      <div className="strength-track" aria-hidden="true">
        <div className={`strength-bar strength-${strength.level}`} style={{ width: `${strength.score}%` }} />
      </div>
      <p className="strength-label" aria-live="polite">
        Password strength: <strong className={`strength-text strength-text-${strength.level}`}>{strength.label}</strong>
      </p>
    </div>
  );
}

export function useFieldIds(prefix: string) {
  const reactId = useId();
  const base = `${prefix}-${reactId}`;
  return {
    field: base,
    help: `${base}-help`,
    error: `${base}-error`,
  };
}
