using System.Linq;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Logic;
using POSTechSupport.Simulation;

namespace POSTechSupport.Managers
{
    public class ActionResult
    {
        public string resultText;
        public bool triggeredMadeWorse;
        public bool wasRisky;
    }

    /// <summary>
    /// Every player action on the desktop routes through here so preconditions / risky-confirm / clue
    /// reveal stay consistent (Docs/manager.md ActionManager). Ported from the prototype's runAction /
    /// autoRevealApp / revealClueOnce. Risky confirm itself is a UI concern — check <c>action.isRisky</c>
    /// and confirm before calling RunAction.
    /// </summary>
    public class ActionManager
    {
        private readonly ContentDatabaseSO content;
        private readonly DesktopManager desktop;

        public ActionManager(ContentDatabaseSO content, DesktopManager desktop)
        {
            this.content = content;
            this.desktop = desktop;
        }

        public DesktopActionSO Find(string actionId) =>
            content.allActions?.FirstOrDefault(a => a != null && a.actionId == actionId);

        /// <summary>A Fix is executable only when its target isn't Blocked and all preconditions pass.</summary>
        public bool CanExecute(VirtualDesktopInstance d, DesktopActionSO action)
        {
            if (d.EffectiveStatus(action.targetModule).status == Status.Blocked) return false;
            if (action.kind == ActionKind.Fix && action.preconditions != null)
                return action.preconditions.All(d.graph.CheckState);
            return true;
        }

        public ActionResult RunAction(ProblemInstance p, string actionId)
        {
            var action = Find(actionId);
            if (action == null) return new ActionResult { resultText = "Unknown action." };
            var result = new ActionResult { wasRisky = action.isRisky };

            if (action.kind == ActionKind.Diagnostic)
            {
                bool any = RevealForAction(p, action);
                if (action.isTest)
                {
                    bool pass = p.desktop.graph.RunTest(action.testReceiptType);
                    string reason = "";
                    if (!pass)
                    {
                        var pr = p.desktop.EffectiveStatus(ModuleType.Printer);
                        if (!pr.IsOk) reason = pr.reason;
                    }
                    Log(p, SessionLogKind.Result, $"{action.actionId}: {(pass ? "PASS" : "FAIL")}{(pass ? "" : " — " + reason)}");
                    any = true;
                }
                if (!any) Log(p, SessionLogKind.Result, Describe(action, "nothing unusual found."));
                result.resultText = p.desktop.Identity.Resolve(Describe(action, "Diagnostic complete."));
            }
            else // Fix
            {
                if (action.stateChanges != null)
                    foreach (var c in action.stateChanges) desktop.ApplyChange(p.desktop, c);
                Log(p, SessionLogKind.Result, Describe(action, "applied."));
                desktop.OnFixApplied(p);
                result.triggeredMadeWorse = ResolutionChecker.EvaluateTicket(p) == TicketStatus.Degraded;
                result.resultText = p.desktop.Identity.Resolve(Describe(action, "Fix applied."));
            }
            return result;
        }

        /// <summary>Reveal a diagnostic action's clues once per ticket (from any app that triggers it).</summary>
        public bool RevealForAction(ProblemInstance p, DesktopActionSO action)
        {
            if (p.ticket.revealedActions.Contains(action.actionId)) return false;
            p.ticket.revealedActions.Add(action.actionId);
            bool any = false;
            foreach (var issue in p.issues)
            {
                if (issue.clues == null) continue;
                foreach (var clue in issue.clues.Where(c => c.revealedBy == action.actionType))
                {
                    Log(p, clue.isRedHerring ? SessionLogKind.RedHerring : SessionLogKind.Clue, clue.clueText);
                    any = true;
                }
            }
            return any;
        }

        /// <summary>
        /// On opening an app (or switching sub-tab), auto-reveal the non-test diagnostics hosted there,
        /// unless the module is Blocked. Actions with no appTab belong to every tab of their app.
        /// </summary>
        public void AutoRevealApp(ProblemInstance p, string appKey, string appTab = null)
        {
            var actions = content.allActions?
                .Where(a => a != null && a.appKey == appKey && a.kind == ActionKind.Diagnostic && !a.isTest &&
                            (appTab == null || string.IsNullOrEmpty(a.appTab) || a.appTab == appTab));
            if (actions == null) return;
            foreach (var a in actions)
            {
                if (p.desktop.EffectiveStatus(a.targetModule).status == Status.Blocked) continue;
                RevealForAction(p, a);
            }
        }

        /// <summary>Authored resultText wins; the fallback keeps the log readable for un-authored actions.</summary>
        private static string Describe(DesktopActionSO action, string fallback) =>
            string.IsNullOrWhiteSpace(action.resultText)
                ? $"{action.actionId}: {fallback}"
                : action.resultText;

        /// <summary>
        /// Chokepoint #3 for token substitution: every clue, test result and action line the player reads
        /// passes through here, so authored text names THIS shop's network rather than a hardcoded one.
        /// </summary>
        private static void Log(ProblemInstance p, SessionLogKind kind, string text) =>
            p.ticket.sessionLog.Add(new SessionLogLine { kind = kind, text = p.desktop.Identity.Resolve(text) });
    }
}
