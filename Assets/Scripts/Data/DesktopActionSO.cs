using UnityEngine;
using POSTechSupport.Core;

namespace POSTechSupport.Data
{
    /// <summary>
    /// One thing the player can do on the remote desktop (Diagnostic = read, Fix = write).
    /// Every action runs through ActionManager so preconditions / risky-confirm / clue reveal
    /// are applied consistently. See Docs/app.md "Action theo từng app" and Docs/schema.md §5.
    /// </summary>
    [CreateAssetMenu(menuName = "POS/DesktopAction", fileName = "Action_")]
    public class DesktopActionSO : ScriptableObject
    {
        public string actionId;
        public DesktopActionType actionType;   // links diagnostic actions to DiagnosticClue.revealedBy
        public ActionKind kind;
        public ModuleType targetModule;
        public string appKey;                  // which desktop-app window hosts this action (Docs/app.md)
        public string appTab;                  // optional sub-tab inside that app ("" = the app's default tab).
                                               // e.g. print_customer_copy targets Printer but is hosted by
                                               // POS Manager ▸ Database, because it needs transaction data.

        public StateCheck[] preconditions;     // precondition chain (all must pass to execute a Fix)
        public FaultInjection[] stateChanges;  // what a Fix writes into module state

        [TextArea] public string resultText;
        public bool isRisky;                   // MadeWorse warning + confirm before running

        // Test actions (print_test_page / print_customer_copy) run a receipt test instead of a state read.
        public bool isTest;
        public ReceiptType testReceiptType;
    }
}
