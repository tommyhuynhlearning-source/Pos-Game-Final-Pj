using System.Linq;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Logic;

namespace POSTechSupport.Managers
{
    /// <summary>
    /// A static help-doc index the PLAYER searches when stuck — unrelated to the customer AI's
    /// GroundTruth (Docs/manager.md KnowledgeBaseManager). Pure lookup: it knows nothing about
    /// "day" or difficulty tiers; ProblemAssembler decides WHEN to attach an article.
    /// Add articles to ContentDatabaseSO to populate it.
    /// </summary>
    public class KnowledgeBaseManager : IGuidanceSource
    {
        private readonly ContentDatabaseSO content;
        public KnowledgeBaseManager(ContentDatabaseSO content) { this.content = content; }

        private KnowledgeArticleSO[] All =>
            content.knowledgeArticles ?? System.Array.Empty<KnowledgeArticleSO>();

        public KnowledgeArticleSO[] SearchByCategory(IssueCategory c) =>
            All.Where(a => a != null && a.category == c).ToArray();

        /// <summary>Case/whitespace tolerant — a player typing "code 39" should not get an empty page.</summary>
        public KnowledgeArticleSO[] SearchByErrorCode(string code)
        {
            string q = (code ?? "").Trim();
            if (q.Length == 0) return System.Array.Empty<KnowledgeArticleSO>();
            return All.Where(a => a != null && a.relatedErrorCodes != null &&
                                  a.relatedErrorCodes.Any(c => string.Equals(c, q, System.StringComparison.OrdinalIgnoreCase)))
                      .ToArray();
        }

        /// <summary>
        /// The onboarding article authored FOR this issue, or null. Matched on issueId, never on
        /// category — P1/P2 are both Printer and P5/P7 are both POS, so a category match would make the
        /// result depend on array order (a paper-out ticket could get handed the driver article).
        /// </summary>
        public KnowledgeArticleSO FindGuidanceArticle(IssueSO issue) =>
            issue == null ? null : All.FirstOrDefault(
                a => a != null && a.guidanceForIssueIds != null && a.guidanceForIssueIds.Contains(issue.issueId));
    }
}
