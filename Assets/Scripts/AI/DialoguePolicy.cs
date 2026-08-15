using POSTechSupport.Core;
using POSTechSupport.Logic;

namespace POSTechSupport.AI
{
    public enum DialogueActKind
    {
        Greet, StateSymptom, GiveFact, ReCheckFact, ReadSessionCode, ConfirmAuthorized, AdmitUnauthorized,
        DeflectTechnical, ReportWhenStarted, ReportWhatTried, ComplyWithInstruction,
        SmsReceipt, Acknowledge, Confused, Goodbye,
    }

    /// <summary>
    /// What the policy DECIDED to say, before anyone words it. content is the safe template phrasing;
    /// the LLM may only rephrase it, never change what it means (GDD nguyên tắc bất biến #7).
    /// </summary>
    public class DialogueAct
    {
        public DialogueActKind kind;
        public string content;
        public FactRef fact;          // set on GiveFact → keeps click-to-compare working
        public bool endsCall;         // unauthorized caller hangs up on themselves
    }

    /// <summary>
    /// Step 2 — the BRAIN (GDD §9, nguyên tắc bất biến #7). It reads only <see cref="GroundTruth"/> plus
    /// the conversation state and decides what may be said now. Every trick in the game lives here:
    /// the customer misremembers, deflects jargon, and answers the authorization question the same way
    /// every time because the answer is fixed ground truth, not a coin flip per ask.
    ///
    /// It has no access to the desktop, the issue, or the verdict — by construction, not by discipline.
    /// </summary>
    public class DialoguePolicy
    {
        private readonly System.Random rng;
        public DialoguePolicy(System.Random rng) { this.rng = rng; }

        public DialogueAct Decide(GroundTruth truth, DialogueState state, PlayerIntent intent)
        {
            state.turnCount++;

            // --- KnowledgeBoundary: the hard stop -------------------------------------------------
            // A non-technical caller cannot answer a technical question, no matter how the agent phrases
            // it or how many times they ask. This is the wall that keeps the answer out of the chat.
            if (intent == PlayerIntent.AskTechnical)
            {
                state.patience -= 0.15f;
                return new DialogueAct { kind = DialogueActKind.DeflectTechnical, content = DeflectLine(truth, state) };
            }

            switch (intent)
            {
                case PlayerIntent.Greeting:
                    state.greeted = true;
                    return Act(DialogueActKind.Greet, $"Hi — this is {truth.callerName}. Thanks for picking up.");

                case PlayerIntent.AskSymptom:
                    return Act(DialogueActKind.StateSymptom, SymptomLine(truth, state));

                case PlayerIntent.AskStoreName:
                    return Fact(FactType.StoreName, truth.statedStoreName, $"We're {truth.statedStoreName}.");

                case PlayerIntent.AskOwnerName:
                    return truth.callerRole == CallerRole.Owner
                        ? Fact(FactType.OwnerName, truth.statedOwnerName, $"That's me — {truth.statedOwnerName}.")
                        : Fact(FactType.OwnerName, truth.statedOwnerName, $"The owner's {truth.statedOwnerName}. I just work here.");

                case PlayerIntent.AskMachineId:
                    return Fact(FactType.MachineId, truth.statedMachineId,
                        $"Uh… I think it says {truth.statedMachineId} on the side?");

                case PlayerIntent.AskDoubleCheck:
                    return DoubleCheckAnswer(truth, state);

                // They read a number off their own screen. Not a diagnosis, not a fact to compare —
                // just the one credential the CRM cannot hold, because it is generated per session.
                case PlayerIntent.AskSessionCode:
                    state.lastWasSessionCode = true;
                    return Act(DialogueActKind.ReadSessionCode,
                        string.IsNullOrEmpty(truth.sessionCode)
                            ? "There's nothing on the screen about a code, sorry."
                            : Pick($"Hang on… it says {Spaced(truth.sessionCode)}.",
                                   $"Yeah, I've got it here — {Spaced(truth.sessionCode)}.",
                                   $"It's, uh… {Spaced(truth.sessionCode)}. Did I read that right?"));

                case PlayerIntent.AskAuthorized:
                    return AuthorizationAnswer(truth, state);

                case PlayerIntent.AskWhenStarted:
                    return Act(DialogueActKind.ReportWhenStarted, WhenStartedLine(truth));

                case PlayerIntent.AskWhatTried:
                    return Act(DialogueActKind.ReportWhatTried, WhatTriedLine(truth));

                case PlayerIntent.InstructCustomer:
                    // They comply, and report only what they can SEE — which is the symptom again.
                    // Never a state readout: that would hand over a diagnosis the caller cannot make.
                    return Act(DialogueActKind.ComplyWithInstruction, ComplyLine(truth));

                case PlayerIntent.RequestSmsReceipt:
                    return Act(DialogueActKind.SmsReceipt, "Okay, hang on, I'll send it over.");

                case PlayerIntent.Reassure:
                    return Act(DialogueActKind.Acknowledge, Pick("Sure, no rush.", "Okay.", "Yeah, take your time."));

                case PlayerIntent.Goodbye:
                    state.saidGoodbye = true;
                    return Act(DialogueActKind.Goodbye, Pick("Thanks for your help.", "Alright, thank you.", "Okay — bye."));

                default:
                    state.patience -= 0.05f;
                    return Act(DialogueActKind.Confused, ConfusedLine(truth, state));
            }
        }

        // --- individual decisions ------------------------------------------------------------------

        /// <summary>
        /// Fixed ground truth, answered identically every time — a real person doesn't change their story
        /// between asks. An unauthorized caller admits it and hangs up (Docs/app.md Caller Authorization).
        /// </summary>
        /// <summary>
        /// "Are you sure?" — the caller stops answering from memory and goes and LOOKS. What comes back
        /// is always the truth, because they are reading it off their own shop.
        ///
        /// This is the counterweight to memoryAccuracy. A caller who names the shop two doors down sends
        /// the player to a CRM record that looks perfect and a remote connect that then fails for no
        /// visible reason; without a way to push back, that ticket is unwinnable through no fault of the
        /// player. The challenge stays intact — nothing prompts the agent to doubt an answer, and an
        /// agent who never doubts still walks into the wrong account.
        /// </summary>
        private DialogueAct DoubleCheckAnswer(GroundTruth truth, DialogueState state)
        {
            // Doubting the code is the commonest thing to try when a connect fails, and the honest
            // answer is that they are reading it off the screen in front of them — so it never changes.
            // Saying "sure about what?" to a question the player just asked reads as the game not
            // listening, and sends them looking for the fault in the one place it isn't.
            if (state.lastWasSessionCode)
                return Act(DialogueActKind.ReadSessionCode,
                    string.IsNullOrEmpty(truth.sessionCode)
                        ? "There's still nothing on the screen, sorry."
                        : Pick($"Yeah — I'm looking right at it. {Spaced(truth.sessionCode)}.",
                               $"Positive, it's {Spaced(truth.sessionCode)}. It's right here on the screen."));

            if (state.lastFact == null)
                return Act(DialogueActKind.Confused,
                    Pick("Sure about what, sorry?", "Sorry — sure about what?"));

            var type = state.lastFact.Value;
            state.patience -= 0.05f;
            bool again = !state.reChecked.Add(type);      // Add is false when it was already checked

            (string stated, string truthValue) = type switch
            {
                FactType.StoreName => (truth.statedStoreName, truth.trueStoreName),
                FactType.OwnerName => (truth.statedOwnerName, truth.trueOwnerName),
                _                  => (truth.statedMachineId, truth.trueMachineId),
            };

            bool wasWrong = !string.Equals(stated, truthValue, System.StringComparison.OrdinalIgnoreCase);
            string line = again ? AlreadyChecked(type, truthValue)
                        : wasWrong ? Corrects(type, truthValue, truth)
                        : Confirms(type, truthValue, truth);

            return new DialogueAct
            {
                kind = DialogueActKind.ReCheckFact,
                content = line,
                fact = new FactRef { type = type, value = truthValue },
            };
        }

        /// <summary>They go and read it, and it is not what they said a minute ago.</summary>
        private string Corrects(FactType type, string value, GroundTruth truth) => type switch
        {
            FactType.StoreName => Pick(
                $"Hang on… let me look at the paperwork. Oh — it's {value}. Sorry, I've been here two weeks.",
                $"Hold on, I'll check the sign out front… ah, {value}. My mistake."),
            FactType.OwnerName => truth.callerRole == CallerRole.Owner
                ? $"Oh — sorry, yes, {value}. Long night."
                : Pick($"Let me check the licence on the wall… it says {value}. Sorry, I got that wrong.",
                       $"Hang on — no, it's {value}. I always mix that up."),
            _ => Pick($"Hold on, let me look at the side of it… it says {value}.",
                      $"Let me squint at the sticker… {value}. Sorry, wrong one."),
        };

        /// <summary>They go and read it, and it is exactly what they said. A dead end, honestly given.</summary>
        private string Confirms(FactType type, string value, GroundTruth truth) => type switch
        {
            FactType.StoreName => Pick($"Yeah, positive — {value}.", $"Certain. It's {value}, been that way for years."),
            FactType.OwnerName => truth.callerRole == CallerRole.Owner
                ? $"Yes — that's me, {value}."
                : Pick($"Yeah, I checked — {value}.", $"Positive. {value}, that's the boss."),
            _ => Pick($"Yeah, I just looked — {value}.", $"Checked it. {value}."),
        };

        private string AlreadyChecked(FactType type, string value) => Pick(
            $"Like I said — {value}.",
            $"Same as before, {value}.",
            $"I did check. {value}.");

        private DialogueAct AuthorizationAnswer(GroundTruth truth, DialogueState state)
        {
            state.answered.Add(nameof(PlayerIntent.AskAuthorized));

            if (truth.callerRole == CallerRole.Owner)
                return Act(DialogueActKind.ConfirmAuthorized,
                    "I'm the owner — this is my place, I don't need anyone's okay for this.");

            if (truth.callerAuthorized)
                return Act(DialogueActKind.ConfirmAuthorized,
                    $"Yeah, {truth.statedOwnerName} told me to call about this — should be fine.");

            return new DialogueAct
            {
                kind = DialogueActKind.AdmitUnauthorized,
                content = "Uh — no, I didn't actually check with them first…",
                endsCall = true,
            };
        }

        private string SymptomLine(GroundTruth truth, DialogueState state)
        {
            if (truth.visibleSymptoms.Count == 0) return "Something's just not right with it, honestly.";
            // Repeat asks get the same observation reworded, never a new fact — they only know what they see.
            int idx = state.answered.Contains(nameof(PlayerIntent.AskSymptom))
                ? rng.Next(truth.visibleSymptoms.Count)
                : 0;
            state.answered.Add(nameof(PlayerIntent.AskSymptom));
            return truth.Misname(truth.visibleSymptoms[idx], rng);
        }

        private string WhenStartedLine(GroundTruth truth) =>
            truth.Cooperativeness >= 0.5f
                ? Pick("It started sometime tonight, I think. It was fine earlier.",
                       "Maybe an hour ago? We noticed it during the rush.",
                       "Since we opened up this evening, pretty much.")
                : Pick("I don't know, a while?", "Honestly I wasn't watching the clock.");

        private string WhatTriedLine(GroundTruth truth) =>
            truth.Cooperativeness >= 0.5f
                ? Pick("We turned it off and on again, that's about it.",
                       "I wiggled the cable. Nothing changed.",
                       "Nothing really — I didn't want to make it worse.")
                : Pick("That's your job, isn't it?", "No. I called you.");

        private string ComplyLine(GroundTruth truth)
        {
            string baseLine = Pick("Okay… hang on… no, looks the same to me.",
                                   "Alright, doing it now… nope, still nothing.",
                                   "Hold on… yeah, I don't see any difference.");
            return truth.EmotionalState > 0.6f ? baseLine + " Look, we've got people waiting." : baseLine;
        }

        private string DeflectLine(GroundTruth truth, DialogueState state)
        {
            if (state.patience < 0.4f)
                return Pick("I really don't know any of that. That's why I called you.",
                            "You're asking the wrong person, honestly.");
            return Pick("Sorry — I wouldn't know about that side of it.",
                        "Ah, that's over my head. Can you have a look yourself?",
                        "No idea, I just work the counter.");
        }

        private string ConfusedLine(GroundTruth truth, DialogueState state) =>
            state.patience < 0.4f
                ? Pick("I'm not following you.", "Sorry, what do you mean?")
                : Pick("Sorry — say that again?", "Hmm? Not sure what you're asking.");

        // --- helpers ---------------------------------------------------------------------------------
        private static DialogueAct Act(DialogueActKind kind, string content) =>
            new() { kind = kind, content = content };

        private static DialogueAct Fact(FactType type, string value, string content) =>
            new() { kind = DialogueActKind.GiveFact, content = content, fact = new FactRef { type = type, value = value } };

        private string Pick(params string[] options) => options[rng.Next(options.Length)];

        /// <summary>"52337" → "5 2 3 3 7" — how somebody actually reads a code down a phone line.</summary>
        private static string Spaced(string code) => string.Join(" ", code.ToCharArray());
    }
}
