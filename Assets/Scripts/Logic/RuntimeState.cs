using System.Collections.Generic;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Simulation;

namespace POSTechSupport.Logic
{
    // ============================================================================
    // Runtime state classes (NOT ScriptableObjects). See Docs/schema.md §6.
    // Assembled by ProblemGenerator from the static SOs + a VirtualDesktopInstance;
    // runtime state is never written back into an asset (nguyên tắc bất biến #6).
    // ============================================================================

    /// <summary>One customer-chat line. fact is optional — enables click-to-compare.</summary>
    public class ChatLine
    {
        public ChatKind kind;
        public string text;
        public FactRef fact;
    }

    public class FactRef
    {
        public FactType type;
        public string value;
    }

    /// <summary>One line in the remote-session diagnostic log (clues, herrings, action results).</summary>
    public class SessionLogLine
    {
        public SessionLogKind kind;
        public string text;
    }

    /// <summary>
    /// The persona instantiated for THIS ticket: a static profile + who's actually on the phone.
    /// role is the single source of truth for identity; stated* may be wrong per memoryAccuracy.
    /// See Docs/schema.md §6 and Docs/app.md Caller Authorization.
    /// </summary>
    public class PersonaInstance
    {
        public PersonaProfileSO profile;
        public CallerRole role;
        public string name;
        public string statedStoreName;
        public string statedOwnerName;
        public string statedMachineId;
    }

    public class CrmLookupState
    {
        public string query = "";
        public List<StoreRecord> results = new();
        public int selectedIndex = -1;
    }

    public class CompareState
    {
        public FactRef pending;                       // one field selected, awaiting its counterpart
        public FactType pendingType;
        public CompareSource pendingSource;
        public CompareResult result = CompareResult.None;
        public FactType resultType;
        public string crmValue;
        public string chatValue;
    }

    public class AuthorizationState
    {
        public bool isRefundVoidCase;         // ~40% of tickets carry real authorization risk
        public bool callerAuthorized = true;  // ground truth (rolled 50/50 only on refund/void cases)
        public bool confirmed;                // established by the PLAYER (owner-name MATCH, or asking)
        public bool asked;
        public bool customerHungUp;
        public bool unauthorizedActionTaken;  // Refund/Void done unconfirmed → caps verdict at Degraded
    }

    public class RemoteConnectState
    {
        public string passcode;               // one-time session code, fresh per ticket
        public string queryId = "";
        public string queryPass = "";
        public bool connected;
        public bool connectFailed;
    }

    /// <summary>
    /// Conversation memory for one call (GDD §9 DialogueState). Lives on the ticket, not in the AI, so
    /// the customer stays consistent across turns and the policy can tell a first ask from a fifth.
    /// </summary>
    public class DialogueState
    {
        public bool greeted;
        public int turnCount;
        public float patience = 1f;                   // drains with repeated/jargon questions
        public HashSet<string> answered = new();      // intents already answered at least once
        public bool saidGoodbye;
    }

    /// <summary>Two independent verification layers: right STORE (CRM) vs right PERSON (identity).</summary>
    public class VerificationState
    {
        public bool storeVerified;
        public bool identityVerified;
        public bool machineVerified;
        public bool CanGrantRemote() => storeVerified;   // remote just needs the right record's creds
    }

    /// <summary>A fault that's live in a ticket, with its resolution lifecycle + blocker tracking.</summary>
    public class ActiveFault
    {
        public IssueSO issue;
        public FaultStatus status = FaultStatus.Active;
        public List<string> blockedBy = new();
    }

    /// <summary>
    /// State of one running call/ticket. lifecycle (call vitae) and verdict (health) are two
    /// distinct enums even though the names sound alike — see Docs/schema.md §6.
    /// </summary>
    public class TicketState
    {
        public string ticketId;
        public int day;

        public CallLifecycleStatus lifecycle = CallLifecycleStatus.Queued;
        public ClosedOutcome closedOutcome = ClosedOutcome.None;
        public TicketStatus verdict = TicketStatus.InProgress;

        public List<ChatLine> chat = new();
        public List<SessionLogLine> sessionLog = new();

        public CrmLookupState crmLookup = new();
        public CompareState compare = new();
        public AuthorizationState authorization = new();
        public RemoteConnectState remoteConnect = new();
        public DialogueState dialogue = new();

        // Onboarding guidance auto-attached during the first pool tier; null afterwards, when the
        // player must open the Knowledge Base app themselves (Docs/manager.md KnowledgeBaseManager).
        public KnowledgeArticleSO attachedArticle;

        public string openAppKey;                     // which desktop app is open, null if none
        public HashSet<string> revealedActions = new();
        public Dictionary<string, string> appTabs = new();

        // scheduling bookkeeping (ShiftManager / TicketManager)
        public float ringDeadline;
        public float answeredAtElapsed;
    }

    /// <summary>
    /// The complete assembled problem for one ticket (Docs/schema.md §6 dependency map).
    /// This is the only object services pass around; ProblemGenerator is the only thing that builds it.
    /// </summary>
    public class ProblemInstance
    {
        public IssueSO[] issues;
        public StoreRecord store;              // the account on the phone THIS ticket
        public PersonaInstance persona;
        public VirtualDesktopInstance desktop;
        public List<ActiveFault> faults = new();
        public VerificationState verification = new();
        public TransactionState transactions = new();
        public TicketState ticket = new();

        // CRM directory this ticket searches against — shared across tickets, never mutated.
        public List<StoreRecord> crmDirectory = new();

        /// <summary>
        /// Is this CRM row the account actually on the phone? Asked by identity rather than by a flag on
        /// the record, because the directory is shared: tonight's caller is tomorrow's decoy.
        /// </summary>
        public bool IsCallerRecord(StoreRecord rec) =>
            rec != null && store != null &&
            (ReferenceEquals(rec, store) || (!string.IsNullOrEmpty(rec.storeId) && rec.storeId == store.storeId));
    }
}
