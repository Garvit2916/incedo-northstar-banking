import { describe, expect, it } from 'vitest';
import { confidencePercent, draftForAction } from './aiDrafts';
import type { NextBestAction } from '../api/types';

function action(overrides: Partial<NextBestAction> = {}): NextBestAction {
  return {
    action: 'Check the request',
    title: 'Check the request',
    reason: 'The ticket contains a billing question.',
    explanation: 'Approved guidance applies to card billing.',
    evidence: 'Category: CreditCardBilling',
    priority: 'High',
    requiresHumanApproval: true,
    expectedOutcome: 'The customer receives an accurate billing explanation.',
    actionType: 'reply',
    sources: [],
    suggestedWorkflow: 'agent-review',
    confidence: 0.8,
    ...overrides,
  };
}

describe('draftForAction', () => {
  it('keeps document requests on the secure portal flow', () => {
    const draft = draftForAction(action({ actionType: 'request-documents' }));

    expect(draft).toContain('secure upload link in your portal');
    expect(draft).toContain('Do not send documents by email.');
  });

  it('never asks a customer to disclose credentials during verification', () => {
    const draft = draftForAction(action({ actionType: 'verify-identity' }));

    expect(draft).toContain('approved identity verification process');
    expect(draft).toContain('never ask for your password, OTP, PIN or card number');
    expect(draft.toLowerCase()).not.toContain('please reply with');
  });

  it('marks application reviews as internal notes rather than customer replies', () => {
    const draft = draftForAction(
      action({
        actionType: 'review-application',
        title: 'Review evidence',
        reason: 'Evidence is incomplete.',
        expectedOutcome: 'An authorised reviewer decides.',
      }),
    );

    expect(draft.startsWith('Internal note:')).toBe(true);
    expect(draft).toContain('Suggested workflow: agent-review');
    expect(draft).not.toContain('Kind regards');
  });

  it('creates an editable reply with the expected outcome for normal actions', () => {
    const draft = draftForAction(action({ actionType: 'clarify', expectedOutcome: 'Confirm the date.' }));

    expect(draft).toContain('could you clarify');
    expect(draft).toContain('Confirm the date.');
    expect(draft).toContain('Kind regards');
  });
});

describe('confidencePercent', () => {
  it('converts 0–1 scores to whole percentages', () => {
    expect(confidencePercent(0.826)).toBe(83);
  });

  it('accepts already-scaled values and clamps out-of-range values', () => {
    expect(confidencePercent(80)).toBe(80);
    expect(confidencePercent(120)).toBe(100);
    expect(confidencePercent(-1)).toBe(0);
  });

  it('returns null for missing or invalid scores', () => {
    expect(confidencePercent(null)).toBeNull();
    expect(confidencePercent(undefined)).toBeNull();
    expect(confidencePercent(Number.NaN)).toBeNull();
  });
});
