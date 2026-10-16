import { describe, expect, it } from 'vitest';
import {
  PASSWORD_MIN_LENGTH,
  PASSWORD_REQUIREMENTS,
  evaluatePassword,
  isPasswordValid,
  passwordsMatch,
  passwordStrength,
  unmetRequirements,
} from './passwordPolicy';

const VALID = 'Northstar#1';
const VALID_STRONG = 'Northstar#Strong01';

describe('passwordPolicy', () => {
  it('accepts a password that satisfies every requirement', () => {
    expect(isPasswordValid(VALID)).toBe(true);
    expect(unmetRequirements(VALID)).toHaveLength(0);
  });

  it.each([
    ['Short#1', 'length'],
    ['northstar#1', 'uppercase'],
    ['NORTHSTAR#1', 'lowercase'],
    ['Northstar#', 'number'],
    ['Northstar1', 'special'],
    [' Northstar#1', 'no-space'],
    ['Northstar#1 ', 'no-space'],
  ])('rejects %s and reports the %s requirement', (password, requirementId) => {
    expect(isPasswordValid(password)).toBe(false);
    expect(evaluatePassword(password)[requirementId]).toBe(false);
    expect(unmetRequirements(password).map((r) => r.id)).toContain(requirementId);
  });

  it('treats the exact minimum length with all classes as valid', () => {
    const password = 'North#123a';
    expect(password.length).toBe(PASSWORD_MIN_LENGTH);
    expect(isPasswordValid(password)).toBe(true);
  });

  it('does not trim whitespace — a padded password fails', () => {
    expect(isPasswordValid(` ${VALID}`)).toBe(false);
    expect(isPasswordValid(`${VALID} `)).toBe(false);
  });

  it('requires a non-empty confirmation that matches exactly', () => {
    expect(passwordsMatch(VALID, VALID)).toBe(true);
    expect(passwordsMatch(VALID, `${VALID}x`)).toBe(false);
    expect(passwordsMatch(VALID, '')).toBe(false);
  });

  it('exposes a stable requirement list', () => {
    expect(PASSWORD_REQUIREMENTS.map((r) => r.id)).toEqual([
      'length',
      'uppercase',
      'lowercase',
      'number',
      'special',
      'no-space',
    ]);
  });

  describe('strength meter', () => {
    it('reports weak for an empty password', () => {
      const strength = passwordStrength('');
      expect(strength.level).toBe('weak');
      expect(strength.score).toBe(0);
    });

    it('reports strong for a long password with all classes', () => {
      expect(passwordStrength(VALID_STRONG).level).toBe('strong');
    });

    it('never returns a score above 100', () => {
      const strength = passwordStrength(VALID_STRONG);
      expect(strength.score).toBeLessThanOrEqual(100);
    });
  });
});
