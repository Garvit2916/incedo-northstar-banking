/**
 * Shared registration password policy for the frontend.
 *
 * This mirrors the authoritative backend policy in
 * `backend/src/Northstar.Application/Validation/PasswordPolicy.cs` and the
 * ASP.NET Core Identity password options. Keep all three in sync.
 *
 * The policy applies to account *registration* only. Sign-in is intentionally
 * not subject to these rules so that legacy passwords keep working.
 */

export const PASSWORD_MIN_LENGTH = 10;

export interface PasswordRequirement {
  id: string;
  label: string;
  test: (password: string) => boolean;
}

export const PASSWORD_REQUIREMENTS: PasswordRequirement[] = [
  { id: 'length', label: `At least ${PASSWORD_MIN_LENGTH} characters`, test: (p) => p.length >= PASSWORD_MIN_LENGTH },
  { id: 'uppercase', label: 'One uppercase letter (A–Z)', test: (p) => /[A-Z]/.test(p) },
  { id: 'lowercase', label: 'One lowercase letter (a–z)', test: (p) => /[a-z]/.test(p) },
  { id: 'number', label: 'One number (0–9)', test: (p) => /[0-9]/.test(p) },
  { id: 'special', label: 'One special character (!@#$…)', test: (p) => /[^A-Za-z0-9]/.test(p) },
  { id: 'no-space', label: 'No leading or trailing spaces', test: (p) => p === p.trim() },
];

export type PasswordRequirementResult = Record<string, boolean>;

export function evaluatePassword(password: string): PasswordRequirementResult {
  const result: PasswordRequirementResult = {};
  for (const requirement of PASSWORD_REQUIREMENTS) {
    result[requirement.id] = requirement.test(password);
  }
  return result;
}

export function unmetRequirements(password: string): PasswordRequirement[] {
  return PASSWORD_REQUIREMENTS.filter((requirement) => !requirement.test(password));
}

export function isPasswordValid(password: string): boolean {
  return PASSWORD_REQUIREMENTS.every((requirement) => requirement.test(password));
}

export function passwordsMatch(password: string, confirmation: string): boolean {
  return confirmation.length > 0 && password === confirmation;
}

export type PasswordStrengthLevel = 'weak' | 'fair' | 'good' | 'strong';

export interface PasswordStrength {
  level: PasswordStrengthLevel;
  label: string;
  score: number;
  metCount: number;
}

/**
 * A user-experience aid only — not a guarantee of password security. It is based
 * purely on the password's own characteristics (length and character classes).
 * No breach-database lookup is performed.
 */
export function passwordStrength(password: string): PasswordStrength {
  const metCount = PASSWORD_REQUIREMENTS.filter((requirement) => requirement.test(password)).length;

  let level: PasswordStrengthLevel = 'weak';
  if (password.length > 0 && password.length >= PASSWORD_MIN_LENGTH) {
    if (metCount <= 4) level = 'fair';
    else if (metCount === 5) level = 'good';
    else level = password.length >= 14 ? 'strong' : 'good';
  }

  const labels: Record<PasswordStrengthLevel, string> = {
    weak: 'Weak',
    fair: 'Fair',
    good: 'Good',
    strong: 'Strong',
  };

  const percent = password.length === 0 ? 0 : Math.min(100, Math.round((metCount / PASSWORD_REQUIREMENTS.length) * 90) + (password.length >= 14 ? 10 : 0));

  return { level, label: labels[level], score: percent, metCount };
}
