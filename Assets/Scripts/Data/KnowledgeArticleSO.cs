using UnityEngine;
using POSTechSupport.Core;

namespace POSTechSupport.Data
{
    /// <summary>
    /// A help-doc article the PLAYER (not the customer AI) can look up when stuck.
    /// Unrelated to DialoguePolicy / customer GroundTruth. See Docs/schema.md §5.
    /// </summary>
    [CreateAssetMenu(menuName = "POS/KnowledgeArticle", fileName = "KB_")]
    public class KnowledgeArticleSO : ScriptableObject
    {
        public string articleId;
        public string title;
        public IssueCategory category;
        [TextArea] public string content;
        public string[] relatedErrorCodes;

        /// <summary>
        /// Issues this article is the ONBOARDING guidance for (auto-attached during the first pool tier).
        /// Explicit rather than inferred from <see cref="category"/>: several issues share a category
        /// (P1/P2 are both Printer, P5/P7 both POS), so a category match would hand the player whichever
        /// article happens to sit first in the array. Empty = lookup-only, never auto-attached.
        /// </summary>
        public string[] guidanceForIssueIds;
    }
}
