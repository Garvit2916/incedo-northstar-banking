using Northstar.Domain;

namespace Northstar.Application.Ai;

/// <summary>
/// Deterministic baseline sentiment classifier used by the development fallback
/// and as a sanity check on model output. It is intentionally simple and
/// explainable: every label is backed by the literal signals that produced it.
///
/// Confidence is deliberately left null. The specification permits a confidence
/// estimate only when it is meaningfully supported by the selected model; a
/// keyword heuristic does not provide a calibrated probability, so none is
/// reported rather than inventing a number.
/// </summary>
public static class SentimentRules
{
    private static readonly string[] EscalationSignals =
    {
        "fraud", "unauthorized", "unauthorised", "stolen", "scam", "suspicious",
        "police", "legal action", "ombudsman", "regulator", "sue "
    };

    private static readonly string[] UrgentSignals =
    {
        "urgent", "asap", "immediately", "emergency", "locked out", "cannot access",
        "money missing", "lost access", "blocked", "deadline"
    };

    private static readonly string[] NegativeSignals =
    {
        "frustrat", "angry", "unacceptable", "disappointed", "terrible", "worst",
        "still waiting", "again", "complaint", "error", "failed", "failure",
        "problem", "issue", "cannot", "can't", "unable", "stuck", "delay", "delayed"
    };

    private static readonly string[] PositiveSignals =
    {
        "thank", "thanks", "great", "appreciate", "happy", "excellent", "helpful", "resolved"
    };

    public static AiSentiment Analyze(AiTicketContext context)
    {
        var text = string.Join(
            '\n',
            context.Subject,
            string.Join('\n', context.Conversation.Select(m => m.Body))).ToLowerInvariant();

        var found = new List<string>();

        var escalation = Collect(text, EscalationSignals, found);
        var urgent = Collect(text, UrgentSignals, found);
        var negative = Collect(text, NegativeSignals, found);
        var positive = Collect(text, PositiveSignals, found);

        SentimentLabel label;
        string explanation;
        bool escalateRecommended;

        if (escalation > 0)
        {
            label = SentimentLabel.UrgentEscalation;
            escalateRecommended = true;
            explanation = "The ticket contains language associated with potential fraud, " +
                          "unauthorised access, or formal complaint. Treat as a priority " +
                          "escalation to an authorised team.";
        }
        else if (urgent > 0)
        {
            label = SentimentLabel.UrgentEscalation;
            escalateRecommended = true;
            explanation = "The customer expresses urgency or loss of access. Confirm identity " +
                          "through the approved process and escalate if access cannot be restored.";
        }
        else if (negative > positive)
        {
            label = SentimentLabel.Negative;
            escalateRecommended = false;
            explanation = "The ticket contains unresolved-problem language. Acknowledge the " +
                          "inconvenience and provide a clear next step.";
        }
        else if (positive > 0 && negative == 0)
        {
            label = SentimentLabel.Positive;
            escalateRecommended = false;
            explanation = "The customer uses appreciative language and no unresolved-problem signals.";
        }
        else
        {
            label = SentimentLabel.Neutral;
            escalateRecommended = false;
            explanation = "No strong sentiment signals were detected. Treat the ticket as a " +
                          "straightforward enquiry.";
        }

        return new AiSentiment(
            label,
            explanation,
            Confidence: null,
            escalateRecommended,
            Signals: found.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList());
    }

    private static int Collect(string text, string[] signals, List<string> found)
    {
        var count = 0;
        foreach (var signal in signals)
        {
            if (text.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(signal.Trim());
                count++;
            }
        }

        return count;
    }
}
