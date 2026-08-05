using System.Collections.Generic;
using System.Linq;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Logic;
using POSTechSupport.Simulation;

namespace POSTechSupport.Managers
{
    /// <summary>
    /// Owns desktop building + the fix aftermath (Docs/manager.md DesktopManager). EffectiveStatus is
    /// delegated to DependencyGraph — the ONE place the Blocked/Error cascade is decided. OnFixApplied
    /// runs the Latent→Active reveal after each fix (GDD §7).
    /// </summary>
    public class DesktopManager
    {
        public VirtualDesktopInstance Build(ModuleBaseline baseline, IEnumerable<IssueSO> issues)
        {
            var d = VirtualDesktopInstance.BuildFresh();
            foreach (var issue in issues)
                if (issue.faults != null)
                    foreach (var f in issue.faults) d.Apply(f);
            return d;
        }

        public StatusResult EffectiveStatus(VirtualDesktopInstance d, ModuleType m) => d.EffectiveStatus(m);

        public void ApplyChange(VirtualDesktopInstance d, FaultInjection change) => d.Apply(change);

        /// <summary>
        /// After a fix: mark faults resolved, then unblock any Latent fault whose blockers are now
        /// resolved (Latent→Active — a newly revealed problem). Mirrors GDD §7 OnFixApplied.
        /// </summary>
        public void OnFixApplied(ProblemInstance p)
        {
            var resolvedIds = new HashSet<string>();
            foreach (var f in p.faults)
            {
                var verdict = ResolutionChecker.EvaluateIssue(p.desktop, f.issue);
                if (verdict == ResolveStatus.Resolved)
                {
                    f.status = FaultStatus.Resolved;
                    resolvedIds.Add(f.issue.issueId);
                }
            }
            foreach (var f in p.faults.Where(f => f.status == FaultStatus.Latent))
            {
                f.blockedBy.RemoveAll(id => resolvedIds.Contains(id));
                if (f.blockedBy.Count == 0) f.status = FaultStatus.Active;
            }
        }
    }
}
