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
        public string remoteId;        // fixed device ID, like a TeamViewer/AnyDesk ID
        public string fixedPasscode;   // what the record shows when it ISN'T tonight's caller
        public bool isRealAccount;     // authored-asset flag only; see ProblemInstance.IsCallerRecord
        public MachineConfig[] machines;

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
            fixedPasscode = so.fixedPasscode,
            isRealAccount = so.isRealAccount,
            machines = so.machines,
        };
    }
}
