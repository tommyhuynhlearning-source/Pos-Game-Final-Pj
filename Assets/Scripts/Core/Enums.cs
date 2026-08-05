namespace POSTechSupport.Core
{
    // ============================================================================
    // All enums for POS Tech Support. See Docs/schema.md §"Enums cần định nghĩa".
    // Ported from the validated web prototype (Docs/web-prototype/app.js).
    // ============================================================================

    /// <summary>IssueSO.category — the family a problem belongs to.</summary>
    public enum IssueCategory { Terminal, POS, Printer, OS, Network, CashDrawer, Business }

    /// <summary>IssueSO.tier — difficulty gate used by ProblemGenerator's day pool.</summary>
    public enum DifficultyTier { Basic, Medium, Hard }

    /// <summary>
    /// Which simulated module a piece of state lives in. See Docs/app.md for the
    /// module ↔ desktop-app mapping and the dependency cascade order.
    /// </summary>
    public enum ModuleType { OS, POSSoftware, Terminal, Printer, CashDrawer, Network, Database, Browser }

    /// <summary>StateCheck.op — comparison operator for a data-driven state check.</summary>
    public enum ComparisonOp { Equals, NotEquals, GreaterThan, LessThan }

    /// <summary>ReceiptTemplateSO.type / test receipt kind. TestPage needs no transaction data.</summary>
    public enum ReceiptType { TestPage, Merchant, Customer, Store }

    /// <summary>DesktopActionSO.kind — Diagnostic reads state, Fix writes it.</summary>
    public enum ActionKind { Diagnostic, Fix }

    /// <summary>
    /// DiagnosticClue.revealedBy — which diagnostic action surfaces a clue.
    /// One value per Diagnostic action id in the prototype's ACTIONS table.
    /// </summary>
    public enum DesktopActionType
    {
        None,
        CheckPrintQueue,
        PrintTestPage,
        PrintCustomerCopy,
        OpenDeviceManager,
        CheckPortConfig,
        CheckNetworkStatus,
        CheckPosReceiptConfig,
        CheckTerminalNetwork,
        CheckStaffAccount,       // POS Manager ▸ Staff Mgmt — role / terminal assignment / sync
        CheckPosConnections,     // POS Manager ▸ Connections — registered terminal IP + DB host
        CheckSystemHealth,       // System ▸ Health — disk space, pending updates, clock
        CheckServices,           // System ▸ Services — Print Spooler and friends
        CheckPrinterHardware,    // Printer ▸ Queue — cable, jam, roll width (the physical layer)
        CheckDrawerHardware,     // Cash Drawer — key lock and trigger mode
        CheckTerminalPairing,    // POS Terminal ▸ Status — pairing, firmware, EMV, live/training
        CheckPosLicensing        // POS Manager ▸ Receipt — licence, tax, price sync, offline mode
    }

    /// <summary>ActiveFault.status — Latent faults are hidden behind a blocker until it clears.</summary>
    public enum FaultStatus { Latent, Active, Resolved }

    /// <summary>Result of ResolutionChecker.EvaluateIssue for a single fault.</summary>
    public enum ResolveStatus { Unresolved, Resolved, MadeWorse, Hidden }

    /// <summary>
    /// Health verdict of a whole ticket (ResolutionChecker.EvaluateTicket). Distinct from
    /// CallLifecycleStatus — see Docs/schema.md TicketState note.
    /// </summary>
    public enum TicketStatus { InProgress, Resolved, Degraded }

    /// <summary>Where a call is in its lifecycle. Distinct from the health verdict above.</summary>
    public enum CallLifecycleStatus { Queued, Ringing, Active, Closed, Missed, Abandoned }

    /// <summary>
    /// Final closed sub-state carried into the night's call log (prototype's "Closed-*").
    /// Unauthorized = correctly refused an unverified caller (no strike, no resolved credit).
    /// </summary>
    public enum ClosedOutcome { None, Resolved, Degraded, Unauthorized }

    /// <summary>The ONE source of truth for who is on the phone. See Docs/app.md Caller Authorization.</summary>
    public enum CallerRole { Owner, Staff }

    /// <summary>Identity fact usable in the click-to-compare mechanic (CRM field ↔ chat statement).</summary>
    public enum FactType { StoreName, OwnerName, MachineId }

    /// <summary>Which side of a click-to-compare selection a fact came from.</summary>
    public enum CompareSource { Crm, Chat }

    /// <summary>Outcome of a click-to-compare (only exists after the player compares two facts).</summary>
    public enum CompareResult { None, Match, Mismatch }

    /// <summary>ChatLine.kind — a line in the customer conversation panel.</summary>
    public enum ChatKind { Customer, Agent, System, Sms }

    /// <summary>SessionLogLine.kind — a line in the remote-session diagnostic log.</summary>
    public enum SessionLogKind { Clue, RedHerring, Result, System }

    /// <summary>Transaction type on the terminal / in the batch.</summary>
    public enum TransType { Sale, Refund, Void }

    /// <summary>Lifecycle of a single transaction (Open in batch → Settled after close).</summary>
    public enum TransStatus { Open, Voided, Settled, Refunded }

    /// <summary>Lifecycle of a settlement batch.</summary>
    public enum BatchStatus { Open, Closed, SettleFailed }

    /// <summary>Category of a HarmEvent → drives the complaint mail filed by MailboxManager.</summary>
    public enum HarmType { MissedCall, DegradedTicket, AbandonedCall, UnauthorizedTransaction, MadeWorse }

    /// <summary>
    /// EffectiveStatus of a module AFTER dependency resolution (DependencyGraph).
    /// Blocked = an upstream module is broken (clues hidden until fixed);
    /// Error = this module's own local fault (must stay diagnosable). See Docs/app.md §7.
    /// </summary>
    public enum Status { OK, Error, Blocked }

    /// <summary>Overall campaign result checked after each night.</summary>
    public enum GameResult { None, Win, Lose }
}
