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

        [Tooltip("Confusable name families to roll into the CRM directory (each yields 2–4 accounts).")]
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
    }
}
