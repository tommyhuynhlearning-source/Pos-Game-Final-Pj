using System.Linq;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Simulation;

namespace POSTechSupport.Logic
{
    /// <summary>
    /// Pure, stateless verdict logic (GDD §8, Docs/manager.md ResolutionChecker). The verdict is
    /// ALWAYS recomputed from current state — never stored and forgotten. Ported from the
    /// prototype's evaluateIssue / evaluateTicket.
    /// </summary>
    public static class ResolutionChecker
    {
        /// <summary>Verdict for a single issue against the current desktop state.</summary>
        public static ResolveStatus EvaluateIssue(VirtualDesktopInstance desktop, IssueSO issue)
        {
            var faultModule = issue.faults != null && issue.faults.Length > 0
                ? issue.faults[0].module
                : ModuleType.OS;

            // Blocked upstream → this issue's clues are hidden, so it isn't graded yet.
            if (desktop.EffectiveStatus(faultModule).status == Status.Blocked)
                return ResolveStatus.Hidden;

            bool rootOk = issue.resolution.rootCauseFixed == null ||
                          issue.resolution.rootCauseFixed.All(desktop.graph.CheckState);
            bool testOk = !issue.resolution.requiresTestPass ||
                          desktop.graph.RunTest(issue.resolution.testReceiptType);

            if (rootOk && testOk) return ResolveStatus.Resolved;
            if (HasWorseningFault(desktop, issue)) return ResolveStatus.MadeWorse;
            return ResolveStatus.Unresolved;
        }

        /// <summary>
        /// A player mis-step is present when any of the issue's worseningFaults is currently applied.
        /// Generalizes the prototype's P2/connection==Removed special case.
        /// </summary>
        private static bool HasWorseningFault(VirtualDesktopInstance desktop, IssueSO issue)
        {
            if (issue.worseningFaults == null) return false;
            foreach (var w in issue.worseningFaults)
                if (desktop.GetModule(w.module)?.Get(w.stateField) == desktop.Identity.Resolve(w.faultValue))
                    return true;
            return false;
        }

        /// <summary>Overall ticket health verdict.</summary>
        public static TicketStatus EvaluateTicket(ProblemInstance p)
        {
            // Unauthorized Refund/Void is a business-logic harm — caps the ticket at Degraded
            // regardless of how cleanly the technical issues were fixed.
            if (p.ticket.authorization.unauthorizedActionTaken) return TicketStatus.Degraded;

            var statuses = p.issues.Select(i => EvaluateIssue(p.desktop, i)).ToList();
            if (statuses.Contains(ResolveStatus.Hidden)) return TicketStatus.InProgress;
            if (statuses.All(s => s == ResolveStatus.Resolved)) return TicketStatus.Resolved;
            if (statuses.Contains(ResolveStatus.MadeWorse)) return TicketStatus.Degraded;
            return TicketStatus.InProgress;
        }
    }
}
