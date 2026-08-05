using UnityEngine;
using POSTechSupport.Core;

namespace POSTechSupport.Data
{
    /// <summary>
    /// The single source of truth for one problem (GDD nguyên tắc bất biến #1). 4 tầng:
    /// faults (inject) → symptoms (what's seen) → clues (what diagnosis reveals) → resolution.
    /// See Docs/schema.md §5 and the P1–P7 sample set in GDD §14.
    /// </summary>
    [CreateAssetMenu(menuName = "POS/Issue", fileName = "Issue_")]
    public class IssueSO : ScriptableObject
    {
        public string issueId;
        public IssueCategory category;
        public DifficultyTier tier;
        public bool isBlocker;

        public FaultInjection[] faults;            // TẦNG 1
        public Symptom[] symptoms;                 // TẦNG 2
        public DiagnosticClue[] clues;             // TẦNG 3
        public ResolutionCondition resolution;     // TẦNG 4

        public string[] blockedByIssueIds;         // hidden until these issue ids are fixed
        public FaultInjection[] worseningFaults;   // injected when the player does something risky/wrong
    }
}
