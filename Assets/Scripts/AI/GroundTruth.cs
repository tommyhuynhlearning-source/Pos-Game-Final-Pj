using System.Collections.Generic;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Logic;

namespace POSTechSupport.AI
{
    /// <summary>
    /// THE boundary of GDD nguyên tắc bất biến #2 and §3: the only view of a ticket the customer AI is
    /// ever handed. It carries what a non-technical shopkeeper genuinely knows — what they can SEE, who
    /// they are, and whether the owner told them to call — and nothing else.
    ///
    /// Deliberately ABSENT, and it must stay that way: IssueSO, ActiveFault, Symptom.technical,
    /// DiagnosticClue, ResolutionCondition, VirtualDesktopInstance. The AI cannot leak an answer it was
    /// never given; GroundingGuard is the second line, not the first.
    ///
    /// Built by <see cref="From"/> only — never hand a ProblemInstance to anything under this namespace.
    /// </summary>
    public class GroundTruth
    {
        // --- who is on the phone -----------------------------------------------------------------
        public string callerName;
        public CallerRole callerRole;
        public PersonaProfileSO persona;

        /// <summary>What the caller BELIEVES, which may be wrong (persona.memoryAccuracy).</summary>
        public string statedStoreName, statedOwnerName, statedMachineId;

        /// <summary>
        /// What is actually true of their own shop. Not a leak of anything technical: these are the
        /// sign over the door, the name on the licence and the sticker on the register — things the
        /// caller can walk over and READ when the agent asks them to double-check. Without them a
        /// misremembering caller would be a dead end, since nothing else in the game can correct them.
        /// </summary>
        public string trueStoreName, trueOwnerName, trueMachineId;

        /// <summary>
        /// The remote session code showing on THEIR screen. The customer is the only source of it —
        /// nothing on file has it — so relaying it is an ordinary thing a shopkeeper can do, and the
        /// agent still has to work out which device to point it at.
        /// </summary>
        public string sessionCode;

        /// <summary>They legitimately know whether the owner asked them to call. Not a technical fact.</summary>
        public bool callerAuthorized;
        public bool isRefundVoidCase;

        // --- what they can see --------------------------------------------------------------------
        /// <summary>Symptom.layman ONLY. The matching Symptom.technical is not copied across.</summary>
        public readonly List<string> visibleSymptoms = new();

        public static GroundTruth From(ProblemInstance p)
        {
            var g = new GroundTruth
            {
                callerName = p.persona.name,
                callerRole = p.persona.role,
                persona = p.persona.profile,
                statedStoreName = p.persona.statedStoreName,
                statedOwnerName = p.persona.statedOwnerName,
                statedMachineId = p.persona.statedMachineId,
                trueStoreName = p.store?.storeName,
                trueOwnerName = p.store?.ownerName,
                trueMachineId = p.store?.MachineId,
                sessionCode = p.ticket.remoteConnect.passcode,
                callerAuthorized = p.ticket.authorization.callerAuthorized,
                isRefundVoidCase = p.ticket.authorization.isRefundVoidCase,
            };

            foreach (var issue in p.issues)
            {
                if (issue?.symptoms == null) continue;
                foreach (var s in issue.symptoms)
                    if (!string.IsNullOrWhiteSpace(s.layman))
                        g.visibleSymptoms.Add(s.layman);      // .layman only — .technical stays behind
            }
            return g;
        }

        // --- persona traits, with safe defaults when no profile is authored ------------------------
        public float TechLiteracy    => persona != null ? persona.techLiteracy : 0.3f;
        public float Cooperativeness => persona != null ? persona.cooperativeness : 0.6f;
        public float EmotionalState  => persona != null ? persona.emotionalState : 0.4f;

        /// <summary>
        /// Apply the persona's misnaming map (GDD §9 ràng buộc tầng 4). Lower techLiteracy → more likely
        /// to call the terminal "the card machine". This is where the customer's wrong vocabulary is
        /// produced on purpose — it is a trick, not a bug.
        /// </summary>
        public string Misname(string text, System.Random rng)
        {
            if (persona?.misnaming == null || string.IsNullOrEmpty(text)) return text;
            float chance = 1f - TechLiteracy;
            foreach (var m in persona.misnaming)
            {
                if (string.IsNullOrEmpty(m.correctTerm) || string.IsNullOrEmpty(m.customerTerm)) continue;
                if (rng.NextDouble() > chance) continue;
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, System.Text.RegularExpressions.Regex.Escape(m.correctTerm), m.customerTerm,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            return text;
        }
    }
}
