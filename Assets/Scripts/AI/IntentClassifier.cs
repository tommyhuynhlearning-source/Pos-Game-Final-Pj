using System.Collections.Generic;
using System.Linq;

namespace POSTechSupport.AI
{
    /// <summary>What the support agent is trying to do with this line (GDD §9 step 1).</summary>
    public enum PlayerIntent
    {
        Unknown,
        Greeting,
        AskSymptom,        // "what's going on", "tell me what you're seeing"
        AskStoreName,
        AskOwnerName,
        AskMachineId,
        AskDoubleCheck,    // "are you sure?" — makes them go and CHECK the last fact they stated
        AskSessionCode,    // "read me the code on your screen" — the remote passcode, which only they can see
        AskAuthorized,     // "did the owner OK this?"
        AskWhenStarted,    // repro: when / how often
        AskWhatTried,      // "have you tried anything?"
        InstructCustomer,  // "go look at the tray", "unplug it and back in"
        RequestSmsReceipt,
        AskTechnical,      // root-cause / jargon — KnowledgeBoundary blocks this one
        Reassure,          // "one moment", "I'm looking into it"
        Goodbye,
    }

    public class ParsedUtterance
    {
        public PlayerIntent intent;
        public string rawText;
    }

    /// <summary>
    /// Step 1 of the pipeline. Deliberately a deterministic keyword matcher, not a model: GDD §13
    /// Phương án A puts NLU on a small local model or plain rules, and rules cost nothing, never need a
    /// download, and can be unit-tested. An LLM classifier can replace this by implementing the same
    /// method — the three layers below it don't care where the intent came from.
    ///
    /// Order matters: technical detection runs FIRST so "did you check the printer driver" is caught as
    /// AskTechnical rather than matching the friendlier "printer" rule underneath it.
    /// </summary>
    public class IntentClassifier
    {
        public ParsedUtterance Classify(string text)
        {
            string t = (text ?? "").ToLowerInvariant().Trim();
            return new ParsedUtterance { rawText = text, intent = Detect(t) };
        }

        private static PlayerIntent Detect(string t)
        {
            if (t.Length == 0) return PlayerIntent.Unknown;

            // 1. Jargon first — the whole point is that the customer cannot follow this.
            if (TechnicalVocabulary.ContainsAny(t)) return PlayerIntent.AskTechnical;

            if (Any(t, "bye", "goodbye", "that's all", "have a good", "take care")) return PlayerIntent.Goodbye;
            if (Any(t, "hello", "hi ", "hey", "good evening", "thanks for calling", "support here")) return PlayerIntent.Greeting;

            // Before the identity questions and before InstructCustomer: "are you sure about that?" is
            // doubting the answer just given, not a fresh question and not an instruction to go look at
            // hardware. It is the player's only route back from a caller who misremembers.
            if (Any(t, "are you sure", "you sure", "sure about", "double check", "double-check",
                       "check again", "look again", "certain", "positive about", "read it out"))
                return PlayerIntent.AskDoubleCheck;

            // Before InstructCustomer: "read me the code on your screen" is asking them to relay a number,
            // not telling them to go and poke at the hardware.
            if (Any(t, "read me the code", "read the code", "code on your screen", "code on screen",
                       "session code", "what code", "the code it", "connection code", "read it out to me"))
                return PlayerIntent.AskSessionCode;

            if (Any(t, "authoriz", "authoris", "owner ok", "owner approve", "permission to", "allowed to do"))
                return PlayerIntent.AskAuthorized;
            if (Any(t, "store name", "which store", "what shop", "name of the shop", "business name"))
                return PlayerIntent.AskStoreName;
            if (Any(t, "your name", "who am i", "owner's name", "who owns", "speaking with"))
                return PlayerIntent.AskOwnerName;
            if (Any(t, "machine id", "register number", "which register", "which till", "reg-"))
                return PlayerIntent.AskMachineId;

            if (Any(t, "receipt by text", "text me the receipt", "send the receipt", "sms the receipt", "send me a copy"))
                return PlayerIntent.RequestSmsReceipt;

            if (Any(t, "when did", "how long", "since when", "every time", "how often", "start happening"))
                return PlayerIntent.AskWhenStarted;
            if (Any(t, "have you tried", "did you try", "anything you", "already tried", "what have you done"))
                return PlayerIntent.AskWhatTried;

            if (Any(t, "can you check", "go look", "have a look", "take a look", "press", "unplug", "plug it",
                       "turn it off", "switch it", "open the", "lift the"))
                return PlayerIntent.InstructCustomer;

            if (Any(t, "one moment", "hold on", "bear with", "i'm looking", "let me check", "give me a"))
                return PlayerIntent.Reassure;

            if (Any(t, "what's wrong", "whats wrong", "what happened", "what's going on", "describe",
                       "tell me", "what are you seeing", "what do you see", "problem"))
                return PlayerIntent.AskSymptom;

            return PlayerIntent.Unknown;
        }

        private static bool Any(string haystack, params string[] needles) =>
            needles.Any(haystack.Contains);
    }

    /// <summary>
    /// The shared jargon list, used twice on purpose: the classifier uses it to spot a question the
    /// customer cannot answer, and GroundingGuard uses it to make sure no reply ever contains one of
    /// these words. One list, so the two can never drift apart.
    /// </summary>
    public static class TechnicalVocabulary
    {
        public static readonly string[] Banned =
        {
            "driver", "firewall", "spooler", "spool", "service", "config", "configuration", "registry",
            "permission", "role assignment", "port", "com3", "com4", "dns", "subnet", "gateway",
            "ip address", "dhcp", "ssid", "tls", "certificate", "handshake", "batch settlement",
            "settle the batch", "template", "field mapping", "protocol", "reinstall", "device manager",
            "print queue", "queue", "cache", "log file", "disk space", "system clock", "ntp",
            "credentials", "authentication", "encryption", "firmware", "patch", "kernel",
        };

        public static bool ContainsAny(string lowerText) =>
            Banned.Any(lowerText.Contains);

        /// <summary>Which banned term tripped, for the guard's rejection log. Null if clean.</summary>
        public static string FirstHit(string lowerText)
        {
            foreach (var w in Banned) if (lowerText.Contains(w)) return w;
            return null;
        }

        public static IEnumerable<string> All => Banned;
    }
}
