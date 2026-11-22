import type { NextBestAction } from '../api/types';

const CLOSING = '\n\nKind regards,\nNorthstar Support';

/**
 * Builds an editable agent draft from an advisory next-best action.
 *
 * The output is never sent automatically. Sensitive workflows deliberately use
 * safe language and require the agent to follow the approved verification or
 * escalation process rather than asking customers for secrets.
 */
export function draftForAction(action: NextBestAction): string {
  const outcome = action.expectedOutcome.trim();

  switch (action.actionType) {
    case 'request-documents':
      return [
        'Hi,',
        '',
        'To progress this request, please use the secure upload link in your portal to provide the outstanding documents or information. Do not send documents by email.',
        outcome,
        CLOSING,
      ].join('\n');

    case 'verify-identity':
      return [
        'Hi,',
        '',
        'Before we can discuss or change account details, we need to complete our approved identity verification process. We will never ask for your password, OTP, PIN or card number.',
        outcome,
        CLOSING,
      ].join('\n');

    case 'follow-up':
      return [
        'Hi,',
        '',
        `We are following up on your request. ${outcome}`,
        'Please let us know if anything has changed or if you need further help.',
        CLOSING,
      ].join('\n');

    case 'clarify':
      return [
        'Hi,',
        '',
        'Thank you for contacting Northstar. To help us resolve this accurately, could you clarify the following?',
        outcome,
        CLOSING,
      ].join('\n');

    case 'review-application':
      return [
        'Internal note:',
        `${action.title} — ${action.reason}`,
        `Suggested workflow: ${action.suggestedWorkflow ?? 'agent-review'}`,
        `Expected outcome: ${outcome}`,
      ].join('\n');

    default:
      return ['Hi,', '', `Thank you for contacting Northstar. ${outcome}`, CLOSING].join('\n');
  }
}

/** Converts an AI confidence value into a stable 0–100 display percentage. */
export function confidencePercent(value: number | null | undefined): number | null {
  if (value == null || Number.isNaN(value)) {
    return null;
  }
  const scaled = value <= 1 ? value * 100 : value;
  return Math.round(Math.min(100, Math.max(0, scaled)));
}
