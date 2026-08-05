using POSTechSupport.Logic;

namespace POSTechSupport.AI
{
    /// <summary>
    /// Step 4 — the last gate before anything reaches the screen (GDD §9). Two checks:
    ///
    /// 1. JARGON — the reply may not contain anything from <see cref="TechnicalVocabulary"/>. Same list
    ///    the classifier uses, so "the agent can't ask it" and "the customer can't say it" stay in sync.
    /// 2. LEAK — the reply may not contain a module state FIELD NAME from the real fault. This one runs
    ///    against the full ProblemInstance on purpose: the guard sits OUTSIDE the AI boundary, so unlike
    ///    DialoguePolicy it is allowed to know the answer in order to check nobody gave it away.
    ///
    /// Only field names are checked, never fault VALUES — "Empty" is a perfectly ordinary word for a
    /// customer to use about a paper tray, and banning it would gag honest speech.
    ///
    /// A failed check is not an error to surface: it silently falls back to the policy's own template,
    /// which is safe by construction. The guard exists so an LLM can never make the game unwinnable.
    /// </summary>
    public class GroundingGuard
    {
        public string LastRejectionReason { get; private set; }

        /// <summary>True when <paramref name="text"/> is safe to show as the customer's line.</summary>
        public bool IsSafe(string text, ProblemInstance p)
        {
            LastRejectionReason = null;
            if (string.IsNullOrWhiteSpace(text)) { LastRejectionReason = "empty"; return false; }

            string lower = text.ToLowerInvariant();

            string jargon = TechnicalVocabulary.FirstHit(lower);
            if (jargon != null) { LastRejectionReason = $"technical term \"{jargon}\""; return false; }

            foreach (var issue in p.issues)
            {
                if (issue?.faults == null) continue;
                foreach (var f in issue.faults)
                {
                    if (string.IsNullOrEmpty(f.stateField)) continue;
                    if (lower.Contains(f.stateField.ToLowerInvariant()))
                    {
                        LastRejectionReason = $"leaked state field \"{f.stateField}\"";
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Take <paramref name="candidate"/> if it passes, otherwise keep the safe template.</summary>
        public string Filter(string candidate, string safeFallback, ProblemInstance p) =>
            IsSafe(candidate, p) ? candidate : safeFallback;
    }
}
