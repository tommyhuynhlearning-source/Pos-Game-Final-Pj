using UnityEngine;
using POSTechSupport.AI;
using POSTechSupport.Core;
using POSTechSupport.Logic;

namespace POSTechSupport.Managers
{
    /// <summary>
    /// Coordinates the customer-facing channels behind ONE persona (Docs/manager.md CommunicationManager).
    /// Since M4 it owns no dialogue of its own: every word the customer says — typed chat, quick-ask
    /// button, or SMS — comes out of the same <see cref="DialogueManager"/>, which is what stops the
    /// caller being polite in chat and curt over SMS.
    ///
    /// What stays here is genuinely channel-level: which button maps to which intent, and the SMS
    /// receipt trick (GDD §10), whose correctness is a persona trait rather than a dialogue decision.
    /// </summary>
    public class CommunicationManager
    {
        private readonly DialogueManager dialogue;

        public CommunicationManager(DialogueManager dialogue) { this.dialogue = dialogue; }

        public void OpenCall(ProblemInstance p) => dialogue.OpenCall(p);

        /// <summary>Free-text chat — the M4 replacement for the canned menu.</summary>
        public DialogueAct SendChat(ProblemInstance p, string text) =>
            dialogue.HandlePlayerUtterance(p, text);

        // --- Quick-ask buttons: shortcuts INTO the same pipeline, not a parallel one ----------------
        public void AskSymptom(ProblemInstance p) =>
            dialogue.HandleIntent(p, PlayerIntent.AskSymptom, "Can you tell me what you're seeing?");

        public void AskStoreName(ProblemInstance p) =>
            dialogue.HandleIntent(p, PlayerIntent.AskStoreName, "Which store am I speaking to?");

        public void AskOwnerName(ProblemInstance p) =>
            dialogue.HandleIntent(p, PlayerIntent.AskOwnerName, "And who am I speaking with?");

        public void AskMachineId(ProblemInstance p) =>
            dialogue.HandleIntent(p, PlayerIntent.AskMachineId, "Which register is it?");

        public void AskAuthorized(ProblemInstance p) =>
            dialogue.HandleIntent(p, PlayerIntent.AskAuthorized, "Did the owner authorize this?");

        public void AskWhenStarted(ProblemInstance p) =>
            dialogue.HandleIntent(p, PlayerIntent.AskWhenStarted, "When did this start happening?");

        public void AskWhatTried(ProblemInstance p) =>
            dialogue.HandleIntent(p, PlayerIntent.AskWhatTried, "Have you tried anything already?");

        /// <summary>
        /// SMS receipt trick (GDD §10): a less honest persona sends the wrong receipt, and the player has
        /// to catch it on timestamp / machine / store before trusting it for a diagnosis.
        /// </summary>
        public void RequestSmsReceipt(ProblemInstance p)
        {
            dialogue.HandleIntent(p, PlayerIntent.RequestSmsReceipt, "Could you text me a copy of that receipt?");

            float honesty = p.persona.profile != null ? p.persona.profile.honesty : 0.6f;
            var machine = p.store.machines != null && p.store.machines.Length > 0 ? p.store.machines[0].machineId : "REG-1";
            bool correct = Random.value < honesty;
            p.ticket.chat.Add(new ChatLine
            {
                kind = ChatKind.Sms,
                text = correct
                    ? $"Receipt received — Store {p.store.storeId}, Machine {machine}, timestamp matches tonight."
                    : "Receipt received — Machine REG-2, timestamp from 3 days ago. (Doesn't line up — double check before trusting this.)",
            });
        }
    }
}
