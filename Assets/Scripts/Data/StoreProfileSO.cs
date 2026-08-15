using UnityEngine;

namespace POSTechSupport.Data
{
    /// <summary>
    /// An AUTHORED store account. The runtime CRM directory is rolled by StoreDirectoryFactory into
    /// <see cref="StoreRecord"/>s, so this asset's remaining job is to be the template account: the
    /// machine baseline every generated record and every simulated desktop is built from.
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
        public string remoteId;        // fixed device ID; the session passcode is never on file — see StoreRecord

        public MachineConfig[] machines;
    }
}
