using UnityEngine;

namespace POSTechSupport.Data
{
    /// <summary>
    /// A store account as it appears in the CRM directory. remoteId + machines' baselines feed
    /// ProblemGenerator. Decoy records (similar names, different addresses) live in the same
    /// directory so search can return multiple hits — the player verifies via click-to-compare.
    /// See Docs/schema.md §5.
    /// </summary>
    [CreateAssetMenu(menuName = "POS/Store", fileName = "Store_")]
    public class StoreProfileSO : ScriptableObject
    {
        public string storeId;
        public string storeName;
        public string ownerName;
        public string phoneNumber;
        public string address;
        public string remoteId;        // fixed device ID, like a TeamViewer/AnyDesk ID
        public string fixedPasscode;   // shown for CRM decoys; the real record uses the ticket's one-time code
        public bool isRealAccount;     // true for the genuine record, false for CRM decoys

        public MachineConfig[] machines;
    }
}
