using System.Linq;
using UnityEngine;

namespace POSTechSupport.Data
{
    /// <summary>
    /// Central registry of all authored content, wired once in the scene and read by ProblemGenerator,
    /// ActionManager, KnowledgeBaseManager, etc. Replaces the prototype's top-of-file ISSUES / STORE /
    /// PERSONA / STORE_DIRECTORY / ACTIONS constants with SO references.
    /// </summary>
    [CreateAssetMenu(menuName = "POS/ContentDatabase", fileName = "ContentDatabase")]
    public class ContentDatabaseSO : ScriptableObject
    {
        public GameConfigSO config;

        [Header("Store / CRM")]
        public StoreProfileSO realStore;               // template account: supplies the machine baseline
        public StoreProfileSO[] crmDecoys;             // only used when crmClusterCount = 0 (scripted directory)

        [Tooltip("Confusable name families to roll into the CRM directory (each yields 2–4 accounts). " +
                 "0 = use the authored realStore + crmDecoys instead.")]
        public int crmClusterCount = 6;
        public StoreNameTableSO storeNames;            // word lists the directory is combined from

        [Header("People")]
        public PersonaProfileSO[] personaPool;
        public string[] staffCallerNames = { "Jenny Park", "Carlos Ibarra", "Deshawn Miller" };

        [Header("Problems / Actions / Docs")]
        public IssueSO[] allIssues;
        public DesktopActionSO[] allActions;
        public ReceiptTemplateSO[] receiptTemplates;
        public KnowledgeArticleSO[] knowledgeArticles;

        public IssueSO FindIssue(string issueId) =>
            allIssues?.FirstOrDefault(i => i != null && i.issueId == issueId);

        /// <summary>
        /// The AUTHORED directory (real record first, then decoys), as runtime records. Only the
        /// fallback path uses this; normally StoreDirectoryFactory rolls the directory from
        /// <see cref="storeNames"/> so a different shop is on the phone each call.
        /// </summary>
        public System.Collections.Generic.List<StoreRecord> CrmDirectory()
        {
            var list = new System.Collections.Generic.List<StoreRecord>();
            if (realStore != null) list.Add(StoreRecord.From(realStore));
            if (crmDecoys != null) list.AddRange(crmDecoys.Where(s => s != null).Select(StoreRecord.From));
            return list;
        }
    }
}
