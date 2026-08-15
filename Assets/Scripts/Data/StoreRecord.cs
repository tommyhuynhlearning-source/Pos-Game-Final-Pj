using System;

namespace POSTechSupport.Data
{
    /// <summary>
    /// One CRM account as the game USES it at runtime, rather than as it is authored.
    ///
    /// The CRM directory has to be rolled per campaign (a different shop on every call, with
    /// deliberately confusable neighbours), and P6 forbids putting rolled state in a ScriptableObject —
    /// so a record is a plain object. StoreProfileSO stays the AUTHORED shape (a hand-written fixed
    /// account, still usable as a template or a scripted directory) and converts into this via
    /// <see cref="From"/>. See Docs/schema.md §5.
    /// </summary>
    [Serializable]
    public class StoreRecord
    {
        public string storeId;
        public string storeName;
        public string ownerName;
        public string phoneNumber;
        public string address;
        /// <summary>
        /// The site's fixed device ID, like a TeamViewer/AnyDesk ID. This is ALL the CRM knows about
        /// remote access, on purpose: the session passcode is generated on the customer's screen and has
        /// to be read out over the phone. A passcode stored per record would be a value the game prints
        /// under "Remote credentials" and then refuses — indistinguishable from a bug, and unfair in a
        /// way no amount of verifying can see through.
        /// </summary>
        public string remoteId;
        public MachineConfig[] machines;

        /// <summary>
        /// Which authored name family this account came from — generation bookkeeping, never shown to the
        /// player. It is what lets StoreDirectory.PickConfusable prefer a genuine sibling ("Fairview
        /// Bookshop") over an account that merely shares a trade word ("Station Road Bookshop").
        /// Null on authored records, which have no family.
        /// </summary>
        public string familyKey;

        /// <summary>The register on file. Callers all report the baseline's register id — see StoreDirectoryFactory.</summary>
        public string MachineId => machines != null && machines.Length > 0 && machines[0] != null
            ? machines[0].machineId : "REG-1";

        public static StoreRecord From(StoreProfileSO so) => so == null ? null : new StoreRecord
        {
            storeId = so.storeId,
            storeName = so.storeName,
            ownerName = so.ownerName,
            phoneNumber = so.phoneNumber,
            address = so.address,
            remoteId = so.remoteId,
            machines = so.machines,
        };
    }
}
