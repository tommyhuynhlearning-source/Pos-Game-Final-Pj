using System;
using System.Collections.Generic;
using System.Linq;
using POSTechSupport.Core;
using POSTechSupport.Logic;
using POSTechSupport.Simulation;

namespace POSTechSupport.Managers
{
    [Serializable]
    public class RecurringFault
    {
        public string issueId;
        public int dueDay;
    }

    /// <summary>Persistent ledger of cross-night consequences (lives in the campaign save).</summary>
    [Serializable]
    public class ConsequenceLedger
    {
        public List<RecurringFault> pendingRecurring = new();
        public float trust = 0.5f;                       // optional (GĐ2) — persona tone/patience
        public List<string> narrativeFlags = new();
    }

    /// <summary>
    /// The only source of cross-night consequences (Docs/manager.md ConsequenceManager). At end of night
    /// it flags "temp fix" tickets (symptomCleared met, rootCauseFixed not) to recur on a later day;
    /// ProblemGenerator asks DueRecurringToday() before rolling the normal pool.
    /// NOTE: not present in the web prototype — new for the Unity build (structural stub, tune later).
    /// </summary>
    public class ConsequenceManager
    {
        public ConsequenceLedger ledger = new();

        public void Commit(List<ProblemInstance> nightHistory, int day)
        {
            foreach (var p in nightHistory)
            {
                // Only tickets the player actually worked can leave a temp fix behind. Missed calls and
                // ones another tech took were never touched, and at a high call volume they are the
                // majority of the night's history.
                if (p.ticket.lifecycle is not (CallLifecycleStatus.Closed or CallLifecycleStatus.Abandoned))
                    continue;
                foreach (var issue in p.issues)
                {
                    bool symptomCleared = issue.resolution.symptomCleared == null ||
                                          issue.resolution.symptomCleared.All(p.desktop.graph.CheckState);
                    bool rootFixed = issue.resolution.rootCauseFixed == null ||
                                     issue.resolution.rootCauseFixed.All(p.desktop.graph.CheckState);
                    if (symptomCleared && !rootFixed && !ledger.pendingRecurring.Any(r => r.issueId == issue.issueId))
                        ledger.pendingRecurring.Add(new RecurringFault { issueId = issue.issueId, dueDay = day + 1 });
                }
            }
        }

        public List<string> DueRecurringToday(int day) =>
            ledger.pendingRecurring.Where(r => r.dueDay <= day).Select(r => r.issueId).ToList();

        /// <summary>Clears a pending recurrence once RecurringProblemFactory has spawned it.</summary>
        public void ConsumeRecurring(string issueId) =>
            ledger.pendingRecurring.RemoveAll(r => r.issueId == issueId);
    }
}
