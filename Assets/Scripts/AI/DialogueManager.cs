using System.Collections;
using UnityEngine;
using POSTechSupport.Core;
using POSTechSupport.Logic;

namespace POSTechSupport.AI
{
    /// <summary>
    /// M4 — the four-stage customer AI from GDD §9, in order:
    ///   1. <see cref="IntentClassifier"/>  — what did the agent just ask?
    ///   2. <see cref="DialoguePolicy"/>    — what may be said about it? (the brain)
    ///   3. <see cref="ILlmClient"/>        — word it (the mouth, optional)
    ///   4. <see cref="GroundingGuard"/>    — last check before it reaches the screen
    ///
    /// Stage 3 runs AFTER the line is already posted, not before. The template reply appears instantly
    /// and is always safe; if a local model is enabled and answers in time, the same ChatLine object is
    /// rewritten in place. So enabling the LLM can improve the wording and can never stall the call, and
    /// pulling the model out changes nothing about whether the game is playable.
    /// </summary>
    public class DialogueManager
    {
        private readonly IntentClassifier classifier = new();
        private readonly DialoguePolicy policy;
        private readonly GroundingGuard guard = new();
        private readonly ILlmClient llm;
        private readonly MonoBehaviour runner;      // coroutine host for the optional LLM pass

        public DialogueManager(ILlmClient llm, MonoBehaviour runner, int seed = 0)
        {
            this.llm = llm ?? new TemplateLlmClient();
            this.runner = runner;
            policy = new DialoguePolicy(seed == 0 ? new System.Random() : new System.Random(seed));
        }

        /// <summary>
        /// The agent typed something. Appends their line and the customer's reply, and returns the act
        /// so callers can react to side effects (an unauthorized caller hanging up, an SMS request).
        /// </summary>
        public DialogueAct HandlePlayerUtterance(ProblemInstance p, string playerText)
        {
            if (p == null || string.IsNullOrWhiteSpace(playerText)) return null;
            p.ticket.chat.Add(new ChatLine { kind = ChatKind.Agent, text = playerText.Trim() });
            return Respond(p, classifier.Classify(playerText).intent);
        }

        /// <summary>
        /// Same pipeline, entered from a canned quick-ask button instead of typing. Routing both through
        /// here is what stops the buttons and the text box behaving like two different customers.
        /// </summary>
        public DialogueAct HandleIntent(ProblemInstance p, PlayerIntent intent, string agentLine = null)
        {
            if (p == null) return null;
            if (!string.IsNullOrWhiteSpace(agentLine))
                p.ticket.chat.Add(new ChatLine { kind = ChatKind.Agent, text = agentLine });
            return Respond(p, intent);
        }

        /// <summary>The customer's opening line — a symptom, unprompted, the way a real call starts.</summary>
        public void OpenCall(ProblemInstance p)
        {
            if (p.ticket.chat.Count > 0) return;
            var truth = GroundTruth.From(p);
            var act = policy.Decide(truth, p.ticket.dialogue, PlayerIntent.AskSymptom);
            string opener = $"Hi, this is {truth.callerName} from {truth.statedStoreName}. {act.content}";
            Post(p, truth, opener, act);
        }

        // -------------------------------------------------------------------------------------------
        private DialogueAct Respond(ProblemInstance p, PlayerIntent intent)
        {
            var auth = p.ticket.authorization;
            if (auth.customerHungUp) return null;      // nobody on the line to answer

            var truth = GroundTruth.From(p);
            var act = policy.Decide(truth, p.ticket.dialogue, intent);

            if (intent == PlayerIntent.AskAuthorized) auth.asked = true;
            if (act.kind == DialogueActKind.ConfirmAuthorized) auth.confirmed = true;

            Post(p, truth, act.content, act);

            if (act.endsCall)
            {
                auth.customerHungUp = true;
                p.ticket.chat.Add(new ChatLine { kind = ChatKind.System, text = "[Call disconnected — customer hung up]" });
            }
            return act;
        }

        /// <summary>Guard the template, post it, then let the model try to improve it in the background.</summary>
        private void Post(ProblemInstance p, GroundTruth truth, string text, DialogueAct act)
        {
            // The template goes through the guard too. It should always pass — if it ever doesn't, that's
            // an authoring bug worth catching here rather than shipping the leak.
            string safe = text;
            if (!guard.IsSafe(safe, p))
            {
                Debug.LogWarning($"[DialogueManager] template line rejected ({guard.LastRejectionReason}): \"{text}\"");
                safe = "Sorry — I'm not sure how to describe it.";
            }

            var line = new ChatLine { kind = ChatKind.Customer, text = safe, fact = act?.fact };
            p.ticket.chat.Add(line);

            if (llm.Enabled && runner != null && act != null)
                runner.StartCoroutine(PolishInPlace(p, truth, line, safe));
        }

        private IEnumerator PolishInPlace(ProblemInstance p, GroundTruth truth, ChatLine line, string fallback)
        {
            yield return llm.Rephrase(SystemPrompt(truth), fallback, reworded =>
            {
                if (string.IsNullOrWhiteSpace(reworded)) return;
                if (!guard.IsSafe(reworded, p))
                {
                    Debug.LogWarning($"[GroundingGuard] blocked model output ({guard.LastRejectionReason}) — keeping template.");
                    return;
                }
                line.text = reworded;
            });
        }

        /// <summary>
        /// GDD §9's sample prompt. Note what is interpolated: persona, identity, and the symptom the
        /// caller can see — never a fault, a clue, or a resolution. The prompt is built from
        /// <see cref="GroundTruth"/> precisely so there is nothing else available to interpolate.
        /// </summary>
        private static string SystemPrompt(GroundTruth t)
        {
            string symptom = t.visibleSymptoms.Count > 0 ? t.visibleSymptoms[0] : "something isn't working right";
            string role = t.callerRole == CallerRole.Owner ? "the owner" : "a staff member";
            return
                $"You are {t.callerName}, {role} of a small shop. You are NOT tech-savvy.\n" +
                $"You only describe what YOU SEE: {symptom}\n" +
                "You do NOT know the cause. Never use technical words (driver, firewall, service, config, port, IP...).\n" +
                "If asked about a technical cause, say you don't really understand that and ask the support agent to check it.\n" +
                "Keep replies SHORT, natural and in character. Always respond in English.";
        }
    }
}
