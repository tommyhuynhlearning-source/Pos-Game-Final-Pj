using System.Collections.Generic;
using POSTechSupport.Core;
using POSTechSupport.Logic;

namespace POSTechSupport.Managers
{
    public class ScoreBreakdown
    {
        public int resolvedCount;
        public int degradedCount;
        public int currencyEarned;   // resolved*10 - degraded*15, clamped >= 0 (prototype formula)
    }

    /// <summary>
    /// Converts a night's closed tickets into currency. Simple linear formula for now; GDD §11 hints at
    /// a richer breakdown (root-cause bonus, extra-steps penalty, time…) — extend ScoreBreakdown later.
    /// Docs/manager.md ScoreManager.
    /// </summary>
    public class ScoreManager
    {
        public ScoreBreakdown Compute(List<ProblemInstance> nightHistory)
        {
            var b = new ScoreBreakdown();
            foreach (var p in nightHistory)
            {
                if (p.ticket.closedOutcome == ClosedOutcome.Resolved) b.resolvedCount++;
                else if (p.ticket.closedOutcome == ClosedOutcome.Degraded) b.degradedCount++;
            }
            b.currencyEarned = b.resolvedCount * 10 - b.degradedCount * 15;
            if (b.currencyEarned < 0) b.currencyEarned = 0;
            return b;
        }
    }
}
